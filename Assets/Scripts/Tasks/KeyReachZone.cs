using System;
using System.Collections.Generic;
using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Counts attempts to reach the key by hand.
    ///
    /// Put on a trigger collider around the key and the front of the shelf
    /// (larger than the KeyKnockZone). Each frame every assigned hand point is
    /// tested against the collider; a hand going from outside to inside counts
    /// as one reach attempt. Polling the transforms means the hands need no
    /// colliders or Rigidbodies.
    ///
    /// <see cref="KeyTaskBridge"/> subscribes to <see cref="ReachAttempt"/> and
    /// writes the CSV rows.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class KeyReachZone : MonoBehaviour
    {
        /// <summary>A hand entered the zone. Args = hand name, total attempts so far.</summary>
        public event Action<string, int> ReachAttempt;

        [Tooltip("Hand points to test (e.g. LeftHandAnchor / RightHandAnchor, or the palm transforms).")]
        [SerializeField] private List<Transform> hands = new List<Transform>();

        [Tooltip("Per hand: re-entering within this many seconds of leaving doesn't count as a new attempt.")]
        [SerializeField] private float minSecondsBetweenAttempts = 1f;

        [SerializeField] private bool enableDebugLogs;

        private Collider _zone;
        private bool[] _inside;
        private float[] _lastExitAt;
        private int _attemptCount;

        public int AttemptCount => _attemptCount;

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnValidate()
        {
            if (minSecondsBetweenAttempts < 0f) minSecondsBetweenAttempts = 0f;
        }

        private void Awake()
        {
            _zone = GetComponent<Collider>();
            _inside = new bool[hands.Count];
            _lastExitAt = new float[hands.Count];
            for (int i = 0; i < _lastExitAt.Length; i++) _lastExitAt[i] = float.NegativeInfinity;

            if (hands.Count == 0)
            {
                Debug.LogWarning($"[{nameof(KeyReachZone)}] No hands assigned on {name}. Reach attempts won't be counted.", this);
            }
        }

        private void OnDisable()
        {
            if (_inside == null) return;
            for (int i = 0; i < _inside.Length; i++) _inside[i] = false;
        }

        private void Update()
        {
            if (_zone == null || !_zone.enabled) return;

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

                    _attemptCount++;

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[{nameof(KeyReachZone)}] Reach attempt #{_attemptCount} by '{hand.name}'.", this);
                    }

                    ReachAttempt?.Invoke(hand.name, _attemptCount);
                }
                else
                {
                    SetOutside(i);
                }
            }
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
