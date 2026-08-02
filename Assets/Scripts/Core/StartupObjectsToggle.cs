using System.Collections.Generic;
using UnityEngine;

namespace CognitiveVR.Core
{
    /// <summary>
    /// Disables a list of objects at scene start (Awake),
    /// and exposes a public method to re-enable them via a button OnClick event.
    /// </summary>
    public class StartupObjectsToggle : MonoBehaviour
    {
        [Header("Objects to disable at scene start")]
        [SerializeField] private List<GameObject> _objects = new List<GameObject>();

        private void Awake()
        {
            DisableAll();
        }

        public void DisableAll()
        {
            foreach (var go in _objects)
            {
                if (go != null)
                    go.SetActive(false);
            }
        }

        public void EnableAll()
        {
            foreach (var go in _objects)
            {
                if (go != null)
                    go.SetActive(true);
            }
        }
    }
}
