using UnityEngine;
using Photon.Pun;

[RequireComponent(typeof(PhotonView))]
public class MovingPlatform : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Movement Settings")]
    [SerializeField] private bool moveHorizontally = true;
    [SerializeField] private bool moveVertically = false;
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float moveDistance = 5f;
    [SerializeField] private float waitTimeAtEnds = 1f;
    [SerializeField] private float networkSmoothTime = 0.1f;

    private Vector3 startPosition;
    private Vector3 endPosition;
    private bool movingToEnd = true;
    private float waitTimer = 0f;

    // Network synchronization variables
    private Vector3 networkPosition;
    private Vector3 velocity;
    private float lerpSpeed = 15f;
    private PhotonView photonView;

    // Player movement handling
    private readonly float playerDetectionRange = 1.1f;
    private readonly float upwardCheckThreshold = 0.7f;

    void Awake()
    {
        photonView = GetComponent<PhotonView>();
        if (photonView == null)
        {
            Debug.LogError("PhotonView missing on MovingPlatform!");
            photonView = gameObject.AddComponent<PhotonView>();
        }

        if (!photonView.ObservedComponents.Contains(this))
        {
            photonView.ObservedComponents.Add(this);
        }

        photonView.Synchronization = ViewSynchronization.UnreliableOnChange;
        gameObject.tag = "MovingPlatform";
    }

    void Start()
    {
        startPosition = transform.position;
        networkPosition = startPosition;

        // Calculate end position based on movement settings
        endPosition = startPosition;
        if (moveHorizontally)
        {
            endPosition += Vector3.right * moveDistance;
        }
        if (moveVertically)
        {
            endPosition += Vector3.up * moveDistance;
        }

        Debug.Log($"Platform initialized - Start: {startPosition}, End: {endPosition}");
    }

    void Update()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            UpdateMasterClient();
        }
        else
        {
            UpdateClient();
        }

        // Handle players on platform
        HandlePlayersOnPlatform();
    }

    private void UpdateMasterClient()
    {
        if (waitTimer > 0)
        {
            waitTimer -= Time.deltaTime;
            return;
        }

        Vector3 targetPosition = movingToEnd ? endPosition : startPosition;
        
        // Calculate smooth movement
        float step = moveSpeed * Time.deltaTime;
        Vector3 newPosition = Vector3.MoveTowards(transform.position, targetPosition, step);
        
        // Apply movement with route clamping
        Vector3 routeDirection = (endPosition - startPosition).normalized;
        float distanceAlongRoute = Vector3.Dot(newPosition - startPosition, routeDirection);
        float totalRouteLength = Vector3.Distance(startPosition, endPosition);
        distanceAlongRoute = Mathf.Clamp(distanceAlongRoute, 0f, totalRouteLength);
        
        transform.position = startPosition + (routeDirection * distanceAlongRoute);
        networkPosition = transform.position;

        if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
        {
            movingToEnd = !movingToEnd;
            waitTimer = waitTimeAtEnds;
        }
    }

    private void UpdateClient()
    {
        transform.position = Vector3.SmoothDamp(
            transform.position,
            networkPosition,
            ref velocity,
            networkSmoothTime,
            Mathf.Infinity,
            Time.deltaTime
        );
    }

    private void HandlePlayersOnPlatform()
    {
        // Find all players in range
        Collider[] hitColliders = Physics.OverlapBox(
            transform.position + Vector3.up * (playerDetectionRange / 2),
            new Vector3(transform.localScale.x / 2, playerDetectionRange / 2, transform.localScale.z / 2)
        );

        foreach (var hitCollider in hitColliders)
        {
            if (hitCollider.CompareTag("Player"))
            {
                CharacterController playerController = hitCollider.GetComponent<CharacterController>();
                if (playerController != null)
                {
                    // Check if player is above platform
                    Vector3 playerBottom = hitCollider.transform.position - Vector3.up * (playerController.height / 2);
                    RaycastHit hit;
                    if (Physics.Raycast(playerBottom + Vector3.up * 0.1f, Vector3.down, out hit, 0.3f))
                    {
                        if (hit.collider.gameObject == gameObject)
                        {
                            // Move player with platform
                            Vector3 movement = transform.position - networkPosition;
                            playerController.Move(movement);
                        }
                    }
                }
            }
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(movingToEnd);
            stream.SendNext(waitTimer);
        }
        else
        {
            networkPosition = (Vector3)stream.ReceiveNext();
            movingToEnd = (bool)stream.ReceiveNext();
            waitTimer = (float)stream.ReceiveNext();

            float lag = Mathf.Abs((float)(PhotonNetwork.Time - info.SentServerTime));
            lerpSpeed = Mathf.Lerp(1f, 15f, lag);
        }
    }

    private void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(startPosition, endPosition);
            Gizmos.DrawWireSphere(startPosition, 0.3f);
            Gizmos.DrawWireSphere(endPosition, 0.3f);

            // Draw player detection box
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(
                transform.position + Vector3.up * (playerDetectionRange / 2),
                new Vector3(transform.localScale.x, playerDetectionRange, transform.localScale.z)
            );
        }
    }
} 