using UnityEngine;
using System.Collections;

public class WalkAndDestroy : MonoBehaviour
{
    public Transform pointA;
    public Transform pointB;
    public float moveSpeed = 2f;
    public float turnSpeed = 360f; // deg/sec
    public float reachDistance = 0.05f;

    // Call this from your other code when ready
    public void Begin()
    {
        StopAllCoroutines();
        StartCoroutine(Routine());
    }

    IEnumerator Routine()
    {
        yield return MoveTo(pointA.position);
        yield return MoveTo(pointB.position);
        Destroy(gameObject);
    }

    IEnumerator MoveTo(Vector3 target)
    {
        target.y = transform.position.y; // lock Y

        // turn to face target first
        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(dir);
            while (Quaternion.Angle(transform.rotation, look) > 1f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, turnSpeed * Time.deltaTime);
                yield return null;
            }
            transform.rotation = look;
        }

        // walk to target
        while (Vector3.Distance(transform.position, target) > reachDistance)
        {
            transform.position = Vector3.MoveTowards(
                transform.position, target, moveSpeed * Time.deltaTime);
            yield return null;
        }
    }
}