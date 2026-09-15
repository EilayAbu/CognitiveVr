using System.Collections.Generic;
using UnityEngine;

namespace CognitiveVR.Core
{
    /// <summary>
    /// Marks a region of the scene as "out of bounds" for tracked props.
    /// Put this on a volume object (e.g. wallOutBoundry): any
    /// <see cref="RespawnableObject"/> whose sample point ends up inside the
    /// volume - or below <see cref="respawnBelowWorldY"/> - is sent back to the
    /// pose it started the session in.
    ///
    /// The volume itself owns the floor threshold so it is configured once for
    /// the whole scene instead of on every prop.
    ///
    /// Several volumes may be active at the same time; they all register into a
    /// static list that <see cref="RespawnableObject"/> queries through
    /// <see cref="IsOutOfBounds"/>.
    /// </summary>
    public class OutOfBoundsVolume : MonoBehaviour
    {
        [Header("Volume")]
        [Tooltip("Extra margin (meters) around the volume colliders. A point within this distance of a collider already counts as out of bounds.")]
        [SerializeField] private float padding = 0f;

        [Header("Floor")]
        [Tooltip("Also treat anything that falls below a world Y height as out of bounds.")]
        [SerializeField] private bool enableFloorCheck = true;

        [Tooltip("World Y height that counts as 'fell through the floor'. Set it slightly below the room floor.")]
        [SerializeField] private float respawnBelowWorldY = -0.5f;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs;

        private static readonly List<OutOfBoundsVolume> ActiveVolumes = new List<OutOfBoundsVolume>();

        private Collider[] _colliders;

        /// <summary>World Y height under which props are sent home.</summary>
        public float FloorThreshold => respawnBelowWorldY;

        private void Awake()
        {
            CacheColliders();
        }

        private void OnEnable()
        {
            if (!ActiveVolumes.Contains(this))
            {
                ActiveVolumes.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveVolumes.Remove(this);
        }

        /// <summary>
        /// Re-reads the colliders of this object and its children. Call it if
        /// the volume's colliders are created or swapped at runtime.
        /// </summary>
        public void CacheColliders()
        {
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        /// <summary>
        /// True while <paramref name="worldPoint"/> is inside any of the volume's
        /// colliders (rotation and scale included), or inside the transform box
        /// when the object has no collider at all.
        /// </summary>
        public bool ContainsPoint(Vector3 worldPoint)
        {
            if (_colliders == null)
            {
                CacheColliders();
            }

            bool anyUsableCollider = false;
            float paddingSqr = padding * padding;

            foreach (Collider col in _colliders)
            {
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                {
                    continue;
                }

                anyUsableCollider = true;

                if (SupportsClosestPoint(col))
                {
                    // ClosestPoint returns the point itself when it is inside the
                    // collider, which makes this an exact test for rotated and
                    // scaled boxes, spheres, capsules and convex meshes.
                    if ((col.ClosestPoint(worldPoint) - worldPoint).sqrMagnitude <= paddingSqr)
                    {
                        return true;
                    }
                }
                else
                {
                    // Concave mesh colliders do not support ClosestPoint, so fall
                    // back to their world axis-aligned bounds.
                    Bounds bounds = col.bounds;
                    bounds.Expand(padding * 2f);
                    if (bounds.Contains(worldPoint))
                    {
                        return true;
                    }
                }
            }

            return !anyUsableCollider && ContainsInTransformBox(worldPoint);
        }

        private static bool SupportsClosestPoint(Collider col)
        {
            return !(col is MeshCollider meshCollider) || meshCollider.convex;
        }

        /// <summary>
        /// Fallback for a volume object without colliders: the transform is read
        /// as a 1x1x1 cube, exactly like Unity's default Cube primitive, so
        /// position / rotation / scale in the Inspector define the region.
        /// </summary>
        private bool ContainsInTransformBox(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 half = Vector3.one * 0.5f;
            Vector3 lossy = transform.lossyScale;
            half.x += SafeLocalPadding(lossy.x);
            half.y += SafeLocalPadding(lossy.y);
            half.z += SafeLocalPadding(lossy.z);

            return Mathf.Abs(local.x) <= half.x
                && Mathf.Abs(local.y) <= half.y
                && Mathf.Abs(local.z) <= half.z;
        }

        private float SafeLocalPadding(float scaleComponent)
        {
            return Mathf.Abs(scaleComponent) > Mathf.Epsilon ? padding / Mathf.Abs(scaleComponent) : 0f;
        }

        /// <summary>
        /// Single entry point for props: true when the point is inside any active
        /// volume or below any active volume's floor threshold.
        /// <paramref name="reason"/> is filled with a short description for logs.
        /// </summary>
        public static bool IsOutOfBounds(Vector3 worldPoint, out string reason)
        {
            return IsBelowAnyFloor(worldPoint, out reason) || IsInsideAnyVolume(worldPoint, out reason);
        }

        /// <summary>
        /// Volume containment only. Used by props that replace the shared floor
        /// threshold with their own.
        /// </summary>
        public static bool IsInsideAnyVolume(Vector3 worldPoint, out string reason)
        {
            for (int i = 0; i < ActiveVolumes.Count; i++)
            {
                OutOfBoundsVolume volume = ActiveVolumes[i];
                if (volume != null && volume.ContainsPoint(worldPoint))
                {
                    reason = $"volume:{volume.name}";
                    return true;
                }
            }

            reason = null;
            return false;
        }

        /// <summary>Floor threshold of any active volume only.</summary>
        public static bool IsBelowAnyFloor(Vector3 worldPoint, out string reason)
        {
            for (int i = 0; i < ActiveVolumes.Count; i++)
            {
                OutOfBoundsVolume volume = ActiveVolumes[i];
                if (volume != null && volume.enableFloorCheck && worldPoint.y < volume.respawnBelowWorldY)
                {
                    reason = $"below_floor:{volume.respawnBelowWorldY:F2}";
                    return true;
                }
            }

            reason = null;
            return false;
        }

        /// <summary>
        /// Trigger fast path: only fires when this volume's collider has Is
        /// Trigger checked, and only shortens the reaction time - the periodic
        /// check inside <see cref="RespawnableObject"/> catches the same case.
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            RespawnableObject prop = other.GetComponentInParent<RespawnableObject>();
            if (prop == null)
            {
                return;
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(OutOfBoundsVolume)}] '{prop.name}' entered '{name}'.", this);
            }

            prop.RequestRespawn($"volume:{name}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            bool drewCollider = false;
            foreach (Collider col in colliders)
            {
                if (col is BoxCollider box)
                {
                    Matrix4x4 prev = Gizmos.matrix;
                    Gizmos.matrix = col.transform.localToWorldMatrix;
                    Gizmos.DrawWireCube(box.center, box.size);
                    Gizmos.matrix = prev;
                    drewCollider = true;
                }
                else if (col != null)
                {
                    Bounds bounds = col.bounds;
                    Gizmos.DrawWireCube(bounds.center, bounds.size);
                    drewCollider = true;
                }
            }

            if (!drewCollider)
            {
                Matrix4x4 prev = Gizmos.matrix;
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                Gizmos.matrix = prev;
            }

            if (!enableFloorCheck)
            {
                return;
            }

            // Floor threshold: a flat square under the volume so the height can
            // be lined up with the room floor by eye.
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            Vector3 center = new Vector3(transform.position.x, respawnBelowWorldY, transform.position.z);
            Gizmos.DrawWireCube(center, new Vector3(4f, 0f, 4f));
        }
#endif
    }
}
