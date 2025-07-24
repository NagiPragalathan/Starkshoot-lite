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

    private const float SYNC_RATE = 5f; // times per second
    private float nextSyncTime = 0f;

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
    }

    void Start()
    {
        // Set initial agent values
        if (agent != null)
        {
            agent.avoidancePriority = Random.Range(20, 80); // Different priorities to avoid stacking
            
            // Client-side NPCs should not update position directly
            if (!PhotonNetwork.IsMasterClient)
            {
                agent.updatePosition = false;
                agent.updateRotation = false;
            }
        }
        
        Debug.Log($"NPC {uniqueID} network controller initialized. Is master: {PhotonNetwork.IsMasterClient}");
    }

    void Update()
    {
        // Only update on clients
        if (!PhotonNetwork.IsMasterClient)
        {
            // Smoothly update position and rotation
            transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * 10f);
            transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation, Time.deltaTime * 10f);
            
            // Update animation parameters
            if (animator != null)
            {
                animator.SetFloat("Horizontal", networkHorizontal);
                animator.SetFloat("Vertical", networkVertical);
                animator.SetBool("Running", networkIsRunning);
            }
            
            // Update agent position
            if (agent != null && agent.isOnNavMesh)
            {
                agent.nextPosition = transform.position;
                
                // Only update destination if it's significantly different to avoid jitter
                if (Vector3.Distance(agent.destination, networkDestination) > 1f)
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
            
            // Important: Send isDead state
            NPCHealth health = GetComponent<NPCHealth>();
            bool isDead = (health != null) ? health.IsDead() : false;
            stream.SendNext(isDead);
            
            // Rest of your syncing code...
        }
        else
        {
            // Receive data (on non-MasterClient)
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            bool isDead = (bool)stream.ReceiveNext();
            
            // If NPC is dead, don't update position/movement
            if (isDead) return;
            
            // Rest of your receiving code...
        }
    }
}