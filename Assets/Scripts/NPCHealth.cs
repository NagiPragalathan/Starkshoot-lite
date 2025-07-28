using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.AI;
using System;

public class NPCHealth : MonoBehaviourPunCallbacks, IPunObservable
{
    public delegate void Respawn(float time);
    public delegate void AddMessage(string Message);
    public event Respawn RespawnEvent;
    public event AddMessage AddMessageEvent;

    // Events
    public event Action OnDamageReceived;
    public event Action OnDeath;

    [SerializeField]
    private int startingHealth = 100;
    [SerializeField]
    private float sinkSpeed = 0.12f;
    [SerializeField]
    private float sinkTime = 2.5f;
    [SerializeField]
    private float respawnTime = 8.0f;
    [SerializeField]
    private AudioClip deathClip;
    [SerializeField]
    private AudioClip hurtClip;
    [SerializeField]
    private AudioSource audioSource;
    [SerializeField]
    private Animator animator;
    [SerializeField]
    private TextMesh nameTagText; // Reference to the 3D text mesh for the name tag

    [Header("Name Tag Settings")]
    [SerializeField] private float nameTagHeight = 1.8f; // Height above the model
    [SerializeField] private float nameTagScale = 0.1f;  // Overall scale of the name tag
    [SerializeField] private int nameTagFontSize = 20;   // Font size for the name tag
    [SerializeField] private Color nameTagColor = new Color(1f, 0f, 0f, 0.8f); // Slightly transparent red

    private int currentHealth;
    private bool isDead;
    private bool isSinking;
    private NetworkManager networkManager;
    private PhotonView photonView;
    private NPCController npcController;
    private NavMeshAgent agent; // Add NavMeshAgent field
    private NPCTpsGun npcGun; // Add NPCTpsGun field

    // Add these new variables for position/rotation sync
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private float lastNetworkPositionUpdate;
    private const float NETWORK_SMOOTHING = 10f;

    // Add after the existing network variables
    private float lastSyncTime;
    private const float SYNC_INTERVAL = 0.05f; // 20 updates per second
    private Vector3 lastSyncPosition;
    private Quaternion lastSyncRotation;

    // Add these new variables
    private static readonly string[] BotFirstNames = {
        "Bot_Alpha", "Bot_Beta", "Bot_Delta", "Bot_Echo", 
        "Bot_Foxtrot", "Bot_Ghost", "Bot_Hunter", "Bot_Iron",
        "Bot_Juliet", "Bot_Kilo", "Bot_Lima", "Bot_Mike"
    };

    // Change botName to be publicly accessible with private set
    public string BotName { get; private set; }

    // Add a flag to track if bot's entry has been reported
    private bool wasReported = false;

    // Add this field
    private bool hasJoinedMessage = false;

    void Awake()
    {
        photonView = GetComponent<PhotonView>();
        if (photonView == null)
        {
            Debug.LogError("PhotonView missing on NPCHealth!");
            photonView = gameObject.AddComponent<PhotonView>();
        }
        
        // Make sure this component is observed by PhotonView
        if (!photonView.ObservedComponents.Contains(this))
        {
            photonView.ObservedComponents.Add(this);
        }
        
        // Set up audio source if needed
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Initialize network position/rotation
        networkPosition = transform.position;
        networkRotation = transform.rotation;
        lastNetworkPositionUpdate = Time.time;

        // Generate random bot name if we're the master client
        if (PhotonNetwork.IsMasterClient)
        {
            GenerateRandomBotName();
            photonView.RPC("SetBotName", RpcTarget.AllBuffered, BotName);
        }

        // Reset reported status
        wasReported = false;
    }

    void Start()
    {
        animator = GetComponent<Animator>();
        networkManager = FindObjectOfType<NetworkManager>();
        npcController = GetComponent<NPCController>();
        agent = GetComponent<NavMeshAgent>(); // Initialize NavMeshAgent
        npcGun = GetComponentInChildren<NPCTpsGun>(); // Initialize NPCTpsGun
        
        currentHealth = startingHealth;
        isDead = false;
        isSinking = false;

        Debug.Log($"NPC Health initialized with {currentHealth} HP. PhotonView ID: {photonView.ViewID}");

        // Adjust the model scale if it's too small
        transform.localScale = new Vector3(1.5f, 1.5f, 1.5f); // Adjust these values as needed
    }

