using Photon.Pun;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;
using System.Collections;

public class FpsGun : MonoBehaviour {

    [SerializeField]
    private int damagePerShot = 20;
    [SerializeField]
    private float timeBetweenBullets = 0.2f;
    [SerializeField]
    private float weaponRange = 100.0f;
    [SerializeField]
    private TpsGun tpsGun;
    [SerializeField]
    private ParticleSystem gunParticles;
    [SerializeField]
    private LineRenderer gunLine;
    [SerializeField]
    private Animator animator;
    [SerializeField]
    private Camera raycastCamera;

    private float timer;

    /// <summary>
    /// Start is called on the frame when a script is enabled just before
    /// any of the Update methods is called the first time.
    /// </summary>
    void Start() {
        timer = 0.0f;
    }

    /// <summary>
    /// Update is called every frame, if the MonoBehaviour is enabled.
    /// </summary>
    void Update() {
        timer += Time.deltaTime;
        bool shooting = CrossPlatformInputManager.GetButton("Fire1");
        if (shooting && timer >= timeBetweenBullets && Time.timeScale != 0) {
            Shoot();
        }
        animator.SetBool("Firing", shooting);
    }

    /// <summary>
    /// Shoot once, this also calls RPCShoot for third person view gun.
    /// <summary>
    void Shoot() {
        timer = 0.0f;
        gunLine.enabled = true;
        StartCoroutine(DisableShootingEffect());
        
        if (gunParticles.isPlaying)
        {
            gunParticles.Stop();
        }
        gunParticles.Play();

        RaycastHit shootHit;
        Ray shootRay = raycastCamera.ScreenPointToRay(new Vector3(Screen.width/2, Screen.height/2, 0f));
        
        if (Physics.Raycast(shootRay, out shootHit, weaponRange))
        {
            Debug.Log($"[FpsGun] Hit object with tag: {shootHit.transform.tag}");
            
            if (shootHit.collider.CompareTag("NPC"))
            {
                NPCController npc = shootHit.collider.GetComponent<NPCController>();
                if (npc != null)
                {
                    Debug.Log($"[FpsGun] Attempting to damage NPC with {damagePerShot} damage");
                    // Important: Send RPC directly to all clients
                    npc.photonView.RPC("TakeNPCDamage", RpcTarget.All, damagePerShot, PhotonNetwork.LocalPlayer.NickName);
                    
                    // Spawn impact effect
                    SpawnImpactEffect("impactFlesh", shootHit.point, shootHit.normal);
                }
            }
        }
        
        if (tpsGun != null)
        {
            tpsGun.RPCShoot();
        }
    }

    private void SpawnImpactEffect(string effectName, Vector3 position, Vector3 normal)
    {
        try
        {
            GameObject effect = PhotonNetwork.Instantiate(effectName, position, Quaternion.LookRotation(normal));
            Destroy(effect, 2f);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to spawn impact effect: {e.Message}");
        }
    }

    /// <summary>
    /// Coroutine function to disable shooting effect.
    /// <summary>
    public IEnumerator DisableShootingEffect() {
        yield return new WaitForSeconds(0.05f);
        gunLine.enabled = false;
    }

}
