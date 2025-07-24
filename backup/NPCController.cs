using UnityEngine;
using UnityEngine.AI;
using Photon.Realtime;
using Photon.Pun;
using System.Collections;

public class NPCController : MonoBehaviourPunCallbacks, IPunObservable
{
    [SerializeField]
    private Animator animator;
    private NavMeshAgent agent;
    [SerializeField]
    new private PhotonView photonView;
    
    // Health and damage settings
    [SerializeField]
    private int maxHealth = 100;
    private int currentHealth;
    public int damageAmount = 20;
    
    // Movement and targeting settings
    public float detectionRange = 50f;
    public float attackRange = 20f;
    public float minAttackRange = 5f;
    public float patrolRadius = 20f;
    private Vector3 startPosition;
    private bool isDead = false;
    
    // Combat timing
    public float attackCooldown = 1f;
    private float lastAttackTime = 0f;

    // NPC Identity
    private string npcName;
    private NetworkManager networkManager;
    private NPCNameTag nameTag;
    
    private static readonly string[] FirstNames = {
        "Alpha", "Bravo", "Charlie", "Delta", "Echo",
        "Foxtrot", "Ghost", "Hunter", "Ice", "Jager"
    };

    private static readonly string[] LastNames = {
        "Bot", "Unit", "Drone", "Agent", "Trooper"
    };

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        networkManager = FindObjectOfType<NetworkManager>();
        
        if (photonView != null)
        {
            photonView.ObservedComponents.Add(this);
            photonView.OwnershipTransfer = OwnershipOption.Takeover;
        }

        startPosition = transform.position;
        currentHealth = maxHealth;
        isDead = false;
        gameObject.tag = "NPC";
        
        CreateNameTag();
        
        if (PhotonNetwork.IsMasterClient)
        {
            GenerateNPCName();
        }

        if (agent != null)
        {
            agent.stoppingDistance = minAttackRange;
            agent.speed = 5f;
            agent.acceleration = 12f;
            agent.angularSpeed = 180f;
        }

