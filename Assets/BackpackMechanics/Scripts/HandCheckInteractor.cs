using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandCheckInteractor : MonoBehaviour
{
    // public class HandCheckInteractor : MonoBehaviour 
    private QuickInteraction quickInteraction;

    void Start()
    {
        quickInteraction = GetComponent<QuickInteraction>();
        quickInteraction.enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<OVRHand>() != null)
        {
            quickInteraction.enabled = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<OVRHand>() != null)
        {
            quickInteraction.enabled = false;
        }
    }
}