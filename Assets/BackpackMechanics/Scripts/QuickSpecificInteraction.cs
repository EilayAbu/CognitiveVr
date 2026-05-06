using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class QuickSpecificInteraction : MonoBehaviour
{
    public GameObject other;
    public UnityEvent onCollide;
    public UnityEvent onTrigger;

    private void OnCollisionEnter(Collision collision)
    {
        if(other == collision.gameObject)
        {
            onCollide.Invoke();

        }
    }
    private void OnTriggerEnter(Collider other)
    {
        if(other == other.gameObject)
        {
            onTrigger.Invoke();
        }
    }
}