using UnityEngine;
using Photon.Pun;
using UnityEngine.AI;

[RequireComponent(typeof(PhotonView))]
[RequireComponent(typeof(NavMeshAgent))]
public class NPCNetworkController : MonoBehaviourPunCallbacks, IPunObservable
{
    private NavMeshAgent agent;
    private PhotonView photonView;
    private Animator animator;
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private Vector3 networkVelocity;
    private Vector3 networkDestination;
    private bool networkIsDead;
    private float networkHorizontal;
    private float networkVertical;
    private bool networkIsRunning;
    private int uniqueID;

    // Network sync settings
    private const float SYNC_RATE = 20f; // Increased to 20 times per second
    private float nextSyncTime = 0f;
    private float lastNetworkUpdate;
    private Vector3 previousNetworkPosition;
    private Quaternion previousNetworkRotation;
    private float interpolationBackTime = 0.1f; // 100ms interpolation time
    private float extrapolationLimit = 0.5f; // Limit extrapolation to 500ms
    private float positionLerpSpeed = 15f; // Faster position lerp
    private float rotationLerpSpeed = 15f; // Faster rotation lerp

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        photonView = GetComponent<PhotonView>();
        uniqueID = photonView.ViewID;
        
        // Get animator from this object or children
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        
        // Initialize network variables
        networkPosition = transform.position;
        networkRotation = transform.rotation;
        networkDestination = transform.position;
        previousNetworkPosition = networkPosition;
        previousNetworkRotation = networkRotation;
        lastNetworkUpdate = Time.time;
    }

    void Start()
    {
        // Set initial agent values
        if (agent != null)
        {
            agent.avoidancePriority = Random.Range(20, 80); // Different priorities to avoid stacking
            
            // Configure client-side NPCs
            if (!PhotonNetwork.IsMasterClient)
            {
                agent.updatePosition = false;
                agent.updateRotation = false;
                agent.updateUpAxis = false;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance; // Let server handle avoidance
            }
            else
            {
                // Server-side settings
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            }
        }
        
        Debug.Log($"NPC {uniqueID} network controller initialized. Is master: {PhotonNetwork.IsMasterClient}");
    }

    void Update()
    {
        // Only update on clients
        if (!PhotonNetwork.IsMasterClient)
        {
            UpdateClientPosition();
        }
    }

    private void UpdateClientPosition()
    {
        if (networkIsDead) return;

        float timeSinceLastUpdate = Time.time - lastNetworkUpdate;

        // Calculate target position with extrapolation
        Vector3 targetPosition = networkPosition;
        Quaternion targetRotation = networkRotation;

        if (timeSinceLastUpdate < extrapolationLimit)
        {
            // Extrapolate position based on velocity
            float extrapolationTime = timeSinceLastUpdate - interpolationBackTime;
            if (extrapolationTime > 0f)
            {
                targetPosition += networkVelocity * extrapolationTime;
            }
        }

        // Smoothly update position and rotation with dynamic speed
        float lerpFactor = timeSinceLastUpdate > 0.2f ? 1f : Time.deltaTime * positionLerpSpeed;
        transform.position = Vector3.Lerp(transform.position, targetPosition, lerpFactor);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * rotationLerpSpeed);

        // Update animation parameters
        if (animator != null)
        {
            animator.SetFloat("Horizontal", networkHorizontal);
            animator.SetFloat("Vertical", networkVertical);
            animator.SetBool("Running", networkIsRunning);
        }

        // Update NavMeshAgent
        if (agent != null && agent.isOnNavMesh)
        {
            // Update agent's position
            agent.nextPosition = transform.position;

            // Only update destination if significantly different
            float distanceToDestination = Vector3.Distance(agent.destination, networkDestination);
            if (distanceToDestination > 0.5f)
            {
                NavMeshPath path = new NavMeshPath();
                if (NavMesh.CalculatePath(transform.position, networkDestination, NavMesh.AllAreas, path))
                {
                    agent.destination = networkDestination;
                }
            }
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Send data (from MasterClient to others)
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(agent.velocity);
            stream.SendNext(agent.destination);
            
            // Send animation data
            stream.SendNext(networkHorizontal);
            stream.SendNext(networkVertical);
            stream.SendNext(networkIsRunning);
            
            // Important: Send isDead state
            NPCHealth health = GetComponent<NPCHealth>();
            bool isDead = (health != null) ? health.IsDead() : false;
            stream.SendNext(isDead);
        }
        else
        {
            // Store previous values for interpolation
            previousNetworkPosition = networkPosition;
            previousNetworkRotation = networkRotation;

            // Receive data (on non-MasterClient)
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            networkVelocity = (Vector3)stream.ReceiveNext();
            networkDestination = (Vector3)stream.ReceiveNext();
            
            // Receive animation data
            networkHorizontal = (float)stream.ReceiveNext();
            networkVertical = (float)stream.ReceiveNext();
            networkIsRunning = (bool)stream.ReceiveNext();
            networkIsDead = (bool)stream.ReceiveNext();

            // Update timing for interpolation/extrapolation
            lastNetworkUpdate = Time.time;

            // If position changed drastically, teleport instead of interpolate
            if (Vector3.Distance(transform.position, networkPosition) > 5f)
            {
                transform.position = networkPosition;
                transform.rotation = networkRotation;
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.Warp(networkPosition);
                }
            }
        }
    }
}