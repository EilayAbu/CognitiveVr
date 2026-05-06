using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class QuickInteraction : MonoBehaviour
{
    public UnityEvent onCollide;
    public UnityEvent onTrigger;

    private void OnCollisionEnter(Collision collision)
    {
        onCollide.Invoke();
    }
    private void OnTriggerEnter(Collider other)
    {
        onTrigger.Invoke();
    }
}