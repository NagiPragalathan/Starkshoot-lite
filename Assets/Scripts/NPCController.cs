using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(PhotonView))]
public class NPCController : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;  // Increased base speed
    [SerializeField] private float patrolRadius = 20f;
    [SerializeField] private float minWaitTime = 1f;
    [SerializeField] private float maxWaitTime = 3f;
    [SerializeField] private float rotationSpeed = 15f; // Faster rotation

    [Header("Combat Settings")]
    [SerializeField] private float detectionRange = 30f;
    [SerializeField] private float attackRange = 10f;
    [SerializeField] private float attackCooldown = 2f;
    [SerializeField] private int damageAmount = 20;

    [Header("Collision Settings")]
    [SerializeField] private float wallCheckRadius = 1f;
    [SerializeField] private LayerMask shootableLayer;
    [SerializeField] private float minWallDistance = 0.75f;
    [SerializeField] private float pushForce = 2.5f;
    [SerializeField] private float smoothingSpeed = 5f;
    private readonly string[] wallTags = { "Metal", "Dirt", "Wood", "Glass", "Concrete", "Water", "Train" };
    private Vector3 lastSafePosition;
    private Vector3 targetPosition;
    private bool isRepositioning = false;

    [Header("Pathfinding Settings")]
    [SerializeField] private float pathRecalculationTime = 0.2f;
    [SerializeField] private float stuckCheckDistance = 0.1f;
    [SerializeField] private float stuckCheckTime = 1.5f;
    [SerializeField] private int maxPathRetries = 3;
    private Vector3 lastPosition;
    private float lastMovementTime;
    private int pathRetryCount;
    private bool isStuck = false;

    // Components
    private NavMeshAgent agent;
    private Animator animator;
    private PhotonView photonView;
    private NPCHealth npcHealth;
    private Vector3 startPosition;
    private bool isDead = false;
    private float nextAttackTime;
    private bool isMoving = false;
    private bool isInitialized = false;
    private int npcViewID;
    private Rigidbody rb;

    // Animation parameter hashes (faster than strings)
    private int hashHorizontal;
    private int hashVertical;
    private int hashRunning;
    private int hashIsDead;
    private int hashIsHurt;
    private int hashDieTrigger;
    private int hashShootTrigger;

    private NPCTpsGun npcGun;
    private Transform currentTarget;

    // Add these network sync variables at the top of the class after other private variables
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private Vector3 lastNetworkPosition;
    private Quaternion lastNetworkRotation;
    private float lastNetworkUpdateTime;
    private const float NETWORK_SYNC_INTERVAL = 0.05f; // 20 updates per second
    private const float MIN_MOVEMENT_THRESHOLD = 0.001f;

    // Add these variables after the network sync variables
    private float networkHorizontal;
    private float networkVertical;
    private bool networkIsRunning;
    private Vector3 networkVelocity;
    private float networkLerpSpeed = 15f;
    private float animationSmoothTime = 0.2f;
    private Vector3 currentVelocityRef;
    private float lastAnimationSyncTime;
    private const float ANIMATION_SYNC_INTERVAL = 0.1f; // Sync animations 10 times per second

    private void Awake()
    {
        // Get components
        agent = GetComponent<NavMeshAgent>();
        photonView = GetComponent<PhotonView>();
        npcHealth = GetComponent<NPCHealth>();
        npcViewID = photonView.ViewID;
        rb = GetComponent<Rigidbody>();
        
        // Ensure we have a Rigidbody and Collider for collision detection
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            Debug.Log($"NPC {npcViewID}: Added Rigidbody component");
        }
        
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // Ensure we have a collider
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.5f;
            capsule.center = new Vector3(0, 1f, 0);
            Debug.Log($"NPC {npcViewID}: Added CapsuleCollider component");
        }
        
        // Get or find animator
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError($"NPC {npcViewID} cannot find Animator component!");
            }
        }
        
        // Cache animation parameter hashes for better performance
        hashHorizontal = Animator.StringToHash("Horizontal");
        hashVertical = Animator.StringToHash("Vertical");
        hashRunning = Animator.StringToHash("Running");
        hashIsDead = Animator.StringToHash("IsDead");
        hashIsHurt = Animator.StringToHash("IsHurt");
        hashDieTrigger = Animator.StringToHash("Die");
        hashShootTrigger = Animator.StringToHash("Shoot");
        
        // Store starting position
        startPosition = transform.position;
        lastSafePosition = startPosition;

        // Set proper scale (1.5x is larger than player)
        transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        
        Debug.Log($"NPC {npcViewID} initialized with animator: {(animator != null ? "Found" : "Missing")}");
    }

    private void Start()
    {
        if (!isInitialized)
        {
            InitializeNPC();
        }
        npcGun = GetComponentInChildren<NPCTpsGun>();

        // Log initial setup
        Debug.Log($"NPC {npcViewID} initialized with components:" +
            $"\nRigidbody: {rb != null}" +
            $"\nCollider: {GetComponent<Collider>() != null}" +
            $"\nNavMeshAgent: {agent != null}" +
            $"\nPosition: {transform.position}");
    }

    public void InitializeNPC()
    {
        // Configure NavMeshAgent for proper movement
        if (agent != null)
        {
            agent.speed = moveSpeed;
            agent.stoppingDistance = 1f;
            agent.autoBraking = true;
            agent.acceleration = 16f;  // Faster acceleration
            agent.angularSpeed = 360f; // Faster turning
            agent.avoidancePriority = Random.Range(20, 80);
            
            // Ensure agent is on the NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 1.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }
        
        // Initialize animator parameters
        if (animator != null)
        {
            animator.SetFloat(hashHorizontal, 0f);
            animator.SetFloat(hashVertical, 0f);
            animator.SetBool(hashRunning, false);
            animator.SetBool(hashIsDead, false);
            
            // Force animation update
            animator.Rebind();
            animator.Update(0f);
            Debug.Log($"NPC {npcViewID} animator initialized");
        }
        
        // For master client, start AI routines
        if (PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(AIRoutine());
            StartCoroutine(FindPlayerRoutine());
        }
        
        isInitialized = true;
        Debug.Log($"NPC {npcViewID} fully initialized. Is Master: {PhotonNetwork.IsMasterClient}");
    }

    private void OnEnable()
    {
        Debug.Log($"NPC {npcViewID}: OnEnable called");
    }

    private void OnDisable()
    {
        Debug.Log($"NPC {npcViewID}: OnDisable called");
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"NPC {npcViewID} trigger enter with: {other.gameObject.name} (Tag: {other.gameObject.tag})");
        
        if (System.Array.Exists(wallTags, tag => other.CompareTag(tag)))
        {
            Debug.LogWarning($"NPC {npcViewID} triggered wall collision with: {other.gameObject.name}");
            HandleWallCollision(other.transform.position);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"NPC {npcViewID} collision enter with: {collision.gameObject.name} (Tag: {collision.gameObject.tag})");
        
        if (System.Array.Exists(wallTags, tag => collision.gameObject.CompareTag(tag)))
        {
            Vector3 collisionPoint = collision.contacts[0].point;
            Debug.LogWarning($"NPC {npcViewID} WALL COLLISION at point: {collisionPoint}");
            HandleWallCollision(collisionPoint);
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        Debug.Log($"NPC {npcViewID} collision stay with: {collision.gameObject.name}");
        
        if (System.Array.Exists(wallTags, tag => collision.gameObject.CompareTag(tag)))
        {
            Debug.LogError($"NPC {npcViewID} STUCK IN WALL: {collision.gameObject.name} at position {transform.position}");
            HandleWallCollision(collision.contacts[0].point);
        }
    }

    private void HandleWallCollision(Vector3 collisionPoint)
    {
        if (isRepositioning) return;
        
        // Calculate direction away from wall
        Vector3 awayFromWall = (transform.position - collisionPoint).normalized;
        Vector3 safePosition = transform.position + awayFromWall * minWallDistance;
        
        // Verify safe position
        NavMeshHit hit;
        if (NavMesh.SamplePosition(safePosition, out hit, minWallDistance, NavMesh.AllAreas))
        {
            targetPosition = hit.position;
            StartCoroutine(SmoothRepositioning());
        }
        else if (NavMesh.SamplePosition(lastSafePosition, out hit, minWallDistance, NavMesh.AllAreas))
        {
            targetPosition = hit.position;
            StartCoroutine(SmoothRepositioning());
        }
    }

    private IEnumerator SmoothRepositioning()
    {
        isRepositioning = true;
        Vector3 startPosition = transform.position;
        float journeyLength = Vector3.Distance(startPosition, targetPosition);
        float startTime = Time.time;
        
        // Temporarily pause the NavMeshAgent
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        while (Vector3.Distance(transform.position, targetPosition) > 0.01f)
        {
            float distanceCovered = (Time.time - startTime) * smoothingSpeed;
            float fractionOfJourney = distanceCovered / journeyLength;
            
            transform.position = Vector3.Lerp(startPosition, targetPosition, fractionOfJourney);
            
            if (agent != null && agent.isOnNavMesh)
            {
                agent.Warp(transform.position);
            }
            
            yield return null;
        }

        // Resume NavMeshAgent
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.ResetPath();
        }
        
        isRepositioning = false;
        lastSafePosition = transform.position;
    }

    private void CheckAndCorrectWallCollision()
    {
        if (isRepositioning) return;

        Collider[] nearbyWalls = Physics.OverlapSphere(transform.position, wallCheckRadius, shootableLayer);
        bool isNearWall = false;
        Vector3 pushDirection = Vector3.zero;

        foreach (Collider wall in nearbyWalls)
        {
            if (System.Array.Exists(wallTags, tag => wall.CompareTag(tag)))
            {
                isNearWall = true;
                Vector3 closestPoint = wall.ClosestPoint(transform.position);
                Vector3 awayFromWall = (transform.position - closestPoint).normalized;
                pushDirection += awayFromWall;
            }
        }

        if (isNearWall)
        {
            pushDirection.Normalize();
            Vector3 targetPos = transform.position + pushDirection * minWallDistance;
            
            NavMeshHit hit;
            if (NavMesh.SamplePosition(targetPos, out hit, minWallDistance, NavMesh.AllAreas))
            {
                // Use smooth movement instead of immediate repositioning
                if (!isRepositioning)
                {
                    targetPosition = hit.position;
                    StartCoroutine(SmoothRepositioning());
                }
            }
        }
        else if (!isRepositioning)
        {
            lastSafePosition = transform.position;
        }
    }

    private bool IsPathBlocked()
    {
        if (agent == null || !agent.hasPath) return false;

        Vector3 direction = (agent.steeringTarget - transform.position).normalized;
        float checkDistance = agent.stoppingDistance + 1f;

        // Check at multiple heights with reduced spacing
        float[] heightChecks = new float[] { 0.1f, 0.5f, 1.0f };
        foreach (float height in heightChecks)
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position + Vector3.up * height, direction, out hit, checkDistance, shootableLayer))
            {
                if (System.Array.Exists(wallTags, tag => hit.collider.CompareTag(tag)))
                {
                    return true;
                }
            }
        }

        // Check for nearby walls with reduced radius
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, 1f, shootableLayer);
        foreach (Collider col in nearbyColliders)
        {
            if (System.Array.Exists(wallTags, tag => col.CompareTag(tag)))
            {
                Vector3 closestPoint = col.ClosestPoint(transform.position);
                if (Vector3.Distance(transform.position, closestPoint) < 0.75f)
                {
                    return true;
                }
            }
        }
        
        return false;
    }

    private void Update()
    {
        if (isDead) return;

        if (PhotonNetwork.IsMasterClient)
        {
            UpdateMasterClient();
        }
        else
        {
            UpdateClient();
        }

        // Update animations for both master and clients
        UpdateAnimations();
    }

    private void UpdateMasterClient()
    {
        CheckAndCorrectWallCollision();
        
        // Debug ray to visualize wall detection
        Debug.DrawRay(transform.position, transform.forward * minWallDistance, Color.red);
        Debug.DrawRay(transform.position, -transform.forward * minWallDistance, Color.red);
        Debug.DrawRay(transform.position, transform.right * minWallDistance, Color.red);
        Debug.DrawRay(transform.position, -transform.right * minWallDistance, Color.red);

        // Calculate movement values
        if (agent != null)
        {
            Vector3 velocity = agent.velocity;
            float speed = velocity.magnitude;
            
            Vector3 localVelocity = transform.InverseTransformDirection(velocity);
            networkHorizontal = localVelocity.x / agent.speed;
            networkVertical = localVelocity.z / agent.speed;
            networkIsRunning = speed > 0.1f;
            isMoving = speed > 0.1f;

            // Sync animations more frequently
            if (Time.time - lastAnimationSyncTime > ANIMATION_SYNC_INTERVAL)
            {
                photonView.RPC("SyncAnimationState", RpcTarget.All, networkHorizontal, networkVertical, networkIsRunning);
                lastAnimationSyncTime = Time.time;
            }
        }
    }

    private void UpdateClient()
    {
        if (!photonView.IsMine && !isDead)
        {
            // Calculate interpolation factor
            float timeSinceLastUpdate = Time.time - lastNetworkUpdateTime;
            float interpolationFactor = Mathf.Clamp01(timeSinceLastUpdate / NETWORK_SYNC_INTERVAL);
            
            // Smoothly interpolate position with increased speed and better error correction
            Vector3 targetPosition = networkPosition;
            if (Vector3.Distance(transform.position, targetPosition) > 5f)
            {
                // If too far, teleport
                transform.position = targetPosition;
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.Warp(targetPosition);
                }
            }
            else
            {
                // Normal interpolation with velocity prediction
                Vector3 predictedPosition = networkPosition + (networkVelocity * timeSinceLastUpdate);
                transform.position = Vector3.Lerp(transform.position, predictedPosition, Time.deltaTime * networkLerpSpeed);
            }
            
            // Smoothly interpolate rotation with increased speed
            transform.rotation = Quaternion.Lerp(transform.rotation, networkRotation, Time.deltaTime * networkLerpSpeed);
            
            // Update animator parameters smoothly
            if (animator != null)
            {
                animator.SetFloat(hashHorizontal, Mathf.Lerp(animator.GetFloat(hashHorizontal), networkHorizontal, Time.deltaTime / animationSmoothTime));
                animator.SetFloat(hashVertical, Mathf.Lerp(animator.GetFloat(hashVertical), networkVertical, Time.deltaTime / animationSmoothTime));
                animator.SetBool(hashRunning, networkIsRunning);
            }
            
            // Update agent if available
            if (agent != null && agent.isOnNavMesh)
            {
                // Update agent's position
                agent.nextPosition = transform.position;
                
                // Update destination if significantly different
                float distanceToDestination = Vector3.Distance(agent.destination, networkPosition);
                if (distanceToDestination > 0.5f)
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.CalculatePath(transform.position, networkPosition, NavMesh.AllAreas, path))
                    {
                        agent.destination = networkPosition;
                    }
                }
                
                // Update velocity
                agent.velocity = networkVelocity;
            }
        }
    }

    private void UpdateAnimations()
    {
        if (animator != null)
        {
            // Apply animation parameters with smoothing
            animator.SetFloat(hashHorizontal, networkHorizontal, animationSmoothTime, Time.deltaTime);
            animator.SetFloat(hashVertical, networkVertical, animationSmoothTime, Time.deltaTime);
            animator.SetBool(hashRunning, networkIsRunning);

            // Ensure animation states are properly set
            animator.SetBool(hashIsDead, isDead);
        }
    }

    private bool IsValidDestination(Vector3 targetPosition)
    {
        // First check if position is on NavMesh
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(targetPosition, out hit, 1.0f, NavMesh.AllAreas))
        {
            Debug.Log($"NPC {npcViewID} invalid destination - not on NavMesh: {targetPosition}");
            return false;
        }

        // Check for walls in all directions
        for (float angle = 0; angle < 360; angle += 45)
        {
            Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            RaycastHit wallHit;
            if (Physics.Raycast(targetPosition + Vector3.up, direction, out wallHit, minWallDistance, shootableLayer))
            {
                if (System.Array.Exists(wallTags, tag => wallHit.collider.CompareTag(tag)))
                {
                    Debug.Log($"NPC {npcViewID} invalid destination - wall detected at angle {angle}: {wallHit.collider.name}");
                    return false;
                }
            }
        }

        // Check the path to target
        Vector3 directionToTarget = (targetPosition - transform.position).normalized;
        float distanceToTarget = Vector3.Distance(transform.position, targetPosition);
        
        RaycastHit pathHit;
        if (Physics.Raycast(transform.position + Vector3.up, directionToTarget, out pathHit, distanceToTarget, shootableLayer))
        {
            if (System.Array.Exists(wallTags, tag => pathHit.collider.CompareTag(tag)))
            {
                Debug.Log($"NPC {npcViewID} invalid path - wall blocking: {pathHit.collider.name}");
                return false;
            }
        }

        Debug.Log($"NPC {npcViewID} valid destination found: {targetPosition}");
        return true;
    }

    private IEnumerator AIRoutine()
    {
        Debug.Log($"NPC {npcViewID} started AI routine");
        
        yield return new WaitForSeconds(Random.Range(0.1f, 0.5f));
        
        while (!isDead)
        {
            if (agent != null && agent.isOnNavMesh && !isMoving)
            {
                Vector3 randomPos = startPosition + Random.insideUnitSphere * patrolRadius;
                randomPos.y = transform.position.y;

                NavMeshHit hit;
                if (NavMesh.SamplePosition(randomPos, out hit, patrolRadius, NavMesh.AllAreas))
                {
                    agent.speed = moveSpeed;
                    agent.SetDestination(hit.position);
                    isMoving = true;
                    
                    float timeout = 0;
                    while (agent.pathPending || 
                          (agent.hasPath && agent.remainingDistance > agent.stoppingDistance) && 
                          timeout < 10f)
                    {
                        if (IsPathBlocked())
                        {
                            agent.ResetPath();
                            isMoving = false;
                            break;
                        }
                        
                        timeout += 0.1f;
                        yield return new WaitForSeconds(0.1f);
                    }
                    
                    isMoving = false;
                }
            }

            yield return new WaitForSeconds(Random.Range(minWaitTime, maxWaitTime));
        }
    }

    private IEnumerator FindPlayerRoutine()
    {
        while (!isDead)
        {
            FindAndAttackPlayer();
            yield return new WaitForSeconds(0.5f);
        }
    }

    private bool CanSeeTarget(Vector3 targetPosition)
    {
        Vector3 directionToTarget = (targetPosition - transform.position).normalized;
        float distanceToTarget = Vector3.Distance(transform.position, targetPosition);
        
        // Check line of sight from NPC's position
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up * 1.5f, directionToTarget, out hit, distanceToTarget))
        {
            if (System.Array.Exists(wallTags, tag => hit.collider.CompareTag(tag)))
            {
                return false; // Wall is blocking the view
            }
        }
        
        // Also check from gun position if available
        if (npcGun != null)
        {
            Vector3 gunPosition = npcGun.transform.position;
            directionToTarget = (targetPosition - gunPosition).normalized;
            distanceToTarget = Vector3.Distance(gunPosition, targetPosition);
            
            if (Physics.Raycast(gunPosition, directionToTarget, out hit, distanceToTarget))
            {
                if (System.Array.Exists(wallTags, tag => hit.collider.CompareTag(tag)))
                {
                    return false; // Wall is blocking the gun's view
                }
            }
        }
        
        return true;
    }

    private void FindAndAttackPlayer()
    {
        if (isDead || Time.time < nextAttackTime || isStuck) return;

        Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRange);
        Transform nearestPlayer = null;
        float nearestDistance = float.MaxValue;
        NavMeshPath bestPath = null;
        
        foreach (Collider col in colliders)
        {
            if (col.CompareTag("Player"))
            {
                PlayerHealth playerHealth = col.GetComponent<PlayerHealth>();
                if (playerHealth != null && playerHealth.IsDead())
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, col.transform.position);
                if (distance < nearestDistance)
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.CalculatePath(transform.position, col.transform.position, NavMesh.AllAreas, path))
                    {
                        if (path.status == NavMeshPathStatus.PathComplete)
                        {
                            // Calculate actual path length
                            float pathLength = CalculatePathLength(path);
                            if (pathLength < nearestDistance * 1.5f) // Allow slightly longer paths if they're valid
                            {
                                nearestDistance = distance;
                                nearestPlayer = col.transform;
                                bestPath = path;
                            }
                        }
                    }
                }
            }
        }

        if (nearestPlayer != null && bestPath != null)
        {
            Vector3 directionToPlayer = (nearestPlayer.position - transform.position).normalized;
            
            if (nearestDistance <= attackRange && CanSeeTarget(nearestPlayer.position))
            {
                // Handle attack logic...
                if (agent != null)
                {
                    agent.isStopped = true;
                    isMoving = false;
                }

                transform.rotation = Quaternion.Lerp(transform.rotation, 
                    Quaternion.LookRotation(directionToPlayer), 
                    Time.deltaTime * rotationSpeed);

                if (npcGun != null)
                {
                    npcGun.UpdateAiming(nearestPlayer.position);
                    if (IsAimedAtTarget(nearestPlayer.position) && CanSeeTarget(nearestPlayer.position))
                    {
                        Attack(nearestPlayer.gameObject);
                        npcGun.Shoot();
                        nextAttackTime = Time.time + attackCooldown;
                    }
                }
            }
            else if (nearestDistance <= detectionRange)
            {
                // Use the pre-calculated path
                if (agent != null && !isRepositioning)
                {
                    agent.isStopped = false;
                    agent.speed = moveSpeed * 1.5f;
                    agent.SetPath(bestPath);
                    isMoving = true;
                    CheckIfStuck();
                }
                if (npcGun != null)
                {
                    npcGun.UpdateAiming(nearestPlayer.position);
                }
            }
        }
        else
        {
            ResetChaseState();
        }
    }

    private bool IsAimedAtTarget(Vector3 targetPosition)
    {
        if (!CanSeeTarget(targetPosition))
        {
            return false;
        }

        Vector3 directionToTarget = (targetPosition - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, directionToTarget);
        return angle < 30f;
    }

    private bool IsPlayerAlive(GameObject player)
    {
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        return playerHealth != null && !playerHealth.IsDead();
    }

    private void Attack(GameObject player)
    {
        if (!IsPlayerAlive(player)) return;

        // Play attack/shoot animation
        photonView.RPC("PlayShootAnimation", RpcTarget.All);

        // Apply damage to player
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        if (playerHealth != null && playerHealth.photonView != null)
        {
            playerHealth.photonView.RPC("TakeDamage", RpcTarget.All, damageAmount, "NPC");
        }
    }

    [PunRPC]
    private void PlayShootAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(hashShootTrigger);
        }
    }
    
    [PunRPC]
    private void PlayHurtAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(hashIsHurt);
        }
    }
    
    [PunRPC]
    private void PlayDeathAnimation()
    {
        if (animator != null)
        {
            animator.SetBool(hashIsDead, true);
            animator.SetTrigger(hashDieTrigger);
        }
    }
    
    [PunRPC]
    private void SyncAnimationState(float horizontal, float vertical, bool isRunning)
    {
        networkHorizontal = horizontal;
        networkVertical = vertical;
        networkIsRunning = isRunning;

        // Force immediate update for smoother transitions
        if (animator != null)
        {
            animator.SetFloat(hashHorizontal, horizontal, animationSmoothTime, Time.deltaTime);
            animator.SetFloat(hashVertical, vertical, animationSmoothTime, Time.deltaTime);
            animator.SetBool(hashRunning, isRunning);
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Calculate network lag
            float lag = Mathf.Abs((float)(PhotonNetwork.Time - info.SentServerTime));
            
            // Send position and movement data
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(agent != null ? agent.velocity : Vector3.zero);
            stream.SendNext(agent != null ? agent.destination : transform.position);
            stream.SendNext(isDead);
            
            // Send animation data
            stream.SendNext(animator != null ? animator.GetFloat(hashHorizontal) : 0f);
            stream.SendNext(animator != null ? animator.GetFloat(hashVertical) : 0f);
            stream.SendNext(networkIsRunning);
        }
        else
        {
            // Store last values for interpolation
            lastNetworkPosition = networkPosition;
            lastNetworkRotation = networkRotation;
            
            // Receive position and movement data
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            networkVelocity = (Vector3)stream.ReceiveNext();
            Vector3 newDestination = (Vector3)stream.ReceiveNext();
            bool newIsDead = (bool)stream.ReceiveNext();
            
            // Receive animation data
            networkHorizontal = (float)stream.ReceiveNext();
            networkVertical = (float)stream.ReceiveNext();
            networkIsRunning = (bool)stream.ReceiveNext();
            
            // Calculate network lag
            float lag = Mathf.Abs((float)(PhotonNetwork.Time - info.SentServerTime));
            
            // Extrapolate position based on velocity and lag
            networkPosition += networkVelocity * lag;
            
            // Update agent destination if changed significantly
            if (agent != null && Vector3.Distance(agent.destination, newDestination) > 0.1f)
            {
                agent.destination = newDestination;
            }

            // Update death state if changed
            if (isDead != newIsDead)
            {
                isDead = newIsDead;
                if (isDead)
                {
                    PlayDeathAnimation();
                }
            }
            
            // Record the time when we received new network data
            lastNetworkUpdateTime = Time.time;
        }
    }

    // Handle damage and death
    public void HandleDamage()
    {
        if (isDead) return;
        photonView.RPC("PlayHurtAnimation", RpcTarget.All);
    }

    public void HandleDeath()
    {
        if (isDead) return;
        isDead = true;

        // Stop movement
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        // Disable the gun/shooting component
        if (npcGun != null)
        {
            npcGun.enabled = false;
        }

        // Stop all coroutines
        StopAllCoroutines();
        
        // Play death animation
        photonView.RPC("PlayDeathAnimation", RpcTarget.All);
    }

    private void CheckIfStuck()
    {
        if (Vector3.Distance(transform.position, lastPosition) < stuckCheckDistance)
        {
            if (Time.time - lastMovementTime > stuckCheckTime)
            {
                if (!isStuck)
                {
                    isStuck = true;
                    StartCoroutine(HandleStuckState());
                }
            }
        }
        else
        {
            lastPosition = transform.position;
            lastMovementTime = Time.time;
            isStuck = false;
            pathRetryCount = 0;
        }
    }

    private IEnumerator HandleStuckState()
    {
        if (pathRetryCount >= maxPathRetries)
        {
            // Try to find any valid path to the target area
            if (currentTarget != null)
            {
                Vector3[] checkPoints = GenerateCheckPointsAroundTarget(currentTarget.position);
                foreach (Vector3 point in checkPoints)
                {
                    NavMeshPath alternatePath = new NavMeshPath();
                    if (NavMesh.CalculatePath(transform.position, point, NavMesh.AllAreas, alternatePath))
                    {
                        if (alternatePath.status == NavMeshPathStatus.PathComplete)
                        {
                            if (agent != null)
                            {
                                agent.SetPath(alternatePath);
                                pathRetryCount = 0;
                                isStuck = false;
                                yield break;
                            }
                        }
                    }
                }
            }

            // If still no path found, retreat
            if (agent != null)
            {
                agent.isStopped = true;
                agent.ResetPath();
                targetPosition = lastSafePosition;
                StartCoroutine(SmoothRepositioning());
            }
            yield return new WaitForSeconds(1f);
            pathRetryCount = 0;
            isStuck = false;
            yield break;
        }

        // Try to find alternative paths with more varied positions
        for (int i = 0; i < 8; i++)
        {
            Vector3 randomOffset = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward * Random.Range(3f, 7f);
            Vector3 alternativeTarget = transform.position + randomOffset;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(alternativeTarget, out hit, 5f, NavMesh.AllAreas))
            {
                if (agent != null && IsValidPath(hit.position))
                {
                    agent.SetDestination(hit.position);
                    pathRetryCount++;
                    break;
                }
            }
        }

        yield return new WaitForSeconds(pathRecalculationTime);
        isStuck = false;
    }

    private Vector3[] GenerateCheckPointsAroundTarget(Vector3 targetPos)
    {
        List<Vector3> points = new List<Vector3>();
        float[] distances = { 2f, 4f, 6f };
        int angleStep = 45;

        foreach (float dist in distances)
        {
            for (int angle = 0; angle < 360; angle += angleStep)
            {
                Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * dist;
                points.Add(targetPos + offset);
            }
        }

        return points.ToArray();
    }

    private bool IsValidPath(Vector3 destination)
    {
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(transform.position, destination, NavMesh.AllAreas, path))
        {
            return false;
        }

        if (path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        // Check each path segment with smaller intervals
        Vector3 previousPoint = path.corners[0];
        for (int i = 1; i < path.corners.Length; i++)
        {
            Vector3 currentPoint = path.corners[i];
            float segmentLength = Vector3.Distance(previousPoint, currentPoint);
            Vector3 direction = (currentPoint - previousPoint).normalized;
            
            // Check multiple points along the path segment
            for (float dist = 0; dist < segmentLength; dist += 0.5f)
            {
                Vector3 checkPoint = previousPoint + direction * dist;
                // Check a bit above ground level to avoid floor collisions
                Vector3 checkPosition = checkPoint + Vector3.up * 0.5f;
                
                // Use smaller radius for checking walls
                Collider[] nearbyColliders = Physics.OverlapSphere(checkPosition, 0.4f, shootableLayer);
                foreach (Collider col in nearbyColliders)
                {
                    if (System.Array.Exists(wallTags, tag => col.CompareTag(tag)))
                    {
                        // Don't immediately reject - check if there's enough space to pass
                        if (!HasSpaceToCross(checkPosition))
                        {
                            return false;
                        }
                    }
                }
            }
            previousPoint = currentPoint;
        }

        return true;
    }

    private bool HasSpaceToCross(Vector3 position)
    {
        // Check in multiple directions for a passage
        Vector3[] directions = {
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            (Vector3.forward + Vector3.right).normalized,
            (Vector3.forward + Vector3.left).normalized,
            (Vector3.back + Vector3.right).normalized,
            (Vector3.back + Vector3.left).normalized
        };

        foreach (Vector3 dir in directions)
        {
            // Check if there's enough space to pass through
            if (!Physics.SphereCast(position, 0.3f, dir, out RaycastHit hit, 1f, shootableLayer))
            {
                return true; // Found a clear path
            }
        }
        return false;
    }

    private float CalculatePathLength(NavMeshPath path)
    {
        float length = 0;
        if (path.corners.Length < 2) return 0;
        
        for (int i = 1; i < path.corners.Length; i++)
        {
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }
        return length;
    }

    private void ResetChaseState()
    {
        if (agent != null)
        {
            agent.isStopped = false;
            agent.speed = moveSpeed;
        }
        if (npcGun != null)
        {
            npcGun.ResetAiming();
        }
        if (isMoving && !agent.hasPath)
        {
            isMoving = false;
        }
        isStuck = false;
        pathRetryCount = 0;
    }
}