using Photon.Pun;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;
using UnityStandardAssets.Characters.FirstPerson;
using System.Collections;

[RequireComponent(typeof(FirstPersonController))]

public class PlayerNetworkMover : MonoBehaviourPunCallbacks, IPunObservable {

    [SerializeField]
    private Animator animator;
    [SerializeField]
    private GameObject cameraObject;
    [SerializeField]
    private GameObject gunObject;
    [SerializeField]
    private GameObject playerObject;
    [SerializeField]
    private NameTag nameTag;

    // Add new spawn points array
    [SerializeField]
    private Transform[] spawnPoints;  // Make sure this is assigned in Unity Inspector

    private Vector3 position;
    private Quaternion rotation;
    private bool jump;
    private float smoothing = 10.0f;
    private Vector3 spawnPosition;
    private Transform platformParent;
    private Vector3 lastPlatformPosition;
    private bool isOnPlatform = false;
    private CharacterController characterController;

    /// <summary>
    /// Move game objects to another layer.
    /// </summary>
    void MoveToLayer(GameObject gameObject, int layer) {
        gameObject.layer = layer;
        foreach(Transform child in gameObject.transform) {
            MoveToLayer(child.gameObject, layer);
        }
    }

    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// </summary>
    void Awake() {
        // FirstPersonController script require cameraObject to be active in its Start function.
        if (photonView.IsMine) {
            cameraObject.SetActive(true);
            characterController = GetComponent<CharacterController>();
        }
    }

    /// <summary>
    /// Start is called on the frame when a script is enabled just before
    /// any of the Update methods is called the first time.
    /// </summary>
    void Start() {
        // Add debug logging to check spawn points
        if (spawnPoints == null || spawnPoints.Length == 0) {
            Debug.LogWarning("No spawn points assigned! Please assign spawn points in the Unity Inspector.");
            spawnPosition = transform.position;
        } else {
            Debug.Log($"Found {spawnPoints.Length} spawn points");
            spawnPosition = spawnPoints[Random.Range(0, spawnPoints.Length)].position;
            transform.position = spawnPosition;
        }
        
        if (photonView.IsMine) {
            GetComponent<FirstPersonController>().enabled = true;
            MoveToLayer(gunObject, LayerMask.NameToLayer("Hidden"));
            MoveToLayer(playerObject, LayerMask.NameToLayer("Hidden"));
            // Set other player's nametag target to this player's nametag transform.
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            foreach (GameObject player in players) {
                player.GetComponentInChildren<NameTag>().target = nameTag.transform;
            }
        } else {
            position = transform.position;
            rotation = transform.rotation;
            // Set this player's nametag target to other players's target.
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            foreach (GameObject player in players) {
                if (player != gameObject) {
                    nameTag.target = player.GetComponentInChildren<NameTag>().target;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Update is called every frame, if the MonoBehaviour is enabled.
    /// </summary>
    void Update() {
        if (!photonView.IsMine) {
            transform.position = Vector3.Lerp(transform.position, position, Time.deltaTime * smoothing);
            transform.rotation = Quaternion.Lerp(transform.rotation, rotation, Time.deltaTime * smoothing);
        } else {
            // Check if player has fallen below -150
            if (transform.position.y < -150f) {
                RespawnPlayer();
            }

            // Handle platform movement
            if (isOnPlatform && platformParent != null) {
                Vector3 platformDelta = platformParent.position - lastPlatformPosition;
                if (platformDelta.magnitude > 0) {
                    characterController.Move(platformDelta);
                }
                lastPlatformPosition = platformParent.position;
            }
        }
    }

    /// <summary>
    /// This function is called every fixed framerate frame, if the MonoBehaviour is enabled.
    /// </summary>
    void FixedUpdate() {
        if (photonView.IsMine) {
            // Check if we're still on the platform
            if (isOnPlatform) {
                RaycastHit hit;
                if (!Physics.Raycast(transform.position, Vector3.down, out hit, 1.1f) || 
                    !hit.collider.CompareTag("MovingPlatform")) {
                    Debug.Log("No longer on platform!");
                    isOnPlatform = false;
                    platformParent = null;
                }
            }

            animator.SetFloat("Horizontal", CrossPlatformInputManager.GetAxis("Horizontal"));
            animator.SetFloat("Vertical", CrossPlatformInputManager.GetAxis("Vertical"));
            if (CrossPlatformInputManager.GetButtonDown("Jump")) {
                animator.SetTrigger("IsJumping");
                isOnPlatform = false;
                platformParent = null;
            }
            animator.SetBool("Running", Input.GetKey(KeyCode.LeftShift));
        }
    }

    /// <summary>
    /// Used to customize synchronization of variables in a script watched by a photon network view.
    /// </summary>
    /// <param name="stream">The network bit stream.</param>
    /// <param name="info">The network message information.</param>
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info) {
        if (stream.IsWriting) {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
        } else {
            position = (Vector3)stream.ReceiveNext();
            rotation = (Quaternion)stream.ReceiveNext();
        }
    }

    void OnControllerColliderHit(ControllerColliderHit hit) {
        float start_value = 0.0f;
        if (!photonView.IsMine) return;

        if (hit.gameObject.CompareTag("MovingPlatform")) {
            start_value = transform.position.y;
            // Check if we're standing on the platform (using dot product with up vector)
            if (Vector3.Dot(hit.normal, Vector3.up) > 0.5f) {
                Debug.Log("Standing on platform!");
                platformParent = hit.transform;
                lastPlatformPosition = platformParent.position;
                lastPlatformPosition.y = start_value;
                isOnPlatform = true;
            }
        }
    }

    private void RespawnPlayer() {
        if (photonView.IsMine) {
            isOnPlatform = false;
            platformParent = null;
            characterController.enabled = false;
            
            if (spawnPoints == null || spawnPoints.Length == 0) {
                Debug.LogWarning("No spawn points available for respawn!");
                transform.position = spawnPosition;
            } else {
                // Get a random spawn point that's different from current position
                int attempts = 0;
                Vector3 newSpawnPos;
                do {
                    int randomIndex = Random.Range(0, spawnPoints.Length);
                    newSpawnPos = spawnPoints[randomIndex].position;
                    attempts++;
                } while (Vector3.Distance(newSpawnPos, transform.position) < 1f && attempts < 10);
                
                Debug.Log($"Respawning player at new position: {newSpawnPos}");
                transform.position = newSpawnPos;
            }
            
            characterController.enabled = true;
        }
    }

}