    void Update()
    {
        if (isSinking)
        {
            transform.Translate(Vector3.down * sinkSpeed * Time.deltaTime);
            return;
        }

        // Only interpolate position if we're not the Master Client and not dead
        if (!PhotonNetwork.IsMasterClient && !isDead)
        {
            float timeSinceLastSync = (Time.time - lastSyncTime) / SYNC_INTERVAL;
            transform.position = Vector3.Lerp(lastSyncPosition, networkPosition, timeSinceLastSync);
            transform.rotation = Quaternion.Lerp(lastSyncRotation, networkRotation, timeSinceLastSync);
        }

        UpdateNameTagVisibility();
    }

    [PunRPC]
    public void TakeDamage(int amount, string attackerName)
    {
        if (isDead) return;

        // Process damage
        currentHealth -= amount;
        Debug.Log($"NPC {BotName} took {amount} damage from {attackerName}. Health: {currentHealth}");

        // Synchronize the damage animation and effects across all clients
        photonView.RPC("SyncDamageEffects", RpcTarget.All);

        // Check for death
        if (currentHealth <= 0 && !isDead)
        {
            photonView.RPC("ProcessDeath", RpcTarget.All, attackerName);
        }

        // Force immediate position sync after damage
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("SyncPosition", RpcTarget.All, transform.position, transform.rotation.eulerAngles);
        }
    }

    [PunRPC]
    private void SyncPosition(Vector3 position, Vector3 rotation)
    {
        if (!isDead)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(rotation);
            
            // Update network positions for interpolation
            networkPosition = position;
            networkRotation = transform.rotation;
            lastSyncPosition = position;
            lastSyncRotation = transform.rotation;
            lastSyncTime = Time.time;
        }
    }

    [PunRPC]
    private void SyncDamageEffects()
    {
        // Invoke damage event
        OnDamageReceived?.Invoke();

        // Get NPC controller to handle animation
        NPCController controller = GetComponent<NPCController>();
        if (controller != null)
        {
            controller.HandleDamage();
        }
        else
        {
            // Fallback if controller not found
            if (animator != null)
            {
                animator.SetTrigger("IsHurt");
            }
        }

        // Play hurt sound
        if (audioSource != null && hurtClip != null)
        {
            audioSource.clip = hurtClip;
            audioSource.Play();
        }
    }

    [PunRPC]
    private void ProcessDeath(string killerName)
    {
        if (isDead) return;
        isDead = true;

        Debug.Log($"NPC {BotName} died, killed by: {killerName}");

        // Store death position for reference
        Vector3 deathPosition = transform.position;

        // Disable components
        if (npcController != null)
        {
            npcController.HandleDeath();
            npcController.enabled = false;
        }
        
        if (agent != null) 
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        // Disable any NPCTpsGun components
        if (npcGun != null)
        {
            npcGun.enabled = false;
        }

        // Play death animation
        if (animator != null)
        {
            animator.SetBool("IsDead", true);
            animator.SetTrigger("Die");
        }

        // Play death sound
        if (audioSource != null && deathClip != null)
        {
            audioSource.clip = deathClip;
            audioSource.Play();
        }

        // Handle network messages
        if (networkManager != null)
        {
            networkManager.photonView.RPC("AddMessage_RPC", RpcTarget.All, 
                $"{BotName} was eliminated by {killerName}!");
            
            networkManager.photonView.RPC("AddBotKill_RPC", RpcTarget.All, 
                killerName, BotName);
        }

        // Start death sequence
        StartCoroutine(DeathSequence(deathPosition));
    }

    private IEnumerator DeathSequence(Vector3 deathPosition)
    {
        // Wait for 2 seconds
        yield return new WaitForSeconds(2f);

        // Start sinking
        isSinking = true;
        
        // Disable physics
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Notify master client to handle respawn
        if (networkManager != null)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                networkManager.RequestNPCRespawn(deathPosition);
            }
            else
            {
                networkManager.photonView.RPC("RequestBotRespawnRPC", 
                    RpcTarget.MasterClient, deathPosition);
            }
        }

        // Wait for sink animation
        yield return new WaitForSeconds(sinkTime);

        // Destroy the NPC if we're the master client
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.Destroy(gameObject);
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Send position, rotation, health data, and isDead state
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(currentHealth);
            stream.SendNext(isDead);
            stream.SendNext(BotName);
            stream.SendNext(isSinking);
        }
        else
        {
            // Store last sync values for interpolation
            lastSyncPosition = transform.position;
            lastSyncRotation = transform.rotation;
            
            // Receive new values
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            currentHealth = (int)stream.ReceiveNext();
            isDead = (bool)stream.ReceiveNext();
            BotName = (string)stream.ReceiveNext();
            isSinking = (bool)stream.ReceiveNext();
            
            // Update sync time
            lastSyncTime = Time.time;
        }
    }

    public int GetCurrentHealth()
    {
        return currentHealth;
    }

    // Helper method to check if NPC is dead (useful for other systems)
    public bool IsDead()
    {
        return isDead;
    }

    private void GenerateRandomBotName()
    {
        string prefix = "Bot-";
        string name = BotFirstNames[UnityEngine.Random.Range(0, BotFirstNames.Length)];
        string number = UnityEngine.Random.Range(1, 100).ToString("00");
        BotName = $"{prefix}{name}{number}";
    }

    [PunRPC]
    private void SetBotName(string name)
    {
        BotName = name;
        if (nameTagText != null)
        {
            nameTagText.text = BotName;
        }
        else
        {
            CreateNameTag();
        }
        
        // Show join message only once
        if (!hasJoinedMessage && networkManager != null)
        {
            if (networkManager.photonView != null)
            {
                networkManager.photonView.RPC("AddMessage_RPC", RpcTarget.All, $"{BotName} has entered the arena!");
            }
            hasJoinedMessage = true;
        }
    }

    private void CreateNameTag()
    {
        // Create a new GameObject for the name tag
        GameObject nameTagObj = new GameObject("BotNameTag");
        nameTagObj.transform.SetParent(transform);
        nameTagObj.transform.localPosition = new Vector3(0, nameTagHeight * 1.5f, 0); // Adjust height for larger model
        nameTagObj.transform.localScale = new Vector3(nameTagScale, nameTagScale, nameTagScale);
        
        // Add TextMesh component
        nameTagText = nameTagObj.AddComponent<TextMesh>();
        nameTagText.text = BotName;
        nameTagText.fontSize = nameTagFontSize;
        nameTagText.alignment = TextAlignment.Center;
        nameTagText.anchor = TextAnchor.MiddleCenter;
        nameTagText.color = nameTagColor;
        nameTagText.characterSize = 1;
        nameTagText.richText = true;
        
        // Add Billboard script to make text face camera
        nameTagObj.AddComponent<Billboard>();
    }

    private void UpdateNameTagVisibility()
    {
        if (nameTagText == null) return;

        // Get distance to main camera
        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        float distanceToCamera = Vector3.Distance(transform.position, mainCamera.transform.position);
        
        // Scale text based on distance (within reasonable limits)
        float scaleFactor = Mathf.Clamp(distanceToCamera * 0.05f, 0.5f, 2f);
        nameTagText.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor) * nameTagScale;
        
        // Fade out name tag if too far or too close
        Color currentColor = nameTagText.color;
        float alpha = Mathf.Clamp01(1f - (distanceToCamera - 5f) / 20f); // Fade between 5 and 25 units
        nameTagText.color = new Color(currentColor.r, currentColor.g, currentColor.b, alpha);
    }

    void LateUpdate()
    {
        UpdateNameTagVisibility();
    }

    public void ResetHealth()
    {
        currentHealth = startingHealth;
        isDead = false;
        isSinking = false;
        
        // Re-enable components
        var colliders = GetComponents<Collider>();
        foreach (var col in colliders)
        {
            col.enabled = true;
        }
        
        // Reset animator
        if (animator != null)
        {
            animator.SetBool("IsDead", false);
            animator.SetBool("IsHurt", false);
        }
        
        // Re-enable NavMeshAgent
        if (agent != null)
        {
            agent.enabled = true;
            agent.isStopped = false;
        }
        
        // Re-enable NPCController
        NPCController controller = GetComponent<NPCController>();
        if (controller != null)
        {
            controller.enabled = true;
            controller.InitializeNPC();
        }
    }

    public void InitializeNPC()
    {
        currentHealth = startingHealth;
        isDead = false;
        isSinking = false;

        // Enable all necessary components
        if (agent != null) 
        {
            agent.enabled = true;
        }

        var controller = GetComponent<NPCController>();
        if (controller != null) controller.enabled = true;

        if (animator != null)
        {
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
        }

        Debug.Log($"NPC {photonView.ViewID} initialized with {currentHealth} HP");
    }
}