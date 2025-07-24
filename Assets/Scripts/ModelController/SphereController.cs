using UnityEngine;
using Photon.Pun;

public class SphereController : MonoBehaviourPunCallbacks
{
    [Header("Movement Settings")]
    [SerializeField] private float rotationSpeed = 150f;    // Slower rotation for big ball
    [SerializeField] private float jumpForce = 500f;        // Increased jump force for more visible multiplayer jumps
    
    [Header("Float Settings")]
    [SerializeField] private float floatHeight = 5f;        // Higher float for big ball
    [SerializeField] private float hoverForce = 50f;        // Stronger hover force
    [SerializeField] private float returnSpeed = 15f;       // Stronger return force

    [Header("Multiplayer Settings")]
    [SerializeField] private float minJumpInterval = 0.1f;  // Minimum time between network jumps
    
    private Rigidbody rb;
    private PhotonView photonView;
    private float startingHeight;
    private float lastNetworkJumpTime;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        photonView = GetComponent<PhotonView>();

        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        // Configure Rigidbody settings for large ball
        rb.constraints = RigidbodyConstraints.FreezePositionZ;
        rb.mass = 5f;                   // Heavier mass for big ball
        rb.useGravity = false;
        rb.drag = 0.5f;                   // Reduced drag for better network physics
        rb.angularDrag = 1.5f;          // Added angular drag for controlled rotation
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // Better collision for multiplayer

        startingHeight = transform.position.y;
    }

    void FixedUpdate()
    {
        if (!photonView.IsMine) return;

        float currentHeight = transform.position.y;
        float heightDifference = startingHeight + floatHeight - currentHeight;

        // Stronger stabilizing force for large ball
        Vector3 targetPosition = new Vector3(transform.position.x, startingHeight + floatHeight, transform.position.z);
        Vector3 moveDirection = (targetPosition - transform.position);
        
        // Lighter damping for better network synchronization
        Vector3 dampedForce = moveDirection * returnSpeed - rb.velocity * 0.1f;
        rb.AddForce(dampedForce * rb.mass, ForceMode.Force);
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        // Handle rotation with momentum for big ball
        float horizontalInput = Input.GetAxis("Horizontal");
        if (horizontalInput != 0)
        {
            photonView.RPC("RotateSphere", RpcTarget.All, horizontalInput);
        }

        // Handle jumping with network rate limiting
        if (Input.GetKeyDown(KeyCode.Space))
        {
            float timeSinceLastJump = Time.time - lastNetworkJumpTime;
            if (timeSinceLastJump >= minJumpInterval)
            {
                photonView.RPC("JumpSphere", RpcTarget.All);
                lastNetworkJumpTime = Time.time;
            }
        }
    }

    [PunRPC]
    private void RotateSphere(float direction)
    {
        if (!rb) return; // Safety check
        // Smoother rotation for big ball
        rb.AddTorque(Vector3.forward * -direction * rotationSpeed * Time.deltaTime * rb.mass, ForceMode.Force);
    }

    [PunRPC]
    private void JumpSphere()
    {
        if (!rb) return; // Safety check
        
        // Add a small random offset to prevent exact synchronization
        float randomOffset = Random.Range(-0.1f, 0.1f);
        Vector3 jumpVector = Vector3.up * (jumpForce + randomOffset);
        
        // Apply the jump force
        rb.AddForce(jumpVector * rb.mass, ForceMode.Impulse);
    }

    void OnCollisionEnter(Collision collision)
    {
        // Handle collisions between players
        if (collision.gameObject.GetComponent<SphereController>())
        {
            // Add some bounce effect between players
            Vector3 bounceDirection = (transform.position - collision.transform.position).normalized;
            rb.AddForce(bounceDirection * jumpForce * 0.5f, ForceMode.Impulse);
        }
    }

    void OnDrawGizmos()
    {
        // Draw larger visualization for big ball
        Gizmos.color = Color.green;
        Vector3 startPos = transform.position;
        startPos.y = startingHeight + floatHeight;
        Gizmos.DrawLine(startPos - Vector3.right * 2f, startPos + Vector3.right * 2f);
        
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, transform.localScale.x / 2f);
    }
}
