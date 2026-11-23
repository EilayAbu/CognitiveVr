using System;
using System.Collections.Generic;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Counts attempts to reach the key - by hand OR with any object.
    ///
    /// Put on a trigger collider around the key and the front of the shelf
    /// (larger than the KeyKnockZone). Two sources count as an attempt:
    ///  1. Tracked points (e.g. LeftHandAnchor / RightHandAnchor) polled every
    ///     frame against the collider - these need no colliders or Rigidbodies.
    ///  2. Any physics object (umbrella, mop, chair, ...) whose collider enters
    ///     the trigger (needs a Rigidbody, which every grabbable has). Turn off
    ///     with Detect Any Object.
    /// Something going from outside to inside counts as one attempt; re-entering
    /// within Min Seconds Between Attempts does not.
    ///
    /// Objects already inside the zone when it starts (the key itself) and
    /// anything under the same root as a tracked point (the player rig's own
    /// hand colliders, which would double-count) are ignored.
    ///
    /// <see cref="KeyTaskBridge"/> subscribes to <see cref="ReachAttempt"/> and
    /// writes the CSV rows (key_reach_attempt, obj=&lt;name&gt;).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class KeyReachZone : MonoBehaviour
    {
        /// <summary>Something entered the zone. Args = object name, total attempts so far.</summary>
        public event Action<string, int> ReachAttempt;

        /// <summary>
        /// Every tracked hand point has left the zone (none is inside any more).
        /// Physics objects (umbrella, mop, ...) are not part of this. Arg = seconds
        /// a hand was inside. Not raised when the zone is disabled.
        /// </summary>
        public event Action<float> HandsLeftZone;

        // Field keeps its old name so the hand anchors already assigned in the scene are kept.
        [Tooltip("Points polled every frame (e.g. LeftHandAnchor / RightHandAnchor, or the palm transforms). Need no colliders.")]
        [SerializeField] private List<Transform> hands = new List<Transform>();

        [Tooltip("Also count any Rigidbody object (umbrella, mop, chair, ...) whose collider enters the zone.")]
        [SerializeField] private bool detectAnyObject = true;

        [Tooltip("Objects that never count (e.g. the key, the shelf). Matched by Rigidbody root or the object itself.")]
        [SerializeField] private List<Transform> ignoredObjects = new List<Transform>();

        [Tooltip("Anything inside the zone during this many seconds after it is enabled is ignored for good (the key sitting on the shelf).")]
        [SerializeField] private float startupIgnoreSeconds = 0.5f;

        [Tooltip("Per object: re-entering within this many seconds of leaving doesn't count as a new attempt.")]
        [SerializeField] private float minSecondsBetweenAttempts = 1f;

        [SerializeField] private bool enableDebugLogs;

        private Collider _zone;
        private bool[] _inside;
        private float[] _lastExitAt;
        private int _attemptCount;
        private float _enabledAt;
        private float _handsEnteredAt;

        // Physics objects: overlapping collider count and last exit, keyed by root.
        private readonly Dictionary<Transform, int> _overlaps = new Dictionary<Transform, int>();
        private readonly Dictionary<Transform, float> _objLastExitAt = new Dictionary<Transform, float>();
        private readonly HashSet<Transform> _startupIgnored = new HashSet<Transform>();

        public int AttemptCount => _attemptCount;

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnValidate()
        {
            if (minSecondsBetweenAttempts < 0f) minSecondsBetweenAttempts = 0f;
            if (startupIgnoreSeconds < 0f) startupIgnoreSeconds = 0f;
        }

        private void Awake()
        {
            _zone = GetComponent<Collider>();
            _inside = new bool[hands.Count];
            _lastExitAt = new float[hands.Count];
            for (int i = 0; i < _lastExitAt.Length; i++) _lastExitAt[i] = float.NegativeInfinity;

            if (hands.Count == 0 && !detectAnyObject)
            {
                Debug.LogWarning($"[{nameof(KeyReachZone)}] No tracked points and Detect Any Object is off on {name}. Reach attempts won't be counted.", this);
            }
        }

        private void OnEnable()
        {
            _enabledAt = Time.time;
        }

        private void OnDisable()
        {
            if (_inside != null)
                for (int i = 0; i < _inside.Length; i++) _inside[i] = false;
            _overlaps.Clear();
        }

        private void Update()
        {
            if (_zone == null || !_zone.enabled) return;

            bool wasAnyHandInside = AnyHandInside();

            for (int i = 0; i < hands.Count; i++)
            {
                Transform hand = hands[i];
                if (hand == null || !hand.gameObject.activeInHierarchy)
                {
                    SetOutside(i);
                    continue;
                }

                if (IsInside(hand.position))
                {
                    if (_inside[i]) continue;
                    _inside[i] = true;

                    if (Time.time - _lastExitAt[i] < minSecondsBetweenAttempts) continue;

                    CountAttempt(hand.name);
                }
                else
                {
                    SetOutside(i);
                }
            }

            bool isAnyHandInside = AnyHandInside();
            if (!wasAnyHandInside && isAnyHandInside)
            {
                _handsEnteredAt = Time.time;
            }
            else if (wasAnyHandInside && !isAnyHandInside)
            {
                float secondsInside = Time.time - _handsEnteredAt;

                if (enableDebugLogs)
                {
                    Debug.Log($"[{nameof(KeyReachZone)}] All hands left the zone after {secondsInside:0.##}s.", this);
                }

                HandsLeftZone?.Invoke(secondsInside);
            }
        }

        private bool AnyHandInside()
        {
            for (int i = 0; i < _inside.Length; i++)
            {
                if (_inside[i]) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ //
        // Any physics object
        // ------------------------------------------------------------------ //

        private void OnTriggerEnter(Collider other)
        {
            if (!detectAnyObject || !enabled) return;

            Transform root = RootOf(other);
            if (root == null || IsIgnored(root)) return;

            if (Time.time - _enabledAt < startupIgnoreSeconds)
            {
                _startupIgnored.Add(root);
                return;
            }

            _overlaps.TryGetValue(root, out int count);
            _overlaps[root] = count + 1;
            if (count > 0) return; // another collider of the same object - already inside

            if (_objLastExitAt.TryGetValue(root, out float lastExit) &&
                Time.time - lastExit < minSecondsBetweenAttempts)
                return;

            CountAttempt(root.name);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!detectAnyObject) return;

            Transform root = RootOf(other);
            if (root == null || !_overlaps.TryGetValue(root, out int count)) return;

            count--;
            if (count > 0)
            {
                _overlaps[root] = count;
                return;
            }

            _overlaps.Remove(root);
            _objLastExitAt[root] = Time.time;
        }

        private static Transform RootOf(Collider c)
        {
            if (c == null) return null;
            return c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform;
        }

        private bool IsIgnored(Transform root)
        {
            if (_startupIgnored.Contains(root)) return true;

            for (int i = 0; i < ignoredObjects.Count; i++)
            {
                Transform t = ignoredObjects[i];
                if (t != null && (root == t || root.IsChildOf(t))) return true;
            }

            // The player rig's own colliders (hand physics, controllers) would
            // double-count what the tracked points already report.
            for (int i = 0; i < hands.Count; i++)
            {
                Transform h = hands[i];
                if (h != null && root.root == h.root) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ //

        private void CountAttempt(string objectName)
        {
            _attemptCount++;

            if (enableDebugLogs)
            {
                Debug.Log($"[{nameof(KeyReachZone)}] Reach attempt #{_attemptCount} by '{objectName}'.", this);
            }

            ReachAttempt?.Invoke(objectName, _attemptCount);
        }

        private void SetOutside(int i)
        {
            if (!_inside[i]) return;
            _inside[i] = false;
            _lastExitAt[i] = Time.time;
        }

        private bool IsInside(Vector3 point)
        {
            return (_zone.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;
        }
    }
}
