using UnityEngine;
using Photon.Pun;

public class CloudMovement : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Position References")]
    public GameObject startPointObject;
    public GameObject endPointObject;

    [Header("Settings")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float waitTimeAtEnds = 2f;

    private Vector3 startPosition;
    private Vector3 endPosition;
    private Vector3 networkPosition;
    private bool movingToEnd = true;
    private float waitTimer = 0f;
    private bool isInitialized = false;
    new public PhotonView photonView;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
    }

    private void Start()
    {
        if (startPointObject == null || endPointObject == null)
        {
            Debug.LogError($"[CloudMovement] Points not set for cloud {gameObject.name}!");
            enabled = false;
            return;
        }

        startPosition = startPointObject.transform.position;
        endPosition = endPointObject.transform.position;
        transform.position = startPosition;
        networkPosition = startPosition;
        isInitialized = true;

        Debug.Log($"[CloudMovement] Cloud initialized at {startPosition}");
    }

    private void Update()
    {
        if (!isInitialized) return;

        // ALL clients update movement
        UpdateMovement();
    }

    private void UpdateMovement()
    {
        if (waitTimer > 0)
        {
            waitTimer -= Time.deltaTime;
            return;
        }

        Vector3 targetPosition = movingToEnd ? endPosition : startPosition;
        float step = moveSpeed * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, step);

        // Only master client changes direction
        if (PhotonNetwork.IsMasterClient)
        {
            if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
            {
                movingToEnd = !movingToEnd;
                waitTimer = waitTimeAtEnds;
                // Sync the direction change to all clients
                photonView.RPC("SyncDirectionChange", RpcTarget.All, movingToEnd, waitTimer);
            }
        }
    }

    [PunRPC]
    private void SyncDirectionChange(bool newMovingToEnd, float newWaitTimer)
    {
        movingToEnd = newMovingToEnd;
        waitTimer = newWaitTimer;
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

            // Smooth position update
            transform.position = Vector3.Lerp(transform.position, networkPosition, 0.5f);
        }
    }

    private void OnDrawGizmos()
    {
        if (startPointObject != null && endPointObject != null)
        {
            Gizmos.color = Color.cyan;
            Vector3 start = startPointObject.transform.position;
            Vector3 end = endPointObject.transform.position;
            Gizmos.DrawLine(start, end);
            
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(start, 0.5f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(end, 0.5f);
        }
    }
} 