using UnityEngine;
[RequireComponent(typeof(MeshFilter))]
public class NoCull : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        var mf = GetComponent<MeshFilter>();
        var m = mf.mesh;
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
