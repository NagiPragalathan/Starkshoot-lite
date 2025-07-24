using Photon.Pun;
using UnityEngine;

public class NPCTpsGun : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Gun Settings")]
    [SerializeField] private float aimSmoothSpeed = 5f;
    [SerializeField] private ParticleSystem gunParticles;
    [SerializeField] private AudioSource gunAudio;
    [SerializeField] private Animator animator;

    [Header("Aim Settings")]
    [SerializeField] private float verticalAimSpeed = 3f;
    [SerializeField] private float maxUpwardAngle = 40f;
    [SerializeField] private float maxDownwardAngle = -30f;
    [SerializeField] private float positionLerpSpeed = 8f;
    
    [Header("Position Adjustment")]
    [SerializeField] private float maxHeightAdjustment = 0.3f;
    [SerializeField] private float minHeightAdjustment = -0.2f;

    // Initial transform values
    private readonly Vector3 initialLocalPosition = new Vector3(-0.079f, 0.188f, 0.372f);
    private readonly Quaternion initialLocalRotation = new Quaternion(0.035574f, -0.37492f, 0.075489f, 0.923294f);

    // Runtime variables
    private Vector3 currentAimPosition;
    private Quaternion currentAimRotation;
    private float currentVerticalAngle = 0f;
    private Vector3 targetPosition;
    private bool hasTarget = false;

    // Network sync variables
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private bool isShooting = false;

    private Transform npcTransform;
    private Vector3 lastValidPosition;

    [Header("Bullet Effects")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private Transform bulletSpawnPoint;
    [SerializeField] private float bulletSpeed = 30f;
    [SerializeField] private float bulletLifetime = 2f;
    [SerializeField] private TrailRenderer bulletTrail;
    [SerializeField] private GameObject muzzleFlashPrefab;
    
    [Header("Bullet Properties")]
    [SerializeField] private int bulletDamage = 20;
    [SerializeField] private float bulletSpread = 0.05f;

    private void Start()
    {
        npcTransform = transform.parent;
        
        // Set initial position and rotation
        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation;
        
        currentAimPosition = initialLocalPosition;
        currentAimRotation = initialLocalRotation;
        lastValidPosition = transform.position;

        if (!photonView.IsMine)
        {
            networkPosition = initialLocalPosition;
            networkRotation = initialLocalRotation;
        }

        SetupAudio();
    }

    private void SetupAudio()
    {
        if (gunAudio == null)
        {
            gunAudio = gameObject.AddComponent<AudioSource>();
            gunAudio.spatialBlend = 1f;
            gunAudio.maxDistance = 30f;
            gunAudio.rolloffMode = AudioRolloffMode.Linear;
        }
    }

    public void UpdateAiming(Vector3 playerPosition)
    {
        if (!photonView.IsMine) return;

        hasTarget = true;
        targetPosition = playerPosition;
        
        // Calculate direction to player
        Vector3 directionToTarget = (playerPosition - transform.position).normalized;
        
        // Calculate vertical angle to target
        float targetVerticalAngle = Mathf.Asin(directionToTarget.y) * Mathf.Rad2Deg;
        targetVerticalAngle = Mathf.Clamp(targetVerticalAngle, maxDownwardAngle, maxUpwardAngle);
        
        // Smoothly adjust vertical angle
        currentVerticalAngle = Mathf.Lerp(currentVerticalAngle, targetVerticalAngle, Time.deltaTime * verticalAimSpeed);

        // Calculate height adjustment based on target height
        float heightDifference = playerPosition.y - transform.position.y;
        float heightAdjustment = Mathf.Clamp(heightDifference * 0.2f, minHeightAdjustment, maxHeightAdjustment);

        // Update gun position
        Vector3 targetLocalPosition = initialLocalPosition + new Vector3(0, heightAdjustment, 0);
        currentAimPosition = Vector3.Lerp(currentAimPosition, targetLocalPosition, Time.deltaTime * positionLerpSpeed);

        // Calculate aim rotation
        Vector3 horizontalDirection = new Vector3(directionToTarget.x, 0, directionToTarget.z).normalized;
        float horizontalAngle = Vector3.SignedAngle(npcTransform.forward, horizontalDirection, Vector3.up);
        
        // Create rotation based on both horizontal and vertical angles
        Quaternion targetRotation = initialLocalRotation * Quaternion.Euler(currentVerticalAngle, horizontalAngle, 0);
        currentAimRotation = Quaternion.Lerp(currentAimRotation, targetRotation, Time.deltaTime * aimSmoothSpeed);

        // Apply position and rotation
        ApplyTransform(currentAimPosition, currentAimRotation);
    }

    private void ApplyTransform(Vector3 position, Quaternion rotation)
    {
        // Validate position before applying
        if (!float.IsNaN(position.x) && !float.IsNaN(position.y) && !float.IsNaN(position.z))
        {
            transform.localPosition = position;
            lastValidPosition = position;
        }
        else
        {
            transform.localPosition = lastValidPosition;
        }

        // Apply rotation if valid
        if (!float.IsNaN(rotation.x) && !float.IsNaN(rotation.y) && !float.IsNaN(rotation.z) && !float.IsNaN(rotation.w))
        {
            transform.localRotation = rotation;
        }
    }

    public void ResetAiming()
    {
        if (!photonView.IsMine) return;

        hasTarget = false;
        
        // Smoothly return to initial position and rotation
        currentAimPosition = Vector3.Lerp(currentAimPosition, initialLocalPosition, Time.deltaTime * positionLerpSpeed);
        currentAimRotation = Quaternion.Lerp(currentAimRotation, initialLocalRotation, Time.deltaTime * aimSmoothSpeed);
        currentVerticalAngle = 0f;

        ApplyTransform(currentAimPosition, currentAimRotation);
    }

    public void Shoot()
    {
        if (!photonView.IsMine || !hasTarget) return;
        photonView.RPC("RPCShoot", RpcTarget.All);
    }

    [PunRPC]
    private void RPCShoot()
    {
        // Play gun audio
        if (gunAudio != null && !gunAudio.isPlaying)
        {
            gunAudio.Play();
        }

        // Spawn bullet
        SpawnBullet();

        // Play particle effects
        if (gunParticles != null)
        {
            if (gunParticles.isPlaying)
            {
                gunParticles.Stop();
            }
            gunParticles.Play();
        }

        // Show muzzle flash
        ShowMuzzleFlash();

        // Trigger animation
        if (animator != null)
        {
            animator.SetTrigger("Shoot");
        }
    }

    private void SpawnBullet()
    {
        if (bulletPrefab == null || bulletSpawnPoint == null) return;

        // Calculate bullet direction with slight spread but keep it horizontal
        Vector3 direction = bulletSpawnPoint.forward;
        direction.y = 0; // Keep it horizontal
        direction = direction.normalized;

        // Spawn bullet with horizontal rotation only
        Quaternion bulletRotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 90, 0);
        GameObject bullet = Instantiate(bulletPrefab, bulletSpawnPoint.position, bulletRotation);

        // Set up rigidbody
        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = bullet.AddComponent<Rigidbody>();
        }
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.velocity = direction * bulletSpeed;

        // Add trail renderer if specified
        if (bulletTrail != null)
        {
            TrailRenderer trail = bullet.GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = bullet.AddComponent<TrailRenderer>();
                trail.time = 0.05f; // Shorter trail time
                trail.startWidth = 0.05f;
                trail.endWidth = 0.0f;
                trail.material = bulletTrail.material;
            }
        }

        // Destroy bullet after a short time if it doesn't hit anything
        Destroy(bullet, 1f); // Reduced from previous lifetime
    }

    private void ShowMuzzleFlash()
    {
        if (muzzleFlashPrefab == null || bulletSpawnPoint == null) return;

        GameObject muzzleFlash = Instantiate(muzzleFlashPrefab, bulletSpawnPoint.position, bulletSpawnPoint.rotation);
        muzzleFlash.transform.parent = bulletSpawnPoint;
        Destroy(muzzleFlash, 0.1f);
    }

    private void LateUpdate()
    {
        if (!photonView.IsMine)
        {
            // Smooth interpolation for network clients
            transform.localPosition = Vector3.Lerp(transform.localPosition, networkPosition, Time.deltaTime * positionLerpSpeed);
            transform.localRotation = Quaternion.Lerp(transform.localRotation, networkRotation, Time.deltaTime * aimSmoothSpeed);
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.localPosition);
            stream.SendNext(transform.localRotation);
            stream.SendNext(isShooting);
            stream.SendNext(hasTarget);
        }
        else
        {
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            isShooting = (bool)stream.ReceiveNext();
            hasTarget = (bool)stream.ReceiveNext();
        }
    }
}