        DisablePlayerComponents();
    }

    private void CreateNameTag()
    {
        // Create name tag object
        GameObject nameTagObject = new GameObject("NameTag");
        nameTagObject.transform.SetParent(transform);
        nameTagObject.transform.localPosition = new Vector3(0, 2f, 0);
        
        // Add and setup the name tag component
        nameTag = nameTagObject.AddComponent<NPCNameTag>();
    }

    private void GenerateNPCName()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            string firstName = FirstNames[Random.Range(0, FirstNames.Length)];
            string lastName = LastNames[Random.Range(0, LastNames.Length)];
            int number = Random.Range(1, 1000);
            string generatedName = $"{firstName} {lastName}-{number} [NPC]";
            photonView.RPC("SyncNPCName", RpcTarget.All, generatedName);
        }
    }

    [PunRPC]
    private void SyncNPCName(string name)
    {
        npcName = name;
        NPCNameTag nameTagComponent = GetComponentInChildren<NPCNameTag>();
        if (nameTagComponent != null)
        {
            nameTagComponent.SetName(name);  // Fixed: Changed InitializeName to SetName
        }
    }

    private void AttackPlayer(Transform player)
    {
        if (player == null || isDead) return;

        lastAttackTime = Time.time;
        
        FaceTarget(player.position);
        
        // Call the RPC with proper attribute
        photonView.RPC("PlayAttackAnimation", RpcTarget.All);

        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        if (playerHealth != null && playerHealth.photonView != null)
        {
            playerHealth.photonView.RPC("TakeDamage", RpcTarget.All, damageAmount, npcName);
            
            if (networkManager != null)
            {
                networkManager.AddMessage($"{npcName} attacked {player.name} for {damageAmount} damage!");
            }
        }
    }

    [PunRPC]
    private void PlayAttackAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }
    }

    [PunRPC]
    public void TakeNPCDamage(int damage, string attackerName)
    {
        Debug.Log($"[NPC] TakeNPCDamage called by {attackerName}. Current Health: {currentHealth}, Damage: {damage}");
        
        if (isDead) return;

        // Process damage
        currentHealth = Mathf.Max(0, currentHealth - damage);
        
        // Show damage message
        if (networkManager != null)
        {
            networkManager.AddMessage($"{attackerName} hit {npcName} for {damage} damage! (Health: {currentHealth})");
        }

        // Play hit animation
        if (animator != null)
        {
            animator.SetTrigger("Hit");
        }

        // Check for death
        if (currentHealth <= 0 && !isDead)
        {
            HandleDeath(attackerName);
        }
    }

    private void HandleDeath(string killerName)
    {
        if (isDead) return;
        
        isDead = true;
        
        // Disable NPC functionality
        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        // Disable colliders
        foreach (Collider col in GetComponents<Collider>())
        {
            col.enabled = false;
        }

        // Play death animation
        if (animator != null)
        {
            animator.SetTrigger("IsDead");
        }

        // Award score and show message
        if (networkManager != null)
        {
            networkManager.AddNPCKillScore(killerName, npcName);
            networkManager.AddMessage($"{npcName} was eliminated by {killerName}!");
        }

        // Start destruction sequence
        if (PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(DestroyAfterDelay());
        }
    }

    private IEnumerator DestroyAfterDelay()
    {
        yield return new WaitForSeconds(2f);
        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Destroy(gameObject);
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(currentHealth);
            stream.SendNext(isDead);
            stream.SendNext(npcName);
        }
        else
        {
            currentHealth = (int)stream.ReceiveNext();
            isDead = (bool)stream.ReceiveNext();
            string receivedName = (string)stream.ReceiveNext();
            
            if (npcName != receivedName)
            {
                npcName = receivedName;
                if (nameTag != null)
                {
                    nameTag.SetName(npcName);
                }
            }
        }
    }

    private void DisablePlayerComponents()
    {
        var playerComponents = GetComponents<MonoBehaviour>();
        foreach (var comp in playerComponents)
        {
            if (comp.GetType().Name != "PhotonView" && 
                comp.GetType().Name != "NPCController")
            {
                comp.enabled = false;
            }
        }

        var cameras = GetComponentsInChildren<Camera>(true);
        foreach (var cam in cameras)
        {
            cam.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (isDead) return;
        UpdateBehavior();
    }

    private void UpdateBehavior()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        Transform target = FindNearestPlayer();
        
        if (target != null)
        {
            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            
            if (distanceToTarget <= attackRange && distanceToTarget > minAttackRange)
            {
                // Stop and attack
                agent.isStopped = true;
                FaceTarget(target.position);
                
                if (Time.time >= lastAttackTime + attackCooldown)
                {
                    AttackPlayer(target);
                }
            }
            else if (distanceToTarget <= detectionRange)
            {
                // Chase player
                agent.isStopped = false;
                agent.SetDestination(target.position);
                animator.SetBool("Running", true);
                
                // If too close, back away
                if (distanceToTarget < minAttackRange)
                {
                    Vector3 directionAway = transform.position - target.position;
                    Vector3 newPos = transform.position + directionAway.normalized * (minAttackRange + 1f);
                    agent.SetDestination(newPos);
                }
            }
            else
            {
                // Return to patrol
                agent.isStopped = false;
                animator.SetBool("Running", false);
                Patrol();
            }
        }
        else
        {
            // No target, just patrol
            agent.isStopped = false;
            animator.SetBool("Running", false);
            Patrol();
        }
    }

    private Transform FindNearestPlayer()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        Transform nearestPlayer = null;
        float nearestDistance = float.MaxValue;
        
        foreach (GameObject player in players)
        {
            if (player == null || !player.activeInHierarchy) continue;

            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance < nearestDistance && HasLineOfSightTo(player.transform))
            {
                nearestDistance = distance;
                nearestPlayer = player.transform;
            }
        }
        
        return nearestPlayer;
    }

    private bool HasLineOfSightTo(Transform target)
    {
        if (target == null) return false;

        Vector3 directionToTarget = (target.position - transform.position).normalized;
        RaycastHit hit;
        
        Vector3 rayStart = transform.position + Vector3.up * 1.5f;
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        if (Physics.Raycast(rayStart, directionToTarget, out hit, distanceToTarget))
        {
            return hit.transform.CompareTag("Player");
        }

        return false;
    }

    private void Patrol()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        if (agent.remainingDistance <= agent.stoppingDistance)
        {
            SetNewPatrolTarget();
        }
    }

    private void SetNewPatrolTarget()
    {
        Vector3 randomDirection = Random.insideUnitSphere * patrolRadius;
        randomDirection += startPosition;
        NavMeshHit hit;
        
        if (NavMesh.SamplePosition(randomDirection, out hit, patrolRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        direction.y = 0;
        
        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
        }
    }
}
   