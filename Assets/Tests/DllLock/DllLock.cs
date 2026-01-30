using UnityEngine;

public class DllLock : MonoBehaviour
{
    void Awake()
    {
        var rb = gameObject.AddComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero;
    }
}