using System.Runtime.InteropServices;
using UnityEngine;

public class LocalStorageManager : MonoBehaviour
{
    // Default key for wallet address
    private const string DEFAULT_WALLET_KEY = "wallet_address_test";

    #if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern string GetLocalStorageData(string key);
    
    [DllImport("__Internal")]
    private static extern void SetLocalStorageData(string key, string value);
    #endif

    /// <summary>
    /// Get data from local storage by key
    /// </summary>
    /// <param name="key">The key to retrieve data for</param>
    /// <returns>The retrieved data, or null if not found</returns>
    public string GetData(string key)
    {
        string data = "";

        // Check if we're running in a WebGL build
        #if UNITY_WEBGL && !UNITY_EDITOR
            data = GetLocalStorageData(key);
            if (string.IsNullOrEmpty(data))
            {
                Debug.Log($"No data found for key: {key}");
                return null;
            }
            Debug.Log($"Data found for key {key}: {data}");
        #else
            // When running in editor or non-WebGL build, use PlayerPrefs instead
            if (PlayerPrefs.HasKey(key))
            {
                data = PlayerPrefs.GetString(key);
                Debug.Log($"Data found for key {key}: {data}");
            }
            else
            {
                Debug.Log($"No data found for key: {key}");
                data = null;
            }
        #endif

        return data;
    }

    /// <summary>
    /// Store data in local storage with the given key
    /// </summary>
    /// <param name="key">The key to store the data under</param>
    /// <param name="value">The data to store</param>
    public void SetData(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("Cannot use empty key for local storage");
            return;
        }

        // Set the data
        #if UNITY_WEBGL && !UNITY_EDITOR
            SetLocalStorageData(key, value);
        #else
            // When running in editor or non-WebGL build, use PlayerPrefs instead
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        #endif

        Debug.Log($"Data set for key {key}: {value}");
    }

    /// <summary>
    /// Clear data for the given key
    /// </summary>
    /// <param name="key">The key to clear data for</param>
    public void ClearData(string key)
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
            SetLocalStorageData(key, "");
        #else
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        #endif
        Debug.Log($"Data cleared for key: {key}");
    }

    // For backward compatibility - Get wallet address using default key
    public string GetWalletAddress()
    {
        return GetData(DEFAULT_WALLET_KEY);
    }

    // For backward compatibility - Set wallet address using default key
    public void SetWalletAddress(string address)
    {
        SetData(DEFAULT_WALLET_KEY, address);
    }

    // Test function to demonstrate setting and getting wallet address with custom key
    public void TestStorageWithKey(string key, string value)
    {
        // Set the test value
        Debug.Log($"Setting test value for key '{key}'...");
        SetData(key, value);
        
        // Get and verify
        Debug.Log($"Retrieving value for key '{key}'...");
        string retrievedValue = GetData(key);
        
        if (retrievedValue == value)
        {
            Debug.Log("Test successful! Value was stored and retrieved correctly.");
        }
        else
        {
            Debug.LogError($"Test failed! Retrieved: {retrievedValue}, Expected: {value}");
        }
    }

    // For backward compatibility - maintain the original test function
    public void TestWalletAddressStorage()
    {
        string testAddress = "0x123456789abcdef0123456789abcdef012345678";
        TestStorageWithKey(DEFAULT_WALLET_KEY, testAddress);
    }
}
