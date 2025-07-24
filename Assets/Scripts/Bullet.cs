using UnityEngine;
using Photon.Pun;

public class Bullet : MonoBehaviour
{
    public int damage = 20;
    public float speed = 30f;
    private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        // Ensure the bullet stays horizontal
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        }

        // Set initial position slightly above ground
        transform.position = new Vector3(transform.position.x, 1f, transform.position.z);
    }

    private void Update()
    {
        // Maintain horizontal movement
        if (rb != null)
        {
            Vector3 velocity = rb.velocity;
            velocity.y = 0;
            rb.velocity = velocity;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Check if we hit a player
        PlayerHealth playerHealth = collision.gameObject.GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            // Apply damage
            playerHealth.TakeDamage(damage, "NPC");
            
            // Destroy bullet immediately
            Destroy(gameObject);
            return;
        }

        // If we hit anything else, destroy after a very short delay
        Destroy(gameObject, 0.1f);
    }
}
