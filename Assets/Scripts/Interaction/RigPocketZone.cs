using Oculus.Interaction;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Trigger volume for returning the phone. When the assigned item enters the
    /// trigger, it is deactivated (SetActive(false)). That's it.
    ///
    /// Setup:
    ///   1. GameObject with a Collider, "Is Trigger" ticked.
    ///   2. Add this component, drag the phone into "Item".
    ///
    /// The item needs a (kinematic) Rigidbody so trigger events fire, and its layer
    /// must be allowed against this zone's layer in the Physics collision matrix.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class RigPocketZone : MonoBehaviour
    {
        [Tooltip("The item that gets deactivated when it enters this zone.")]
        [SerializeField] private GameObject item;
        
        [Tooltip("The item that gets deactivated when it enters this zone.")]
        [SerializeField] private RigPocket pocketConnection;

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (item == null)
            {
                return;
            }

            if (other.transform == item.transform || other.transform.IsChildOf(item.transform))
            {
                pocketConnection.permanentItem = item.GetComponent<Grabbable>();
                
            }
        }
    }
}
