using UnityEngine;

public class DllLock : MonoBehaviour
{
    void Awake()
    {
        var rb = gameObject.AddComponent<Rigidbody>();
        rb.velocity = Vector3.zero;
    }
}