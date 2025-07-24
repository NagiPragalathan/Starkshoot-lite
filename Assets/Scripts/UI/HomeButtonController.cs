using UnityEngine;
using UnityEngine.UI;
using System.Runtime.InteropServices;
using Photon.Pun;

public class HomeButtonController : MonoBehaviour
{
    [SerializeField]
    private string homeURL = "https://starkshoot.vercel.app/"; // Default URL that can be changed in Inspector

    #if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void RedirectToURL(string url);
    #endif

    private Button homeButton;

    void Start()
    {
        // Get the Button component
        homeButton = GetComponent<Button>();
        
        // Add click listener
        if (homeButton != null)
        {
            homeButton.onClick.AddListener(OnHomeButtonClick);
        }
        else
        {
            Debug.LogError("HomeButtonController: No Button component found!");
        }

        // Validate URL
        if (string.IsNullOrEmpty(homeURL))
        {
            Debug.LogWarning("HomeButtonController: Home URL is not set!");
        }
    }

    public void OnHomeButtonClick()
    {
        if (string.IsNullOrEmpty(homeURL))
        {
            Debug.LogError("HomeButtonController: Cannot redirect - URL is not set!");
            return;
        }

        // Clean up Photon connection if connected
        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.LeaveRoom();
        }

        // Handle redirection based on platform
        #if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL build
            RedirectToURL(homeURL);
        #else
            // Unity Editor or other platforms
            Debug.Log($"Redirecting to: {homeURL}");
            Application.OpenURL(homeURL);
        #endif
    }

    // Public method to set URL at runtime if needed
    public void SetHomeURL(string newURL)
    {
        if (string.IsNullOrEmpty(newURL))
        {
            Debug.LogError("HomeButtonController: Cannot set empty URL!");
            return;
        }
        homeURL = newURL;
    }

    void OnDestroy()
    {
        // Clean up the listener when the object is destroyed
        if (homeButton != null)
        {
            homeButton.onClick.RemoveListener(OnHomeButtonClick);
        }
    }
}
