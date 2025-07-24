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
        CheckImpactResources();
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
        
        if (Physics.Raycast(shootRay, out shootHit, weaponRange, LayerMask.GetMask("Shootable")))
        {
            GameObject hitObject = shootHit.collider.gameObject;
            Debug.Log($"Hit object: {hitObject.name} with tag: {hitObject.tag}");

            if (hitObject.CompareTag("NPC"))
            {
                // FIXED: Use NPCHealth instead of NPCController
                NPCHealth npcHealth = hitObject.GetComponent<NPCHealth>();
                if (npcHealth != null && npcHealth.photonView != null)
                {
                    Debug.Log($"Applying damage to NPC with PhotonView ID: {npcHealth.photonView.ViewID}");
                    npcHealth.photonView.RPC("TakeDamage", RpcTarget.All, damagePerShot, PhotonNetwork.LocalPlayer.NickName);
                }
                else
                {
                    Debug.LogError($"NPC missing NPCHealth or PhotonView component: {hitObject.name}");
                }
            }
            else if (hitObject.CompareTag("Player"))
            {
                // Handle player damage
                PlayerHealth playerHealth = hitObject.GetComponent<PlayerHealth>();
                if (playerHealth != null && playerHealth.photonView != null)
                {
                    playerHealth.photonView.RPC("TakeDamage", RpcTarget.All, damagePerShot, PhotonNetwork.LocalPlayer.NickName);
                }
            }

            // Handle impact effects
            string impactEffectName = hitObject.CompareTag("NPC") ? "impactFlesh" : GetImpactEffectName(hitObject.tag);
            try
            {
                PhotonNetwork.Instantiate(impactEffectName, 
                    shootHit.point, 
                    Quaternion.FromToRotation(Vector3.up, shootHit.normal),
                    0);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error spawning impact effect: {e.Message}");
            }
        }

        tpsGun.RPCShoot();
    }

    private string GetImpactEffectName(string hitTag)
    {
        switch (hitTag.ToLower())
        {
            case "player":
            case "npc":
                return "impactFlesh";
            case "metal":
                return "impactMetal";
            case "wood":
                return "impactWood";
            case "stone":
            case "concrete":
                return "impactConcrete";
            default:
                return "impactFlesh"; // Default to flesh impact if no specific effect
        }
    }

    /// <summary>
    /// Coroutine function to disable shooting effect.
    /// <summary>
    public IEnumerator DisableShootingEffect() {
        yield return new WaitForSeconds(0.05f);
        gunLine.enabled = false;
    }

    private void CheckImpactResources()
    {
        string[] effectNames = new string[] 
        { 
            "impactFlesh",
            "impactMetal", 
            "impactWood",
            "impactConcrete"
        };

        foreach (string effectName in effectNames)
        {
            GameObject effect = Resources.Load<GameObject>(effectName);
            if (effect != null)
            {
                Debug.Log($"Found effect: {effectName}");
                PhotonView pv = effect.GetComponent<PhotonView>();
                if (pv != null)
                {
                    Debug.Log($"{effectName} has PhotonView component");
                }
                else
                {
                    Debug.LogError($"{effectName} is missing PhotonView component!");
                }
            }
            else
            {
                Debug.LogError($"Could not find effect: {effectName} in Resources folder");
            }
        }
    }

}
