using UnityEngine;
using Photon.Pun;

[RequireComponent(typeof(PhotonView))]
public class TrainPlatform : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Position References")]
    [SerializeField] private GameObject startPointObject;  // Drag start point object here
    [SerializeField] private GameObject endPointObject;    // Drag end point object here

    [Header("Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float waitTimeAtEnds = 2f;
    [SerializeField] private float networkSmoothTime = 0.1f; // Smoothing time for network interpolation

    private Vector3 startPosition;
    private Vector3 endPosition;
    private bool movingToEnd = true;
    private float waitTimer = 0f;

    // Network synchronization variables
    private Vector3 networkPosition;
    private Vector3 velocity;
    private float lerpSpeed = 15f;
    private PhotonView photonView;

    void Awake()
    {
        photonView = GetComponent<PhotonView>();
        if (photonView == null)
        {
            Debug.LogError("PhotonView missing on TrainPlatform!");
            photonView = gameObject.AddComponent<PhotonView>();
        }
        
        // Make sure this component is observed by PhotonView
        if (!photonView.ObservedComponents.Contains(this))
        {
            photonView.ObservedComponents.Add(this);
        }
        
        // Configure PhotonView settings
        photonView.Synchronization = ViewSynchronization.UnreliableOnChange;
    }

    private void Start()
    {
        if (startPointObject == null || endPointObject == null)
        {
            Debug.LogError("Start or End point objects not set! Please assign them in the inspector.");
            enabled = false;
            return;
        }

        // Get positions from the GameObjects
        startPosition = startPointObject.transform.position;
        endPosition = endPointObject.transform.position;

        // Set train to start position
        transform.position = startPosition;
        networkPosition = startPosition;

        Debug.Log($"Train initialized - Start: {startPosition}, End: {endPosition}, IsMaster: {PhotonNetwork.IsMasterClient}");
    }

    private void Update()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            UpdateMasterClient();
        }
        else
        {
            UpdateClient();
        }
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
            Debug.Log($"Train reached {(movingToEnd ? "end" : "start")} position");
        }
    }

    private void UpdateClient()
    {
        // Smooth interpolation to network position
        transform.position = Vector3.SmoothDamp(
            transform.position, 
            networkPosition, 
            ref velocity, 
            networkSmoothTime,
            Mathf.Infinity,
            Time.deltaTime
        );
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Send position, movement state, and timer
            stream.SendNext(transform.position);
            stream.SendNext(movingToEnd);
            stream.SendNext(waitTimer);
        }
        else
        {
            // Receive and interpolate
            networkPosition = (Vector3)stream.ReceiveNext();
            movingToEnd = (bool)stream.ReceiveNext();
            waitTimer = (float)stream.ReceiveNext();

            // Calculate interpolation speed based on lag
            float lag = Mathf.Abs((float)(PhotonNetwork.Time - info.SentServerTime));
            lerpSpeed = Mathf.Lerp(1f, 15f, lag); // Adjust interpolation speed based on lag
        }
    }

    private void OnDrawGizmos()
    {
        // Only draw if both points are set
        if (startPointObject != null && endPointObject != null)
        {
            Gizmos.color = Color.yellow;
            Vector3 start = Application.isPlaying ? startPosition : startPointObject.transform.position;
            Vector3 end = Application.isPlaying ? endPosition : endPointObject.transform.position;
            
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(start, 0.5f);
            Gizmos.DrawWireSphere(end, 0.5f);
        }
    }
}
