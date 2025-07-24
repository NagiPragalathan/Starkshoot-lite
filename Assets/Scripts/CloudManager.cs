using UnityEngine;
using Photon.Pun;

public class CloudManager : MonoBehaviourPunCallbacks
{
    [System.Serializable]
    public class CloudSetup
    {
        public GameObject startPoint;
        public GameObject endPoint;
    }

    [SerializeField] private GameObject cloudPrefab;
    [SerializeField] private CloudSetup[] cloudSetups;

    private void Start()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            SpawnClouds();
        }
    }

    private void SpawnClouds()
    {
        foreach (CloudSetup setup in cloudSetups)
        {
            if (setup.startPoint == null || setup.endPoint == null)
            {
                Debug.LogError("[CloudManager] Missing start or end point!");
                continue;
            }

            GameObject cloudObj = PhotonNetwork.Instantiate(
                cloudPrefab.name, 
                setup.startPoint.transform.position, 
                Quaternion.identity
            );

            CloudMovement cloudMove = cloudObj.GetComponent<CloudMovement>();
            if (cloudMove != null)
            {
                cloudMove.startPointObject = setup.startPoint;
                cloudMove.endPointObject = setup.endPoint;
            }
        }
    }

    // Optional: Add method to verify cloud synchronization
    public void VerifyCloudSync()
    {
        CloudMovement[] clouds = FindObjectsOfType<CloudMovement>();
        foreach (CloudMovement cloud in clouds)
        {
            Debug.Log($"Cloud {cloud.gameObject.name} - Position: {cloud.transform.position}, " +
                      $"IsMine: {cloud.photonView.IsMine}, " +
                      $"Owner: {cloud.photonView.Owner?.NickName}");
        }
    }
} 