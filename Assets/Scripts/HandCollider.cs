using UnityEngine;

/// <summary>
/// Attach this script to your Left and Right Controller objects (or hand models)
/// in the Unity Editor. It adds a small invisible collider that can trigger
/// menu buttons via physics overlaps (OnTriggerEnter).
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class HandCollider : MonoBehaviour
{
    [Tooltip("Size of the invisible poke area (in meters).")]
    public float radius = 0.05f; // 5 cm radius

    private void Awake()
    {
        // Get or add the sphere collider
        SphereCollider col = GetComponent<SphereCollider>();
        
        // Make sure it's a trigger so it doesn't bump physical objects
        col.isTrigger = true;
        
        // Set the size to represent a hand / finger poke
        col.radius = radius;

        // Ensure there is a Rigidbody, so collisions are reliably detected if the object moves fast
        // (Even though the menu button also has a Kinematic Rigidbody, it's good practice 
        // to have a Kinematic Rigidbody on the moving hand).
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
        rb.useGravity = false;
    }
}
