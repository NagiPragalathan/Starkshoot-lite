using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using System;
using UnityStandardAssets.Characters.FirstPerson;
using UnityEngine.AI;
using Random = UnityEngine.Random;
using System.Runtime.InteropServices;
using UnityEngine.Networking;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class NetworkManager : MonoBehaviourPunCallbacks {

    [SerializeField]
    private Text connectionText;
    [SerializeField]
    private Transform[] spawnPoints;
    [SerializeField]
    private Camera sceneCamera;
    [SerializeField]
    private GameObject[] playerModel;
    [SerializeField]
    private GameObject serverWindow;
    [SerializeField]
    private GameObject messageWindow;
    [SerializeField]
    private GameObject sightImage;
    [SerializeField]
    private InputField username;
    [SerializeField]
    private InputField roomName;
    [SerializeField]
    private InputField roomList;
    [SerializeField]
    private InputField messagesLog;
    [SerializeField]
    private Text scoreText;
    [SerializeField]
    private Text killsText;
    [SerializeField]
    private Text timerText;
    [SerializeField]
    private GameObject leaderboardPanel;
    [SerializeField]
    private Transform leaderboardContent;
    [SerializeField]
    private GameObject leaderboardEntryPrefab;
    [SerializeField]
    private Dropdown timeSelectionDropdown;
    [SerializeField]
    private float[] timeOptions = { 180f, 300f, 600f }; // 3, 5, 10 minutes in seconds
    [SerializeField]
    private GameObject startGameCanvas;

    [Header("NPC Settings")]
    [SerializeField] private GameObject npcPrefab;
    [SerializeField] private float npcSpawnDelay = 5f;

    [SerializeField]
    private Text walletText;
    private Button joinButton;
    private Text joinButtonText;

    private GameObject player;
    private Queue<string> messages;
    private const int messageCount = 10;
    private string nickNamePrefKey = "PlayerName";
    private Dictionary<string, PlayerStats> playerStats = new Dictionary<string, PlayerStats>();
    private float currentGameTime;
    private bool isGameActive = false;
    private Dictionary<string, int> killStreaks = new Dictionary<string, int>();
    private float roomListUpdateTimer = 0f;
    private const float ROOM_LIST_UPDATE_INTERVAL = 3f;
    private Dictionary<string, RoomInfo> cachedRoomList = new Dictionary<string, RoomInfo>();
    private Dictionary<string, int> roomPlayerCounts = new Dictionary<string, int>();
    private bool isReconnecting = false;
    private const float RECONNECT_INTERVAL = 2f;
    private const int MAX_RECONNECT_ATTEMPTS = 5;
    private int currentReconnectAttempts = 0;
    private string lastRoomName = null;
    private bool wasInRoom = false;
    private const float CONNECTION_CHECK_INTERVAL = 1f;
    private float connectionCheckTimer = 0f;

    // Add a new dictionary to track bot kills
    private Dictionary<string, int> botKills = new Dictionary<string, int>();

    // Add these new fields after the existing private fields around line 90
    private bool isMasterClientSwitching = false;
    private Dictionary<string, PlayerStats> backupPlayerStats = new Dictionary<string, PlayerStats>();
    private Dictionary<string, int> backupKillStreaks = new Dictionary<string, int>();
    private float backupGameTime = 0f;
    private bool backupGameActive = false;
    private List<Vector3> backupNPCPositions = new List<Vector3>();

    // Add this class to track player statistics
    private class PlayerStats {
        public int Score { get; set; }
        public int Kills { get; set; }

        public PlayerStats() {
            Score = 0;
            Kills = 0;
        }
    }

    // Add a HashSet to track processed kills
    private HashSet<string> processedKills = new HashSet<string>();

    // Add these at the top of the NetworkManager class
    private List<GameObject> activeNPCs = new List<GameObject>();
    private List<GameObject> deadNPCs = new List<GameObject>();
    private float npcCleanupInterval = 3f; // Check and cleanup every 3 seconds

    // Add this at the start of the class, after other private fields
    private const string PLAYER_STATS_PROP_KEY = "PlayerStats";

    // Add these constants at the top of the NetworkManager class
    private const int TARGET_TOTAL_PLAYERS = 6; // Total players (real + NPCs) we want in the room
    private const int MAX_REAL_PLAYERS = 6; // Maximum number of real players allowed

    #if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern string GetLocalStorageData(string key);
    #else
    private string GetLocalStorageData(string key)
    {
        return PlayerPrefs.GetString(key, "");
    }
    #endif

    private bool isWalletConnected = false;
    private string walletAddress = "";
    private bool isDataFetched = false;
    private bool isStaked = false;
    private bool isRoomNameFromAPI = false;
    private bool isTimerFromAPI = false;

    [Header("Debug Settings")]
    [SerializeField]
    private bool testDebugMode = false;
    [SerializeField]
    private string testWalletAddress = "0x06C3431c2D3F57BfE4de3A99Af9B53fc4f95197c";
    [SerializeField]
    private bool autoStartEnabled = false; // New debug setting for auto-start

    [SerializeField]
    private Button returnToLobbyButton;

    /// <summary>
    /// Start is called on the frame when a script is enabled just before
    /// any of the Update methods is called the first time.
    /// </summary>
    void Start() {
        messages = new Queue<string>(messageCount);
        
        // Get the join button and its text from serverWindow
        if (serverWindow != null)
        {
            joinButton = serverWindow.GetComponentInChildren<Button>();
            if (joinButton != null)
            {
                joinButtonText = joinButton.GetComponentInChildren<Text>();
            }
        }
        
        // Initialize UI elements
        InitializeUI();
        
        // Start the connection sequence
        StartCoroutine(ConnectionSequence());

        // Add listener for return to lobby button
        if (returnToLobbyButton != null) {
            returnToLobbyButton.onClick.AddListener(ReturnToLobby);
        }

        // Initialize fields as non-editable unless in debug mode
        UpdateFieldsInteractability(false);
    }

    private void InitializeUI()
    {
        // Initialize UI with default values
        if (scoreText != null) scoreText.text = ":0";
        if (killsText != null) killsText.text = ":0";
        
        // Setup time selection dropdown
        SetupTimeDropdown();
        
        // Initialize timer with default time (5 minutes)
        currentGameTime = timeOptions[1];
        if (timerText != null) {
            timerText.text = FormatTime(currentGameTime);
        }
        
        if (leaderboardPanel != null) {
            leaderboardPanel.SetActive(false);
        }

        // Initialize wallet text
        if (walletText != null) {
            walletText.text = "Checking wallet connection...";
        }

        // Initialize join button
        UpdateJoinButtonState(false, false);
        
        // Initialize player stats
        InitializePlayerStats();
    }

    private void UpdateJoinButtonState(bool isConnected, bool isStaked = false)
    {
        if (joinButton != null)
        {
            joinButton.interactable = testDebugMode || (isConnected && isStaked);
        }

        if (joinButtonText != null)
        {
            if (testDebugMode)
            {
                joinButtonText.text = "Join Room (Debug Mode)";
            }
            else if (!isConnected)
            {
                joinButtonText.text = "Please Connect Your Wallet";
            }
            else if (!isStaked)
            {
                joinButtonText.text = "Please Stake Your Tokens";
            }
            else
            {
                joinButtonText.text = "Join Room";
            }
        }
    }

    private IEnumerator ConnectionSequence()
    {
        // Step 1: Get wallet address from local storage
        yield return StartCoroutine(GetWalletAddressCoroutine());

        // Step 2: If wallet is connected, fetch user data
        if (isWalletConnected)
        {
            yield return StartCoroutine(FetchUserData(walletAddress));
        }

        // Step 3: Connect to Photon
        ConnectToPhoton();
    }

    private IEnumerator GetWalletAddressCoroutine()
    {
        try
        {
            if (testDebugMode)
            {
                // Use test wallet address in debug mode
                walletAddress = testWalletAddress;
                isWalletConnected = true;
                Debug.Log($"Debug Mode: Using test wallet address: {walletAddress}");
            }
            else
            {
                // Normal wallet address retrieval
                #if UNITY_WEBGL && !UNITY_EDITOR
                walletAddress = GetLocalStorageData("walletAddress");
                #else
                walletAddress = PlayerPrefs.GetString("walletAddress", "");
                #endif
                
                isWalletConnected = !string.IsNullOrEmpty(walletAddress);
            }

            if (isWalletConnected)
            {
                Debug.Log($"Wallet connected: {walletAddress}");
                if (connectionText != null)
                {
                    connectionText.text = "Wallet connected. Fetching user data...";
                }
                if (walletText != null)
                {
                    string formattedAddress = FormatWalletAddress(walletAddress);
                    walletText.text = $"Wallet: {formattedAddress}";
                }

                // Enable fields for editing since wallet is connected
                if (roomName != null)
                {
                    roomName.interactable = true;
                }
                if (timeSelectionDropdown != null)
                {
                    timeSelectionDropdown.interactable = true;
                }

                // Enable join button when wallet is connected (staking status will be updated after API call)
                UpdateJoinButtonState(true, false);
            }
            else
            {
                Debug.Log("No wallet connected");
                if (connectionText != null)
                {
                    connectionText.text = "Please connect your wallet to continue";
                }
                if (walletText != null)
                {
                    walletText.text = "Oops! Wallet Not Connected!";
                }

                // Disable fields when wallet is not connected
                if (roomName != null)
                {
                    roomName.interactable = false;
                }
                if (timeSelectionDropdown != null)
                {
                    timeSelectionDropdown.interactable = false;
                }

                if (username != null)
                {
                    username.interactable = testDebugMode;
                    if (PlayerPrefs.HasKey(nickNamePrefKey))
                    {
                        username.text = PlayerPrefs.GetString(nickNamePrefKey);
                    }
                }
                // Disable join button when wallet is not connected
                UpdateJoinButtonState(false, false);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error retrieving wallet address: {e.Message}");
            isWalletConnected = false;
            if (connectionText != null)
            {
                connectionText.text = "Error connecting wallet. Please try again.";
            }
            if (walletText != null)
            {
                walletText.text = "Oops! Wallet Not Connected!";
            }
            // Disable join button on error
            UpdateJoinButtonState(false, false);
        }

        yield return null;
    }

    private void TryAutoStart()
    {
        Debug.Log($"[Auto Start] Checking conditions - AutoStart Enabled: {autoStartEnabled}, Room from API: {isRoomNameFromAPI}, Timer from API: {isTimerFromAPI}, Has Username: {!string.IsNullOrEmpty(username.text)}, Wallet Connected: {isWalletConnected}, Is Staked: {isStaked}");

        if (!autoStartEnabled)
        {
            Debug.Log("[Auto Start] Auto-start is disabled in debug settings");
            ShowUI();
            return;
        }

        if (!isRoomNameFromAPI || !isTimerFromAPI || string.IsNullOrEmpty(username.text))
        {
            Debug.Log("[Auto Start] Not all data is from API");
            ShowUI();
            return;
        }

        if (!isWalletConnected)
        {
            Debug.Log("[Auto Start] Wallet not connected");
            ShowUI();
            return;
        }

        if (!testDebugMode && !isStaked)
        {
            Debug.Log("[Auto Start] User not in debug mode and not staked");
            ShowUI();
            return;
        }

        // All conditions met, start the game
        Debug.Log("[Auto Start] All conditions met, starting game automatically");
        HideUI();
        JoinRoom();
    }

    private void ShowUI()
    {
        if (serverWindow != null) serverWindow.SetActive(true);
        if (startGameCanvas != null) startGameCanvas.SetActive(true);
    }

    private void HideUI()
    {
        if (serverWindow != null) serverWindow.SetActive(false);
        if (startGameCanvas != null)
        {
            Destroy(startGameCanvas);
            startGameCanvas = null;
        }
    }

    private IEnumerator FetchUserData(string walletAddress)
    {
        Debug.Log($"[API] Starting to fetch user data for wallet: {walletAddress}");
        string url = $"https://starkshoot-server.vercel.app/api/user/{walletAddress}";
        
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.timeout = 10; // Set timeout to 10 seconds
            Debug.Log($"[API] Sending request to: {url}");
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    string responseText = www.downloadHandler.text;
                    Debug.Log($"[API] Received response: {responseText}");
                    
                    UserData userData = JsonConvert.DeserializeObject<UserData>(responseText);
                    if (userData == null)
                    {
                        Debug.LogError("[API] Failed to deserialize user data - response was null");
                        HandleWalletError();
                        yield break;
                    }
                    
                    Debug.Log($"[API] Parsed user data - Username: {userData.username}, Room: {userData.currentRoom}, Duration: {userData.duration}, Staked: {userData.isStaked}");
                    
                    // Set username
                    if (username != null)
                    {
                        username.text = userData.username;
                        Debug.Log($"[UI] Set username to: {userData.username}");
                    }

                    PlayerPrefs.SetString(nickNamePrefKey, userData.username);
                    PlayerPrefs.Save();

                    isStaked = userData.isStaked;
                    Debug.Log($"[State] Set staking status to: {isStaked}");
                    
                    // Set room name if available
                    if (roomName != null)
                    {
                        roomName.interactable = true;
                        if (!string.IsNullOrEmpty(userData.currentRoom))
                        {
                            roomName.text = userData.currentRoom;
                            isRoomNameFromAPI = true;
                            Debug.Log($"[UI] Set room name from API: {userData.currentRoom}");
                        }
                        else
                        {
                            string shortWallet = FormatWalletAddress(walletAddress);
                            roomName.text = $"Room_{shortWallet}";
                            isRoomNameFromAPI = false;
                            Debug.Log($"[UI] Set default room name: {roomName.text}");
                        }
                    }

                    // Handle duration setting
                    if (timeSelectionDropdown != null)
                    {
                        timeSelectionDropdown.interactable = true;
                        float durationSeconds = 300f; // Default to 5 minutes
                        
                        if (!string.IsNullOrEmpty(userData.duration))
                        {
                            if (float.TryParse(userData.duration, out float parsedDuration))
                            {
                                durationSeconds = parsedDuration;
                                isTimerFromAPI = true;
                                Debug.Log($"[UI] Parsed duration from API: {durationSeconds} seconds");
                            }
                            else
                            {
                                Debug.LogWarning($"[UI] Failed to parse duration: {userData.duration}");
                                isTimerFromAPI = false;
                            }
                        }
                        else
                        {
                            isTimerFromAPI = false;
                        }

                        // Rebuild time options list
                        List<float> timeOptionsList = timeOptions.ToList();
                        if (!timeOptionsList.Contains(durationSeconds))
                        {
                            timeOptionsList.Add(durationSeconds);
                            timeOptionsList.Sort();
                            timeOptions = timeOptionsList.ToArray();
                            Debug.Log($"[UI] Added duration {durationSeconds} to time options");
                        }
                        
                        // Rebuild the dropdown with updated options
                        SetupTimeDropdown();
                        Debug.Log($"[UI] Rebuilt time dropdown with {timeOptions.Length} options");

                        // Find and set the duration
                        int durationIndex = Array.IndexOf(timeOptions, durationSeconds);
                        if (durationIndex != -1)
                        {
                            timeSelectionDropdown.value = durationIndex;
                            Debug.Log($"[UI] Set time selection to index {durationIndex} ({durationSeconds} seconds)");
                        }
                        else
                        {
                            timeSelectionDropdown.value = 1; // Default to second option if not found
                            Debug.Log("[UI] Using default time selection");
                        }
                    }
                    else
                    {
                        Debug.LogError("[UI] Time selection dropdown is null!");
                    }

                    // Update fields interactability
                    UpdateFieldsInteractability(true);
                    Debug.Log("[UI] Updated fields interactability");
                    
                    // Update button state based on staking status
                    UpdateJoinButtonState(true, isStaked);
                    Debug.Log($"[UI] Updated join button state - Connected: true, Staked: {isStaked}");

                    isDataFetched = true;
                    Debug.Log($"[State] Successfully fetched and processed user data");
                    
                    if (connectionText != null)
                    {
                        if (testDebugMode || isStaked)
                        {
                            connectionText.text = "User data fetched successfully. Connecting to game...";
                        }
                        else
                        {
                            connectionText.text = "Please stake your tokens to join the game.";
                        }
                    }

                    // Check for auto-start after all data is processed
                    TryAutoStart();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[API] Error parsing user data: {e.Message}\nStack trace: {e.StackTrace}");
                    HandleWalletError();
                }
            }
            else
            {
                Debug.LogError($"[API] Error fetching user data: {www.error}\nResponse Code: {www.responseCode}");
                if (www.downloadHandler != null)
                {
                    Debug.LogError($"[API] Response text: {www.downloadHandler.text}");
                }
                HandleWalletError();
            }
        }
    }

    private void ConnectToPhoton()
    {
        if (isWalletConnected && !isDataFetched)
        {
            if (connectionText != null)
            {
                connectionText.text = "Error: Could not fetch user data. Please try again.";
            }
            return;
        }

        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.ConnectUsingSettings();
        
        if (connectionText != null)
        {
            connectionText.text = "Connecting to game server...";
        }
    }

    private void HandleWalletError()
    {
        Debug.Log("[Error Handler] Starting error recovery process");
        isDataFetched = false;
        
        // Keep fields editable if wallet is connected, regardless of error
        if (isWalletConnected)
        {
            Debug.Log("[Error Handler] Wallet is connected, keeping fields editable");
            
            // Enable and set default values for room name
            if (roomName != null)
            {
                roomName.interactable = true;
                string shortWallet = FormatWalletAddress(walletAddress);
                roomName.text = $"Room_{shortWallet}";
                Debug.Log($"[Error Handler] Set default room name: {roomName.text}");
            }
            else
            {
                Debug.LogError("[Error Handler] Room name input field is null!");
            }

            // Enable and set default values for time selection
            if (timeSelectionDropdown != null)
            {
                timeSelectionDropdown.interactable = true;
                // Default to 5 minutes (index 1)
                timeSelectionDropdown.value = 1;
                Debug.Log("[Error Handler] Reset time selection to default (5 minutes)");
            }
            else
            {
                Debug.LogError("[Error Handler] Time selection dropdown is null!");
            }

            // Update username if available in PlayerPrefs
            if (username != null)
            {
                username.interactable = testDebugMode;
                if (PlayerPrefs.HasKey(nickNamePrefKey))
                {
                    username.text = PlayerPrefs.GetString(nickNamePrefKey);
                    Debug.Log($"[Error Handler] Restored username from PlayerPrefs: {username.text}");
                }
            }
        }
        else
        {
            Debug.Log("[Error Handler] Wallet not connected, disabling fields");
            // Disable all fields when wallet is not connected
            if (roomName != null) roomName.interactable = false;
            if (timeSelectionDropdown != null) timeSelectionDropdown.interactable = false;
            if (username != null) username.interactable = testDebugMode;
        }
        
        // Update connection status text
        if (connectionText != null)
        {
            connectionText.text = "Error fetching user data. Please try again.";
            Debug.Log("[Error Handler] Updated connection status text");
        }

        // Update join button state
        UpdateJoinButtonState(isWalletConnected, false);
        Debug.Log($"[Error Handler] Updated join button state - Connected: {isWalletConnected}, Staked: false");

        // Always show UI on error
        if (serverWindow != null) serverWindow.SetActive(true);
        if (startGameCanvas != null) startGameCanvas.SetActive(true);
        
        Debug.Log("[Error Handler] Error recovery process completed");
    }

    void SetupTimeDropdown() {
        if (timeSelectionDropdown != null) {
            timeSelectionDropdown.ClearOptions();
            List<string> options = new List<string>();
            
            // Sort time options to ensure they're in ascending order
            Array.Sort(timeOptions);
            
            foreach (float time in timeOptions) {
                if (time < 60)
                {
                    options.Add($"{time} Sec");
                }
                else
                {
                    int minutes = Mathf.FloorToInt(time / 60f);
                    int seconds = Mathf.FloorToInt(time % 60f);
                    if (seconds == 0)
                    {
                        options.Add($"{minutes} Min");
                    }
                    else
                    {
                        options.Add($"{minutes}m {seconds}s");
                    }
                }
            }
            
            Debug.Log($"Setting up dropdown with {options.Count} options: {string.Join(", ", options)}");
            timeSelectionDropdown.AddOptions(options);
            
            // Default to 5 minutes (300 seconds) if available, otherwise use the middle option
            int defaultIndex = Array.IndexOf(timeOptions, 300f);
            if (defaultIndex == -1)
            {
                defaultIndex = options.Count > 1 ? 1 : 0;
            }
            timeSelectionDropdown.value = defaultIndex;
            Debug.Log($"Set dropdown default value to index {defaultIndex}");
            
            // Ensure the dropdown is interactable if wallet is connected
            timeSelectionDropdown.interactable = isWalletConnected;
        }
    }

    /// <summary>
    /// Called on the client when you have successfully connected to a master server.
    /// </summary>
    public override void OnConnectedToMaster() {
        Debug.Log("Connected to Master Server. Joining lobby...");
        
        if (connectionText != null)
        {
            connectionText.text = "Connected to game server. Joining lobby...";
        }
        
        if (isReconnecting && wasInRoom && !string.IsNullOrEmpty(lastRoomName)) {
            // Try to rejoin the previous room
            Debug.Log($"Attempting to rejoin room: {lastRoomName}");
            PhotonNetwork.RejoinRoom(lastRoomName);
        } else {
            // Normal connection flow
            PhotonNetwork.JoinLobby(TypedLobby.Default);
        }
    }

    /// <summary>
    /// Called on the client when the connection was lost or you disconnected from the server.
    /// </summary>
    /// <param name="cause">DisconnectCause data associated with this disconnect.</param>
    public override void OnDisconnected(DisconnectCause cause) {
        Debug.LogWarning($"Disconnected from server: {cause}");
        
        // Always unlock cursor and make it visible when disconnected
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Save current game state before attempting reconnection
        if (PhotonNetwork.IsMasterClient && isGameActive)
        {
            CreateGameStateBackup();
        }

        // Only attempt to reconnect if we're not showing the leaderboard
        if (leaderboardPanel == null || !leaderboardPanel.activeSelf) {
            if (cause != DisconnectCause.DisconnectByClientLogic) {
                StartCoroutine(TryReconnect());
            }

            if (connectionText != null) {
                connectionText.text = $"Disconnected: {cause}. Attempting to reconnect...";
            }
        } else {
            // We're showing the leaderboard, don't attempt to reconnect
            Debug.Log("Disconnected while showing leaderboard - not attempting to reconnect");
            if (connectionText != null) {
                connectionText.text = "";
            }
        }

        // Reset game state
        isGameActive = false;
    }

    /// <summary>
    /// Callback function on joined lobby.
    /// </summary>
    public override void OnJoinedLobby() {
        Debug.Log("Joined Lobby successfully!");
        serverWindow.SetActive(true);
        // Show the start game canvas when entering lobby
        if (startGameCanvas != null) {
            startGameCanvas.SetActive(true);
        }
        connectionText.text = "";
        
        // The room list will automatically start updating via OnRoomListUpdate callback
    }

    /// <summary>
    /// Callback function on reveived room list update.
    /// </summary>
    /// <param name="rooms">List of RoomInfo.</param>
    public override void OnRoomListUpdate(List<RoomInfo> rooms) {
        Debug.Log($"Room list updated. Total rooms: {rooms.Count}");
        
        foreach (RoomInfo room in rooms) {
            if (room.RemovedFromList) { 
                cachedRoomList.Remove(room.Name);
                roomPlayerCounts.Remove(room.Name);
                continue;
            }

            // Update or add room info to cache
            cachedRoomList[room.Name] = room;
            roomPlayerCounts[room.Name] = room.PlayerCount;
        }

        UpdateRoomListDisplay();
    }

    private void UpdateRoomListDisplay() {
        if (roomList == null) return;

        if (cachedRoomList.Count == 0) {
            roomList.text = "No rooms available.";
            return;
        }

        roomList.text = "";
        foreach (var kvp in cachedRoomList) {
            RoomInfo room = kvp.Value;
            if (room == null) continue;

            int realPlayerCount = room.CustomProperties.ContainsKey("RealPlayerCount") ? 
                (int)room.CustomProperties["RealPlayerCount"] : 0;

            string roomStatus = GetRoomStatusText(room, realPlayerCount);
            
            // Get game time from room properties
            string timeDisplay = "";
            if (room.CustomProperties.ContainsKey("GameTime")) {
                float gameTime = (float)room.CustomProperties["GameTime"];
                int minutes = Mathf.FloorToInt(gameTime / 60f);
                timeDisplay = $"{minutes} Minutes";
            }

            roomList.text += $"Room: {room.Name}\n" +
                            $"Players: {realPlayerCount}/{room.MaxPlayers}\n" +
                            $"Time: {timeDisplay}\n" +
                            $"Status: {roomStatus}\n" +
                            "-------------------\n";
        }
    }

    private string GetRoomStatusText(RoomInfo room, int currentPlayerCount) {
        if (!room.IsOpen) return "Closed";
        if (currentPlayerCount >= room.MaxPlayers) return "Full";
        if (room.CustomProperties.ContainsKey("GameState")) {
            string gameState = (string)room.CustomProperties["GameState"];
            if (gameState == "InProgress") return "Game in Progress";
            if (gameState == "Ending") return "Game Ending";
        }
        return "Waiting for Players";
    }

    private string GetRoomCustomPropertiesText(RoomInfo room) {
        if (room == null || room.CustomProperties == null) return "";

        string properties = "";
        if (room.CustomProperties.ContainsKey("GameTime")) {
            float gameTime = (float)room.CustomProperties["GameTime"];
            int minutes = Mathf.FloorToInt(gameTime / 60f);
            properties += $"Game Time: {minutes} minutes\n";
        }
        return properties;
    }

    /// <summary>
    /// The button click callback function for join room.
    /// </summary>
    public void JoinRoom() {
        if (!isWalletConnected)
        {
            connectionText.text = "Please connect your wallet first!";
            return;
        }

        // Skip staking check if in debug mode
        if (!testDebugMode && !isStaked)
        {
            connectionText.text = "Please stake your tokens to join the game!";
            return;
        }

        if (!isDataFetched)
        {
            connectionText.text = "Please wait while we fetch your user data...";
            return;
        }

        // Destroy the start game canvas immediately when join button is clicked
        if (startGameCanvas != null) {
            Destroy(startGameCanvas);
            startGameCanvas = null; // Clear the reference
        }

        // Hide all UI elements
        if (serverWindow != null) serverWindow.SetActive(false);
        if (messageWindow != null) messageWindow.SetActive(false);
        if (leaderboardPanel != null) leaderboardPanel.SetActive(false);
        
        connectionText.text = "Joining room...";
        
        PhotonNetwork.LocalPlayer.NickName = username.text;
        PlayerPrefs.SetString(nickNamePrefKey, username.text);
        
        RoomOptions roomOptions = new RoomOptions() {
            IsVisible = true,
            IsOpen = true,
            MaxPlayers = MAX_REAL_PLAYERS,
            PublishUserId = true,
            EmptyRoomTtl = 0,
            PlayerTtl = 0,
            CleanupCacheOnLeave = true,
            CustomRoomProperties = new ExitGames.Client.Photon.Hashtable() {
                {"GameTime", timeOptions[timeSelectionDropdown.value]},
                {"CreatedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")},
                {"GameState", "Waiting"},
                {"RealPlayerCount", 0},
                {"NPCCount", 0},
                {"TotalPlayers", 0}
            },
            CustomRoomPropertiesForLobby = new string[] { 
                "GameTime", 
                "CreatedAt", 
                "GameState",
                "RealPlayerCount",
                "NPCCount",
                "TotalPlayers"
            }
        };

        if (PhotonNetwork.IsConnectedAndReady) {
            PhotonNetwork.JoinOrCreateRoom(roomName.text, roomOptions, TypedLobby.Default);
        } else {
            connectionText.text = "PhotonNetwork connection is not ready, try restart it.";
            // Re-enable UI if join fails
            if (serverWindow != null) serverWindow.SetActive(true);
            // Re-enable start game canvas if join fails
            if (startGameCanvas != null) {
                startGameCanvas.SetActive(true);
            }
        }
    }

    /// <summary>
    /// Callback function on joined room.
    /// </summary>
    public override void OnJoinedRoom() {
        Debug.Log($"Joined room: {PhotonNetwork.CurrentRoom.Name}");
        
        // Hide/disable all UI elements
        if (serverWindow != null) serverWindow.SetActive(false);
        if (leaderboardPanel != null) leaderboardPanel.SetActive(false);
        
        // Show only necessary game UI
        if (messageWindow != null) messageWindow.SetActive(true);
        if (sightImage != null) sightImage.SetActive(true);
        
        connectionText.text = "";
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        // Get the game time from room properties
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("GameTime")) {
            float gameTime = (float)PhotonNetwork.CurrentRoom.CustomProperties["GameTime"];
            currentGameTime = gameTime;
        }
        
        // Load existing player stats from room properties if available
        LoadPlayerStatsFromRoom();
        
        // Start the game timer if master client
        if (PhotonNetwork.IsMasterClient) {
            isGameActive = true;
            photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);
            
            // Update NPC count based on new player count
            UpdateNPCCount();
            
            // Start periodic data synchronization
            StartCoroutine(PeriodicDataSync());
        }
        
        Respawn(0.0f);
        InitializePlayerStats();

        // If master client, force reset NPCs
        if (PhotonNetwork.IsMasterClient) {
            ForceResetNPCs();
        }
    }

    private void LoadPlayerStatsFromRoom()
    {
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PLAYER_STATS_PROP_KEY))
        {
            ExitGames.Client.Photon.Hashtable statsData = 
                (ExitGames.Client.Photon.Hashtable)PhotonNetwork.CurrentRoom.CustomProperties[PLAYER_STATS_PROP_KEY];
            
            foreach (DictionaryEntry entry in statsData)
            {
                string playerName = entry.Key.ToString();
                ExitGames.Client.Photon.Hashtable playerData = (ExitGames.Client.Photon.Hashtable)entry.Value;
                
                if (!playerStats.ContainsKey(playerName))
                {
                    playerStats[playerName] = new PlayerStats();
                }
                
                playerStats[playerName].Score = (int)playerData["Score"];
                playerStats[playerName].Kills = (int)playerData["Kills"];
                
                // Update UI if this is the local player
                if (playerName == PhotonNetwork.LocalPlayer.NickName)
                {
                    UpdateUIStats(playerStats[playerName].Score, playerStats[playerName].Kills);
                }
            }
            
            Debug.Log($"Loaded player stats from room properties for {statsData.Count} players");
        }
    }

    /// <summary>
    /// Start spawn or respawn a player.
    /// </summary>
    /// <param name="spawnTime">Time waited before spawn a player.</param>
    void Respawn(float spawnTime) {
        sightImage.SetActive(false);
        sceneCamera.enabled = true;
        StartCoroutine(RespawnCoroutine(spawnTime));
    }

    /// <summary>
    /// The coroutine function to spawn player.
    /// </summary>
    /// <param name="spawnTime">Time waited before spawn a player.</param>
    IEnumerator RespawnCoroutine(float spawnTime) {
        yield return new WaitForSeconds(spawnTime);
        messageWindow.SetActive(true);
        sightImage.SetActive(true);
        int playerIndex = Random.Range(0, playerModel.Length);
        int spawnIndex = Random.Range(0, spawnPoints.Length);
        player = PhotonNetwork.Instantiate(playerModel[playerIndex].name, spawnPoints[spawnIndex].position, spawnPoints[spawnIndex].rotation, 0);
        
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        playerHealth.RespawnEvent += Respawn;
        playerHealth.AddMessageEvent += AddMessage;
        
        sceneCamera.enabled = false;
        if (spawnTime == 0) {
            AddMessage("Player " + PhotonNetwork.LocalPlayer.NickName + " Joined Game.");
        } else {
            AddMessage("Player " + PhotonNetwork.LocalPlayer.NickName + " Respawned.");
        }
    }

    /// <summary>
    /// Add message to message panel.
    /// </summary>
    /// <param name="message">The message that we want to add.</param>
    void AddMessage(string message) {
        photonView.RPC("AddMessage_RPC", RpcTarget.All, message);
    }

    /// <summary>
    /// RPC function to call add message for each client.
    /// </summary>
    /// <param name="message">The message that we want to add.</param>
    [PunRPC]
    void AddMessage_RPC(string message) {
        messages.Enqueue(message);
        if (messages.Count > messageCount) {
            messages.Dequeue();
        }
        messagesLog.text = "";
        foreach (string m in messages) {
            messagesLog.text += m + "\n";
        }
    }

    /// <summary>
    /// Callback function when other player disconnected.
    /// </summary>
    public override void OnPlayerLeftRoom(Player other) {
        // Create backup before any master client changes
        if (PhotonNetwork.IsMasterClient)
        {
            CreateGameStateBackup();
        }
        
        if (PhotonNetwork.IsMasterClient) {
            Debug.Log($"Player {other.NickName} left - Updating NPC count");
            
            AddMessage("Player " + other.NickName + " Left Game.");
            
            // Update NPC count immediately when a player leaves
            UpdateNPCCount();
        }

        string roomName = PhotonNetwork.CurrentRoom.Name;
        if (roomPlayerCounts.ContainsKey(roomName)) {
            roomPlayerCounts[roomName] = PhotonNetwork.CurrentRoom.PlayerCount;
            photonView.RPC("UpdateRoomPlayerCount", RpcTarget.All, roomName, PhotonNetwork.CurrentRoom.PlayerCount);
        }
    }

    // Add this method to handle UI updates
    private void UpdateUIStats(int score, int kills) {
        // Ensure UI updates happen on the main thread
        if (scoreText != null) {
            scoreText.text = $":{score}";
            Debug.Log($"Updated score text to: {score}");
        } else {
            Debug.LogWarning("scoreText is null!");
        }
        
        if (killsText != null) {
            killsText.text = $":{kills}";
            Debug.Log($"Updated kills text to: {kills}");
        } else {
            Debug.LogWarning("killsText is null!");
        }
    }

    [PunRPC]
    private void UpdatePlayerStats_RPC(string playerName, int score, int kills) {
        Debug.Log($"UpdatePlayerStats_RPC received for {playerName}. Score: {score}, Kills: {kills}");
        
        // Update local dictionary first
        if (!playerStats.ContainsKey(playerName)) {
            playerStats[playerName] = new PlayerStats();
        }
        playerStats[playerName].Score = score;
        playerStats[playerName].Kills = kills;
        
        // Update room properties if we're in a room (and ensure this persists even during master client switches)
        if (PhotonNetwork.InRoom) {
            // Always try to update room properties, regardless of master client status
            try {
                ExitGames.Client.Photon.Hashtable statsData = new ExitGames.Client.Photon.Hashtable();
                if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PLAYER_STATS_PROP_KEY)) {
                    statsData = (ExitGames.Client.Photon.Hashtable)PhotonNetwork.CurrentRoom.CustomProperties[PLAYER_STATS_PROP_KEY];
                }
                
                // Create or update player stats in the room properties
                ExitGames.Client.Photon.Hashtable playerData = new ExitGames.Client.Photon.Hashtable() {
                    {"Score", score},
                    {"Kills", kills}
                };
                statsData[playerName] = playerData;
                
                // Update the room properties (only master client can do this, but we try anyway)
                if (PhotonNetwork.IsMasterClient) {
                    ExitGames.Client.Photon.Hashtable roomProps = new ExitGames.Client.Photon.Hashtable() {
                        {PLAYER_STATS_PROP_KEY, statsData}
                    };
                    PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);
                } else {
                    // If we're not master client, store locally and send to master client
                    photonView.RPC("RequestStatsUpdate_RPC", RpcTarget.MasterClient, playerName, score, kills);
                }
            } catch (System.Exception e) {
                Debug.LogError($"Error updating room properties in UpdatePlayerStats_RPC: {e.Message}");
            }
        }
        
        // Update UI for the local player
        if (playerName == PhotonNetwork.LocalPlayer.NickName) {
            UpdateUIStats(score, kills);
        }
    }

    // Add new RPC to handle stats update requests from non-master clients
    [PunRPC]
    private void RequestStatsUpdate_RPC(string playerName, int score, int kills) {
        if (!PhotonNetwork.IsMasterClient) return;
        
        Debug.Log($"Master client received stats update request for {playerName}: Score {score}, Kills {kills}");
        
        // Update local stats
        if (!playerStats.ContainsKey(playerName)) {
            playerStats[playerName] = new PlayerStats();
        }
        playerStats[playerName].Score = score;
        playerStats[playerName].Kills = kills;
        
        // Update room properties
        try {
            ExitGames.Client.Photon.Hashtable statsData = new ExitGames.Client.Photon.Hashtable();
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PLAYER_STATS_PROP_KEY)) {
                statsData = (ExitGames.Client.Photon.Hashtable)PhotonNetwork.CurrentRoom.CustomProperties[PLAYER_STATS_PROP_KEY];
            }
            
            ExitGames.Client.Photon.Hashtable playerData = new ExitGames.Client.Photon.Hashtable() {
                {"Score", score},
                {"Kills", kills}
            };
            statsData[playerName] = playerData;
            
            ExitGames.Client.Photon.Hashtable roomProps = new ExitGames.Client.Photon.Hashtable() {
                {PLAYER_STATS_PROP_KEY, statsData}
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);
            
            Debug.Log($"Master client updated room properties for {playerName}");
        } catch (System.Exception e) {
            Debug.LogError($"Error updating room properties for {playerName}: {e.Message}");
        }
    }

    // Add a method to sync all data periodically
    private IEnumerator PeriodicDataSync() {
        while (PhotonNetwork.InRoom) {
            yield return new WaitForSeconds(5f); // Sync every 5 seconds
            
            if (PhotonNetwork.IsMasterClient && !isMasterClientSwitching) {
                // Sync all player stats to ensure consistency
                foreach (var kvp in playerStats) {
                    photonView.RPC("UpdatePlayerStats_RPC", RpcTarget.All, 
                        kvp.Key, kvp.Value.Score, kvp.Value.Kills);
                }
                
                // Sync game state
                photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);
                photonView.RPC("SyncGameState", RpcTarget.All, isGameActive);
                
                Debug.Log("Performed periodic data sync");
            }
        }
    }

    public void AddKill() {
        string playerName = PhotonNetwork.LocalPlayer.NickName;
        if (!playerStats.ContainsKey(playerName)) {
            playerStats[playerName] = new PlayerStats();
        }
        
        // Update local stats
        playerStats[playerName].Kills++;
        int currentScore = playerStats[playerName].Score + 100; // Add 100 points per kill
        playerStats[playerName].Score = currentScore;
        
        // Debug message
        Debug.Log($"AddKill called for {playerName}. Kills: {playerStats[playerName].Kills}, Score: {currentScore}");
        
        // Update UI immediately
        UpdateUIStats(currentScore, playerStats[playerName].Kills);
        
        // Send update to all clients
        photonView.RPC("UpdatePlayerStats_RPC", RpcTarget.All, 
            playerName, 
            currentScore, 
            playerStats[playerName].Kills);
    }

    public void AddScore(int scoreAmount) {
        string playerName = PhotonNetwork.LocalPlayer.NickName;
        if (!playerStats.ContainsKey(playerName)) {
            playerStats[playerName] = new PlayerStats();
        }
        
        // Update local stats
        int currentScore = playerStats[playerName].Score + scoreAmount;
        playerStats[playerName].Score = currentScore;
        
        // Debug message
        Debug.Log($"AddScore called for {playerName}. New Score: {currentScore}");
        
        // Update UI immediately
        UpdateUIStats(currentScore, playerStats[playerName].Kills);
        
        // Send update to all clients
        photonView.RPC("UpdatePlayerStats_RPC", RpcTarget.All, 
            playerName, 
            currentScore, 
            playerStats[playerName].Kills);
    }

    private void InitializePlayerStats() {
        string playerName = PhotonNetwork.LocalPlayer.NickName;
        if (!playerStats.ContainsKey(playerName)) {
            playerStats[playerName] = new PlayerStats();
            killStreaks[playerName] = 0;  // Initialize kill streak
            // Initialize UI with zero values
            UpdateUIStats(0, 0);
        }
    }

    void Update() {
        // Create regular backups of game state
        if (PhotonNetwork.IsMasterClient && !isMasterClientSwitching)
        {
            CreateGameStateBackup();
        }
        
        if (isGameActive && PhotonNetwork.IsMasterClient) {
            if (currentGameTime > 0) {
                currentGameTime -= Time.deltaTime;
                photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);

                if (currentGameTime <= 0) {
                    currentGameTime = 0;
                    photonView.RPC("EndGame", RpcTarget.All);
                }
            }
        }

        // Add room list refresh logic when in lobby
        if (PhotonNetwork.InLobby && !PhotonNetwork.InRoom) {
            roomListUpdateTimer -= Time.deltaTime;
            if (roomListUpdateTimer <= 0f) {
                roomListUpdateTimer = ROOM_LIST_UPDATE_INTERVAL;
                // Room list updates are automatically sent by the server
                // We just need to make sure we're properly handling the OnRoomListUpdate callback
                UpdateRoomListDisplay();
            }
        }

        // Add periodic refresh for room list
        float refreshTimer = 0f;
        const float REFRESH_INTERVAL = 1f; // Update every second

        if (PhotonNetwork.InLobby && !PhotonNetwork.InRoom) {
            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f) {
                refreshTimer = REFRESH_INTERVAL;
                UpdateRoomListDisplay();
            }
        }

        // Add connection monitoring
        if (PhotonNetwork.IsConnected) {
            connectionCheckTimer -= Time.deltaTime;
            if (connectionCheckTimer <= 0f) {
                connectionCheckTimer = CONNECTION_CHECK_INTERVAL;
                // Check if we're still properly connected
                if (PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer ||
                    PhotonNetwork.NetworkClientState == ClientState.Joined) {
                    // Connection is healthy
                    if (connectionText != null) {
                        connectionText.text = "";
                    }
                }
            }
        }

        // Check NPC count more frequently when game is active (every 1 second)
        if (PhotonNetwork.IsMasterClient && isGameActive && Time.frameCount % 60 == 0) // 60 frames ≈ 1 second at 60 FPS
        {
            MaintainNPCCount();
        }

        if (PhotonNetwork.IsMasterClient && Time.frameCount % 300 == 0) { // Check every ~5 seconds
            GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
            int maxAllowed = 6 - PhotonNetwork.CurrentRoom.PlayerCount;
            
            if (npcs.Length > maxAllowed) {
                Debug.Log("[PERIODIC CHECK] Found incorrect NPC count! Triggering emergency cleanup!");
                UpdateNPCCount(); // Force cleanup and respawn
            }
        }
    }

    [PunRPC]
    void SyncTimer(float time) {
        currentGameTime = time;
        if (timerText != null) {
            timerText.text = FormatTime(currentGameTime);
        }
    }

    string FormatTime(float timeInSeconds) {
        timeInSeconds = Mathf.Max(0, timeInSeconds); // Ensure time doesn't go negative
        int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60f);
        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    [PunRPC]
    void EndGame() {
        isGameActive = false;
        
        // Disable all player functionality
        if (player != null) {
            // Disable all interactive components
            var components = player.GetComponents<MonoBehaviour>();
            foreach (var component in components) {
                if (component != this && // Don't disable NetworkManager
                    (component.GetType().Name.Contains("Controller") ||
                     component.GetType().Name.Contains("Shooting") ||
                     component.GetType().Name.Contains("Weapon") ||
                     component.GetType().Name.Contains("Gun") ||
                     component.GetType().Name.Contains("Health") ||
                     component.GetType().Name.Contains("Mover"))) {
                    component.enabled = false;
                }
            }

            // Also disable components in children (weapons, etc.)
            var childComponents = player.GetComponentsInChildren<MonoBehaviour>();
            foreach (var component in childComponents) {
                if (component.GetType().Name.Contains("Weapon") ||
                    component.GetType().Name.Contains("Gun") ||
                    component.GetType().Name.Contains("Shooting")) {
                    component.enabled = false;
                }
            }
        }

        // Disable all NPCs
        GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
        foreach (GameObject npc in npcs)
        {
            NPCController controller = npc.GetComponent<NPCController>();
            if (controller != null)
            {
                controller.enabled = false;
            }
            
            NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.isStopped = true;
                agent.enabled = false;
            }
        }

        // Ensure cursor is visible and can interact with UI
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Force a final stats sync for all players
        if (PhotonNetwork.IsMasterClient)
        {
            // Set a room property to indicate game has ended
            ExitGames.Client.Photon.Hashtable gameEndProps = new ExitGames.Client.Photon.Hashtable();
            gameEndProps.Add("GameEnded", true);
            PhotonNetwork.CurrentRoom.SetCustomProperties(gameEndProps);
        }

        // Each player syncs their own stats
        string playerName = PhotonNetwork.LocalPlayer.NickName;
        if (playerStats.ContainsKey(playerName)) {
            photonView.RPC("UpdatePlayerStats_RPC", RpcTarget.All, 
                playerName, 
                playerStats[playerName].Score, 
                playerStats[playerName].Kills);
        }
        
        // Show leaderboard after a short delay to ensure stats are synced
        StartCoroutine(DelayedShowLeaderboard());
    }

    private IEnumerator DelayedShowLeaderboard() {
        // Wait longer to ensure all stats are synced across network
        yield return new WaitForSeconds(2f);
        
        // Additional wait if we're the master client to ensure all players have synced
        if (PhotonNetwork.IsMasterClient) {
            yield return new WaitForSeconds(1f);
        }
        
        ShowFinalLeaderboard();
    }

    void ShowFinalLeaderboard() {
        if (leaderboardContent == null || leaderboardPanel == null) {
            Debug.LogError("[Leaderboard] UI components are missing!");
            return;
        }

        Debug.Log("[Leaderboard] Starting to show leaderboard...");

        // Enable scene camera before disabling player
        if (sceneCamera != null) {
            sceneCamera.gameObject.SetActive(true);
            sceneCamera.enabled = true;
        }

        // Disable player and its camera
        if (player != null) {
            Camera playerCamera = player.GetComponentInChildren<Camera>();
            if (playerCamera != null) {
                playerCamera.enabled = false;
            }
            player.SetActive(false);
        }

        // Clear existing entries
        foreach (Transform child in leaderboardContent) {
            if (child != null) {
                Destroy(child.gameObject);
            }
        }
        Debug.Log("[Leaderboard] Cleared existing entries");

        // Store the stats data before disconnecting
        var sortedPlayers = new List<KeyValuePair<string, PlayerStats>>();
        
        if (PhotonNetwork.CurrentRoom != null && 
            PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PLAYER_STATS_PROP_KEY)) 
        {
            Debug.Log("[Leaderboard] Processing room properties and player stats...");
            ExitGames.Client.Photon.Hashtable statsData = 
                (ExitGames.Client.Photon.Hashtable)PhotonNetwork.CurrentRoom.CustomProperties[PLAYER_STATS_PROP_KEY];
            
            Debug.Log($"[Leaderboard] Found {statsData.Count} players in stats data");
            
            foreach (DictionaryEntry entry in statsData) {
                string playerName = entry.Key.ToString();
                ExitGames.Client.Photon.Hashtable playerData = (ExitGames.Client.Photon.Hashtable)entry.Value;
                
                PlayerStats stats = new PlayerStats {
                    Score = (int)playerData["Score"],
                    Kills = (int)playerData["Kills"]
                };
                
                sortedPlayers.Add(new KeyValuePair<string, PlayerStats>(playerName, stats));
                Debug.Log($"[Leaderboard] Processing player: {playerName}, Score: {stats.Score}, Kills: {stats.Kills}");

                // Send leaderboard entry to API for each player
                StartCoroutine(AddLeaderboardEntry(playerName, stats.Kills, stats.Score));
                
                // Update staking status to false for the local player only
                if (playerName == PhotonNetwork.LocalPlayer.NickName)
                {
                    Debug.Log($"[Leaderboard] Found local player {playerName}, updating staking status");
                    if (string.IsNullOrEmpty(walletAddress))
                    {
                        Debug.LogWarning("[Leaderboard] Wallet address is null or empty!");
                    }
                    else
                    {
                        Debug.Log($"[Leaderboard] Starting staking status update for wallet: {walletAddress}");
                        StartCoroutine(UpdateStakingStatus(walletAddress, false));
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("[Leaderboard] No player stats found in room properties!");
            // Fallback to local playerStats dictionary
            foreach (var stat in playerStats)
            {
                sortedPlayers.Add(new KeyValuePair<string, PlayerStats>(stat.Key, stat.Value));
            }
        }

        // Sort players by score and kills
        sortedPlayers = sortedPlayers
            .OrderByDescending(p => p.Value.Score)
            .ThenByDescending(p => p.Value.Kills)
            .ToList();
        Debug.Log($"[Leaderboard] Sorted {sortedPlayers.Count} players by score and kills");

        // Create leaderboard entries
        int maxEntries = 8;
        Debug.Log($"[Leaderboard] Creating up to {maxEntries} leaderboard entries");
        for (int i = 0; i < maxEntries; i++) {
            GameObject entry = Instantiate(leaderboardEntryPrefab, leaderboardContent);
            LeaderboardEntry entryScript = entry.GetComponent<LeaderboardEntry>();
            
            if (i < sortedPlayers.Count) {
                var playerStat = sortedPlayers[i];
                Debug.Log($"[Leaderboard] Creating entry {i+1}: {playerStat.Key}, Score: {playerStat.Value.Score}, Kills: {playerStat.Value.Kills}");
                entryScript.SetStats(
                    playerStat.Key,
                    playerStat.Value.Score,
                    playerStat.Value.Kills,
                    i + 1
                );
            } else {
                Debug.Log($"[Leaderboard] Creating empty entry for position {i+1}");
                entryScript.SetStats("-|-", 0, 0, i + 1);
                if (entryScript.scoreText != null) entryScript.scoreText.text = "-|-";
                if (entryScript.killsText != null) entryScript.killsText.text = "-|-";
            }
        }

        // Make sure the leaderboard is visible
        leaderboardPanel.SetActive(true);
        Debug.Log("[Leaderboard] Display completed successfully");

        // Start a coroutine to handle disconnection
        StartCoroutine(HandleGameEndDisconnection());
    }

    private IEnumerator HandleGameEndDisconnection()
    {
        // Wait for a longer time to ensure all players have seen the leaderboard
        yield return new WaitForSeconds(5f);
        
        // Additional wait for non-master clients
        if (!PhotonNetwork.IsMasterClient)
        {
            yield return new WaitForSeconds(2f);
        }
        
        // Now disconnect from Photon
        if (PhotonNetwork.IsConnected)
        {
            Debug.Log("[Leaderboard] Disconnecting from Photon server...");
            wasInRoom = false; // Prevent reconnection attempts
            isReconnecting = false; // Stop any ongoing reconnection attempts
            PhotonNetwork.Disconnect();
        }
    }

    private IEnumerator AddLeaderboardEntry(string username, int kills, int score)
    {
        // Only send data for the local player
        if (username != PhotonNetwork.LocalPlayer.NickName) yield break;

        string url = "https://starkshoot-server.vercel.app/api/leaderboard/add";

        LeaderboardEntryRequest requestData = new LeaderboardEntryRequest
        {
            walletAddress = walletAddress,
            kills = kills,
            score = score,
            roomId = PhotonNetwork.CurrentRoom.Name,
            username = username
        };

        string jsonData = JsonUtility.ToJson(requestData);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                LeaderboardEntryResponse response = JsonUtility.FromJson<LeaderboardEntryResponse>(www.downloadHandler.text);
                Debug.Log($"Successfully added leaderboard entry for {username}. Entry ID: {response._id}");
            }
            else
            {
                Debug.LogError($"Error adding leaderboard entry: {www.error}");
            }
        }
    }

    // Remove the duplicate OnRoomPropertiesUpdate method and combine functionality into a single method
    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged) {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);
        
        // Handle player stats updates for leaderboard
        if (propertiesThatChanged.ContainsKey(PLAYER_STATS_PROP_KEY) && 
            leaderboardPanel != null && 
            leaderboardPanel.activeSelf) {
            ShowFinalLeaderboard();
        }
        
        // Handle reconnection updates
        if (isReconnecting && PhotonNetwork.InRoom) {
            // Make sure we have the latest room properties after reconnecting
            UpdateCachedRoomInfo(PhotonNetwork.CurrentRoom.Name, PhotonNetwork.CurrentRoom.CustomProperties);
        }
        
        // Update room list display
        if (PhotonNetwork.InRoom) {
            string roomName = PhotonNetwork.CurrentRoom.Name;
            if (cachedRoomList.ContainsKey(roomName)) {
                // Update only the properties that changed
                RoomInfo currentRoomInfo = cachedRoomList[roomName];
                if (currentRoomInfo != null) {
                    // Update the cached room info with new properties
                    UpdateCachedRoomInfo(roomName, propertiesThatChanged);
                    UpdateRoomListDisplay();
                }
            }
        }
    }

    // Add method to reset game timer
    public void ResetGameTimer() {
        if (PhotonNetwork.IsMasterClient) {
            currentGameTime = timeOptions[1];
            isGameActive = true;
            photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);
        }
    }

    // Add method to pause/resume timer
    public void SetGameActive(bool active) {
        if (PhotonNetwork.IsMasterClient) {
            isGameActive = active;
            photonView.RPC("SyncGameState", RpcTarget.All, active);
        }
    }

    [PunRPC]
    void SyncGameState(bool active) {
        isGameActive = active;
    }

    public void ReturnToLobby() {
        // Clean up before leaving
        if (leaderboardPanel != null) {
            leaderboardPanel.SetActive(false);
        }
        
        // Make sure we're not in a room before loading the new scene
        if (PhotonNetwork.IsConnected) {
            PhotonNetwork.LeaveRoom();
        }
        
        // Reload the current scene
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // Add method to safely set UI text
    private void SafeSetText(Text textComponent, string message) {
        if (textComponent != null) {
            textComponent.text = message;
        }
    }

    // Add method to check if UI is valid
    private bool IsUIValid() {
        return connectionText != null && 
               scoreText != null && 
               killsText != null && 
               timerText != null && 
               leaderboardPanel != null && 
               leaderboardContent != null;
    }

    // Add OnDestroy to clean up
    void OnDestroy() {
        // Clean up references
        connectionText = null;
        scoreText = null;
        killsText = null;
        timerText = null;
        leaderboardPanel = null;
        leaderboardContent = null;

        if (joinButton != null)
        {
            joinButton.onClick.RemoveListener(JoinRoom);
        }
    }

    // Add this helper method to update cached room info
    private void UpdateCachedRoomInfo(string roomName, ExitGames.Client.Photon.Hashtable properties) {
        if (cachedRoomList.TryGetValue(roomName, out RoomInfo roomInfo)) {
            // Update only the properties that changed
            foreach (DictionaryEntry entry in properties) {
                if (roomInfo.CustomProperties.ContainsKey(entry.Key)) {
                    roomInfo.CustomProperties[entry.Key] = entry.Value;
                } else {
                    roomInfo.CustomProperties.Add(entry.Key, entry.Value);
                }
            }
        }
    }

    [PunRPC]
    private void AddKill_RPC(string killerName)
    {
        // This method now only handles player kills
        if (!playerStats.ContainsKey(killerName))
        {
            playerStats[killerName] = new PlayerStats();
        }
        
        // Update kill streak and calculate score
        killStreaks[killerName]++;
        int scoreToAdd = CalculateKillScore(killStreaks[killerName]);
        
        // Update killer's stats
        playerStats[killerName].Kills++;
        int currentScore = playerStats[killerName].Score + scoreToAdd;
        playerStats[killerName].Score = currentScore;
        
        // Add kill streak notification to chat
        string notification = GetKillStreakNotification(killStreaks[killerName]);
        if (!string.IsNullOrEmpty(notification))
        {
            AddMessage($"{killerName} - {notification}!");
        }
        
        // Update UI if this is the killer's client
        if (killerName == PhotonNetwork.LocalPlayer.NickName)
        {
            UpdateUIStats(currentScore, playerStats[killerName].Kills);
        }
    }

    private int CalculateKillScore(int killStreak) {
        switch (killStreak) {
            case 1:
                return 10;  // First kill
            case 2:
                return 15;  // Double kill
            case 3:
                return 25;  // Triple kill
            case 4:
                return 40;  // Killing spree
            default:
                return 60;  // God like
        }
    }

    private string GetKillStreakNotification(int killStreak) {
        switch (killStreak) {
            case 2:
                return "Double Kill";
            case 3:
                return "Triple Kill";
            case 4:
                return "Killing Spree";
            case 5:
                return "God Like";
            default:
                return null;
        }
    }

    // Add this method to reset kill streak when a player dies
    public void ResetKillStreak(string playerName) {
        if (killStreaks.ContainsKey(playerName)) {
            killStreaks[playerName] = 0;
        }
    }

    public override void OnCreatedRoom() {
        Debug.Log($"Room created successfully: {PhotonNetwork.CurrentRoom.Name}");
        // The room list will automatically update for all clients in the lobby
    }

    public override void OnCreateRoomFailed(short returnCode, string message) {
        Debug.LogError($"Failed to create room: {message}");
        connectionText.text = $"Room creation failed: {message}";
        serverWindow.SetActive(true);
    }

    public override void OnJoinRoomFailed(short returnCode, string message) {
        Debug.LogError($"Failed to join room: {message}");
        
        if (isReconnecting && wasInRoom) {
            // If rejoining failed, clear the stored room info and join the lobby
            lastRoomName = null;
            wasInRoom = false;
            PhotonNetwork.JoinLobby(TypedLobby.Default);
        }
        
        connectionText.text = $"Failed to join room: {message}";
        serverWindow.SetActive(true);
    }

    // Add these new callbacks to track player join/leave events
    public override void OnPlayerEnteredRoom(Player newPlayer) {
        // Create backup before any changes
        if (PhotonNetwork.IsMasterClient)
        {
            CreateGameStateBackup();
        }
        
        if (PhotonNetwork.IsMasterClient) {
            Debug.Log($"Player {newPlayer.NickName} joined - Updating NPC count");
            
            // Update NPC count immediately when a new player joins
            UpdateNPCCount();
            
            AddMessage("Player " + newPlayer.NickName + " Joined Game.");
            
            // Sync current game state to the new player
            photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);
            photonView.RPC("SyncGameState", RpcTarget.All, isGameActive);
            
            // Send current player stats to the new player
            foreach (var kvp in playerStats)
            {
                photonView.RPC("UpdatePlayerStats_RPC", RpcTarget.All, 
                    kvp.Key, kvp.Value.Score, kvp.Value.Kills);
            }
        }

        if (PhotonNetwork.IsMasterClient) {
            Debug.Log($"[PLAYER JOIN] Player {newPlayer.NickName} joined - Forcing NPC reset");
            ForceResetNPCs();
            AddMessage("Player " + newPlayer.NickName + " Joined Game.");
        }
    }

    [PunRPC]
    private void UpdateRoomPlayerCount(string roomName, int playerCount) {
        if (roomPlayerCounts.ContainsKey(roomName)) {
            roomPlayerCounts[roomName] = playerCount;
            UpdateRoomListDisplay();
        }
    }

    // Add this method to handle reconnection attempts
    private IEnumerator TryReconnect() {
        isReconnecting = true;
        currentReconnectAttempts = 0;

        while (!PhotonNetwork.IsConnected && currentReconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
            Debug.Log($"Attempting to reconnect... Attempt {currentReconnectAttempts + 1}/{MAX_RECONNECT_ATTEMPTS}");
            connectionText.text = $"Reconnecting... Attempt {currentReconnectAttempts + 1}";

            // Try to reconnect
            PhotonNetwork.ConnectUsingSettings();
            currentReconnectAttempts++;

            // Wait for the reconnection interval
            yield return new WaitForSeconds(RECONNECT_INTERVAL);
        }

        if (!PhotonNetwork.IsConnected) {
            Debug.LogError("Failed to reconnect after maximum attempts");
            connectionText.text = "Failed to reconnect. Please restart the game.";
        }

        isReconnecting = false;
    }

    // Add method to restore player state after reconnection
    private void RestorePlayerState() {
        if (player != null) {
            // Restore player position, health, etc.
            PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth != null) {
                playerHealth.enabled = true;
            }

            PlayerNetworkMover playerMover = player.GetComponent<PlayerNetworkMover>();
            if (playerMover != null) {
                playerMover.enabled = true;
            }
        }

        // Sync game state
        if (PhotonNetwork.IsMasterClient) {
            photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);
            photonView.RPC("SyncGameState", RpcTarget.All, isGameActive);
        }
    }

    // Add OnApplicationPause and OnApplicationFocus handlers
    void OnApplicationPause(bool isPaused) {
        if (!isPaused) {
            // Application resumed
            if (!PhotonNetwork.IsConnected && !isReconnecting) {
                StartCoroutine(TryReconnect());
            }
        }
    }

    void OnApplicationFocus(bool hasFocus) {
        if (hasFocus) {
            // Application gained focus
            if (!PhotonNetwork.IsConnected && !isReconnecting) {
                StartCoroutine(TryReconnect());
            }
        }
    }

    public GameObject SpawnNPC()
    {
        if (!PhotonNetwork.IsMasterClient) return null;

        // SAFETY CHECK - Don't spawn if we already have max NPCs
        int currentNPCs = GameObject.FindGameObjectsWithTag("NPC").Length;
        int maxAllowed = 6 - PhotonNetwork.CurrentRoom.PlayerCount;
        
        if (currentNPCs >= maxAllowed) {
            Debug.Log($"[SPAWN BLOCKED] Already have {currentNPCs} NPCs, maximum allowed is {maxAllowed}");
            return null;
        }

        Debug.Log("SpawnNPC called by MasterClient");
        
        // Choose a random spawn point
        int spawnIndex = Random.Range(0, spawnPoints.Length);
        Vector3 spawnPosition = spawnPoints[spawnIndex].position;
        Quaternion spawnRotation = spawnPoints[spawnIndex].rotation;

        // Verify spawn point is on NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(spawnPosition, out hit, 1.0f, NavMesh.AllAreas))
        {
            spawnPosition = hit.position;
        }
        
        // Check that we have the NPC prefab
        if (npcPrefab == null)
        {
            Debug.LogError("NPC Prefab is null! Cannot spawn NPC.");
            return null;
        }
        
        try
        {
            // Instantiate the NPC with explicit ownership
            GameObject npc = PhotonNetwork.Instantiate(npcPrefab.name, spawnPosition, spawnRotation, 0);
            
            if (npc != null)
            {
                PhotonView pv = npc.GetComponent<PhotonView>();
                Debug.Log($"NPC spawned with PhotonView ID: {pv.ViewID} at position: {spawnPosition}");
                
                // Ensure the master client owns this NPC
                if (pv.Owner != PhotonNetwork.LocalPlayer)
                {
                    pv.TransferOwnership(PhotonNetwork.LocalPlayer);
                }
                
                SetupNPC(npc);
                
                // Add to our tracking list
                if (!activeNPCs.Contains(npc))
                {
                    activeNPCs.Add(npc);
                }
                
                return npc;
            }
            else
            {
                Debug.LogError("Failed to instantiate NPC prefab!");
                return null;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error spawning NPC: {e.Message}");
            return null;
        }
    }

    private void SetupNPC(GameObject npc)
    {
        if (npc == null) return;
        
        // Set proper scale (1.5x is larger than player)
        npc.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        
        // Set up NPC-specific components
        npc.tag = "NPC";
        npc.layer = LayerMask.NameToLayer("Shootable");
        
        // Get PhotonView
        PhotonView photonView = npc.GetComponent<PhotonView>();
        if (photonView == null)
        {
            Debug.LogError("NPC is missing PhotonView component!");
            return;
        }
        
        // Add unique name to help with debugging
        npc.name = $"NPC_{photonView.ViewID}";
        
        // Setup components
        NPCController npcController = npc.GetComponent<NPCController>();
        if (npcController == null)
        {
            npcController = npc.AddComponent<NPCController>();
        }
        
        // Ensure animator component is active
        Animator animator = npc.GetComponent<Animator>();
        if (animator == null)
        {
            animator = npc.GetComponentInChildren<Animator>();
        }
        
        // If still no animator found, log error
        if (animator == null)
        {
            Debug.LogError($"NPC {npc.name} has no Animator component!");
        }
        else
        {
            // Make sure animator is enabled and reset
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
            Debug.Log($"NPC {npc.name} animator initialized");
        }
        
        // Configure NavMeshAgent with unique settings to avoid stacking
        NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.height = 1.8f;
            agent.radius = 0.5f;
            agent.baseOffset = 0f;
            agent.speed = Random.Range(3.0f, 4.0f); // Slightly different speeds
            agent.acceleration = 12f;
            agent.angularSpeed = 180f;
            agent.stoppingDistance = 1f;
            agent.avoidancePriority = Random.Range(20, 80); // Different priorities
            
            // Ensure agent is on NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(npc.transform.position, out hit, 1.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }

        // Configure collider to match player size
        CapsuleCollider collider = npc.GetComponent<CapsuleCollider>();
        if (collider != null)
        {
            collider.height = 1.8f;
            collider.radius = 0.5f;
            collider.center = new Vector3(0, 0.9f, 0);
        }
        
        // Setup health component
        NPCHealth npcHealth = npc.GetComponent<NPCHealth>();
        if (npcHealth == null)
        {
            npcHealth = npc.AddComponent<NPCHealth>();
        }

        // Force the NPC to initialize
        npcController.InitializeNPC();
        
        // Force initialize on all clients
        photonView.RPC("InitializeRemoteNPC", RpcTarget.Others);
    }

    [PunRPC]
    private void InitializeRemoteNPC()
    {
        // This is called on client side to ensure the NPC is properly initialized
        NPCController controller = GetComponent<NPCController>();
        if (controller != null)
        {
            controller.InitializeNPC();
        }
        
        // Set proper scale
        transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        
        // Get the animator component
        Animator animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        
        // Check if animator exists before using it
        if (animator != null)
        {
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
            Debug.Log($"Remote NPC {photonView.ViewID} animator initialized");
        }
    }

    private void VerifyNPCSetup(GameObject npc)
    {
        if (npc == null)
        {
            Debug.LogError("NPC object is null!");
            return;
        }

        // Check essential components
        var health = npc.GetComponent<NPCHealth>();
        if (health == null) Debug.LogError("NPCHealth component missing!");

        var photonView = npc.GetComponent<PhotonView>();
        if (photonView == null) Debug.LogError("PhotonView component missing!");

        var animator = npc.GetComponent<Animator>();
        if (animator == null)
        {
            animator = npc.GetComponentInChildren<Animator>();
            if (animator == null) Debug.LogError("Animator component missing!");
        }

        var npcController = npc.GetComponent<NPCController>();
        if (npcController == null) Debug.LogError("NPCController component missing!");

        var agent = npc.GetComponent<NavMeshAgent>();
        if (agent == null) Debug.LogError("NavMeshAgent component missing!");

        // Verify layer
        if (npc.layer != LayerMask.NameToLayer("Shootable"))
            Debug.LogError("NPC not in Shootable layer!");

        // Verify tag
        if (npc.tag != "NPC")
            Debug.LogError("NPC tag not set correctly!");
    }

    private void MaintainNPCCount()
    {
        if (!PhotonNetwork.IsMasterClient || !isGameActive) return;

        // Only run this if the game is active
        GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
        int currentNPCCount = npcs.Length;

        // If we have too many NPCs, clean them up
        if (currentNPCCount > TARGET_TOTAL_PLAYERS)
        {
            CleanupAndMaintainNPCs();
            return;
        }

        // Only spawn if we have less than TARGET_TOTAL_PLAYERS
        if (currentNPCCount < TARGET_TOTAL_PLAYERS)
        {
            SpawnNPC();
            Debug.Log($"Maintaining NPC count: Spawned new NPC. Count: {currentNPCCount + 1}/{TARGET_TOTAL_PLAYERS}");
        }
    }

    private IEnumerator SpawnInitialNPCs()
    {
        yield return new WaitForSeconds(1f); // Wait for room to fully initialize
        
        // Add a message that bots are joining the battle
        AddMessage("Bots are joining the battle!");
        
        // Calculate initial NPC count based on current player count
        int requiredNPCs = CalculateRequiredNPCs();
        
        // Spawn exactly the required number of NPCs
        for (int i = 0; i < requiredNPCs; i++)
        {
            SpawnNPC();
            yield return new WaitForSeconds(0.5f); // Small delay between spawns
        }
    }

    public void RequestNPCRespawn(Vector3 deathPosition)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        // Wait a short delay before spawning a new NPC
        StartCoroutine(DelayedRespawn());
    }

    private IEnumerator DelayedRespawn()
    {
        yield return new WaitForSeconds(2f); // Wait 2 seconds before respawning
        
        GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
        int activeCount = npcs.Count(npc => {
            NPCHealth health = npc.GetComponent<NPCHealth>();
            return health != null && !health.IsDead();
        });

        int requiredNPCs = CalculateRequiredNPCs();
        if (activeCount < requiredNPCs)
        {
            SpawnNPC();
        }
    }

    public void SetupNPCPrefab()
    {
        // Add this method to your NetworkManager to verify NPC prefab setup
        GameObject npcPrefab = Resources.Load<GameObject>("NPCPrefab");
        if (npcPrefab != null)
        {
            // Check components
            NPCHealth health = npcPrefab.GetComponent<NPCHealth>();
            if (health == null)
            {
                Debug.LogError("NPCPrefab missing NPCHealth component!");
            }

            PhotonView photonView = npcPrefab.GetComponent<PhotonView>();
            if (photonView == null)
            {
                Debug.LogError("NPCPrefab missing PhotonView component!");
            }
            else
            {
                // Verify PhotonView settings
                photonView.ObservedComponents = new List<Component> { health };
                photonView.Synchronization = ViewSynchronization.UnreliableOnChange;
            }

            Animator animator = npcPrefab.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError("NPCPrefab missing Animator component!");
            }
            else
            {
                // Verify animator parameters
                bool hasIsHurt = false;
                bool hasIsDead = false;
                foreach (AnimatorControllerParameter param in animator.parameters)
                {
                    if (param.name == "IsHurt") hasIsHurt = true;
                    if (param.name == "IsDead") hasIsDead = true;
                }
                if (!hasIsHurt) Debug.LogError("Animator missing IsHurt parameter!");
                if (!hasIsDead) Debug.LogError("Animator missing IsDead parameter!");
            }
        }
        else
        {
            Debug.LogError("NPCPrefab not found in Resources folder!");
        }
    }

    // Add new method for bot kills
    [PunRPC]
    private void AddBotKill_RPC(string killerName, string botName)
    {
        // Add logging to track when this is called
        Debug.Log($"AddBotKill_RPC called - Killer: {killerName}, Bot: {botName}, IsMasterClient: {PhotonNetwork.IsMasterClient}");

        // Ensure this bot hasn't already been counted
        string killKey = $"{botName}_{killerName}";
        if (!processedKills.Contains(killKey))
        {
            processedKills.Add(killKey);
            
            if (!playerStats.ContainsKey(killerName))
            {
                playerStats[killerName] = new PlayerStats();
            }
            
            // Update kill streak and calculate score
            killStreaks[killerName]++;
            int scoreToAdd = CalculateBotKillScore(killStreaks[killerName]);
            
            // Update killer's stats
            playerStats[killerName].Kills++;
            int currentScore = playerStats[killerName].Score + scoreToAdd;
            playerStats[killerName].Score = currentScore;
            
            // Track bot kills separately
            if (!botKills.ContainsKey(killerName))
            {
                botKills[killerName] = 0;
            }
            botKills[killerName]++;
            
            // Add kill message to chat
            AddMessage($"{killerName} eliminated {botName}!");
            
            // Add kill streak notification to chat
            string notification = GetKillStreakNotification(killStreaks[killerName]);
            if (!string.IsNullOrEmpty(notification))
            {
                AddMessage($"{killerName} - {notification}!");
            }
            
            // Update UI if this is the killer's client
            if (killerName == PhotonNetwork.LocalPlayer.NickName)
            {
                UpdateUIStats(currentScore, playerStats[killerName].Kills);
            }
        }
        else
        {
            Debug.Log($"Kill already processed for {killKey}");
        }
    }

    // Add method to calculate bot kill score (you can adjust the values)
    private int CalculateBotKillScore(int killStreak)
    {
        // Bots might be worth less points than player kills
        switch (killStreak)
        {
            case 1:
                return 5;  // First bot kill
            case 2:
                return 8;  // Double kill
            case 3:
                return 12; // Triple kill
            case 4:
                return 20; // Killing spree
            default:
                return 30; // God like
        }
    }

    // Add method to clear processed kills (call this when starting a new game or as needed)
    public void ClearProcessedKills()
    {
        processedKills.Clear();
    }

    [PunRPC]
    public void RequestBotRespawnRPC(Vector3 deathPosition)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        Debug.Log($"Master client received bot respawn request");
        
        if (isGameActive)
        {
            CleanupAndMaintainNPCs();
        }
    }

    private IEnumerator NPCMaintenanceRoutine()
    {
        while (true)
        {
            if (PhotonNetwork.IsMasterClient && isGameActive)
            {
                CleanupAndMaintainNPCs();
            }
            yield return new WaitForSeconds(npcCleanupInterval);
        }
    }

    private void CleanupAndMaintainNPCs()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // Clean up our tracking lists first
        activeNPCs.RemoveAll(npc => npc == null);
        deadNPCs.RemoveAll(npc => npc == null);

        // Find all NPCs in the scene
        GameObject[] allNPCs = GameObject.FindGameObjectsWithTag("NPC");
        
        // Clear our lists before rebuilding them
        activeNPCs.Clear();
        deadNPCs.Clear();
        
        // Update our active and dead NPC lists
        foreach (GameObject npc in allNPCs)
        {
            NPCHealth health = npc.GetComponent<NPCHealth>();
            if (health != null)
            {
                if (health.IsDead())
                {
                    deadNPCs.Add(npc);
                    PhotonNetwork.Destroy(npc);
                }
                else
                {
                    activeNPCs.Add(npc);
                }
            }
        }

        Debug.Log($"NPC Status - Active: {activeNPCs.Count}, Dead: {deadNPCs.Count}, Total: {allNPCs.Length}");

        // Calculate required NPCs based on current player count
        int requiredNPCs = CalculateRequiredNPCs();
        
        // If we have too many active NPCs, destroy the excess ones
        while (activeNPCs.Count > requiredNPCs)
        {
            if (activeNPCs.Count > 0)
            {
                GameObject npcToRemove = activeNPCs[activeNPCs.Count - 1];
                activeNPCs.RemoveAt(activeNPCs.Count - 1);
                if (npcToRemove != null)
                {
                    PhotonNetwork.Destroy(npcToRemove);
                }
            }
        }

        // Spawn new NPCs if needed
        int npcsNeeded = requiredNPCs - activeNPCs.Count;
        if (npcsNeeded > 0)
        {
            StartCoroutine(SpawnAdditionalNPCs(npcsNeeded));
        }
    }

    public void ReloadScene()
    {
        // Keep the camera active until the scene actually reloads
        if (sceneCamera != null)
        {
            sceneCamera.enabled = true;
        }

        // Disable player controls but keep visuals
        if (player != null)
        {
            PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.enabled = false;
            }

            PlayerNetworkMover playerMover = player.GetComponent<PlayerNetworkMover>();
            if (playerMover != null)
            {
                playerMover.enabled = false;
            }
        }

        // Clean up current game state
        isGameActive = false;
        ClearProcessedKills();
        
        // If host, clean up NPCs and notify others
        if (PhotonNetwork.IsMasterClient)
        {
            // Destroy all NPCs before reloading
            GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
            foreach (GameObject npc in npcs)
            {
                if (npc != null)
                {
                    PhotonNetwork.Destroy(npc);
                }
            }
            photonView.RPC("ClientLeaveGame", RpcTarget.All);
        }

        // Start the reload process for everyone
        StartCoroutine(SmoothReloadCoroutine());
    }

    [PunRPC]
    private void ClientLeaveGame()
    {
        // This will be called on all clients when the host initiates reload
        StartCoroutine(SmoothReloadCoroutine());
    }

    private IEnumerator SmoothReloadCoroutine()
    {
        // Wait a short moment to ensure everything is disabled properly
        yield return new WaitForSeconds(0.1f);

        if (PhotonNetwork.IsConnected) {
            // Leave the room first
            PhotonNetwork.LeaveRoom();
        }

        // Load the scene
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        
        // Reset UI elements
        if (serverWindow != null) {
            serverWindow.SetActive(true);
        }
        
        if (connectionText != null) {
            connectionText.text = "Connecting to lobby...";
        }

        // Start the reconnection process
        StartCoroutine(ReconnectAfterReload());
    }

    private IEnumerator ReconnectAfterReload() {
        yield return new WaitForSeconds(1f); // Wait a moment for the scene to load
        
        // Reconnect to Photon
        if (!PhotonNetwork.IsConnected) {
            PhotonNetwork.ConnectUsingSettings();
            
            // Wait for connection
            while (!PhotonNetwork.IsConnected) {
                yield return new WaitForSeconds(0.5f);
            }
            
            // Join the lobby once connected
            PhotonNetwork.JoinLobby();
        }
        
        // Reset UI
        if (serverWindow != null) {
            serverWindow.SetActive(true);
        }
        if (connectionText != null) {
            connectionText.text = "";
        }

        // Make sure cursor is visible and unlocked
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    // Add this method to calculate required NPCs
    private int CalculateRequiredNPCs() {
        int realPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;
        // Make sure we never exceed TARGET_TOTAL_PLAYERS
        return Mathf.Min(
            Mathf.Max(0, TARGET_TOTAL_PLAYERS - realPlayerCount),
            TARGET_TOTAL_PLAYERS - realPlayerCount
        );
    }

    // Add this method to handle NPC count updates
    private void UpdateNPCCount() {
        if (!PhotonNetwork.IsMasterClient) return;

        // HARD CHECK - Get exact count
        GameObject[] allNPCs = GameObject.FindGameObjectsWithTag("NPC");
        int realPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;
        int maxAllowedNPCs = 6 - realPlayerCount; // This is the MAXIMUM allowed
        
        Debug.Log($"[CRITICAL NPC CHECK] Players: {realPlayerCount}, Current NPCs: {allNPCs.Length}, Max Allowed: {maxAllowedNPCs}");

        // IMMEDIATE CLEANUP if we have too many
        if (allNPCs.Length > maxAllowedNPCs) {
            Debug.Log($"[EMERGENCY CLEANUP] Found {allNPCs.Length} NPCs but only allowed {maxAllowedNPCs}! FORCING CLEANUP!");
            
            // Destroy ALL NPCs and respawn correct amount
            foreach (GameObject npc in allNPCs) {
                if (npc != null) {
                    Debug.Log($"[EMERGENCY CLEANUP] Destroying: {npc.name}");
                    PhotonNetwork.Destroy(npc);
                }
            }
            
            // Wait a frame then spawn correct amount
            StartCoroutine(SpawnCorrectNPCCount(maxAllowedNPCs));
        }

        // Update room properties with correct counts
        ExitGames.Client.Photon.Hashtable roomProps = new ExitGames.Client.Photon.Hashtable();
        roomProps["RealPlayerCount"] = realPlayerCount;
        roomProps["NPCCount"] = maxAllowedNPCs;
        roomProps["TotalPlayers"] = realPlayerCount + maxAllowedNPCs;
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);
    }

    private IEnumerator SpawnCorrectNPCCount(int count) {
        yield return new WaitForSeconds(1f); // Wait for cleanup
        
        Debug.Log($"[NPC RESPAWN] Spawning exactly {count} NPCs");
        for (int i = 0; i < count; i++) {
            SpawnNPC();
            yield return new WaitForSeconds(0.5f);
        }
    }

    // Add this method to force cleanup all NPCs and respawn the correct amount
    public void ForceResetNPCs() {
        if (!PhotonNetwork.IsMasterClient) return;

        Debug.Log("[FORCE RESET] Starting force reset of all NPCs");
        
        // First, destroy all existing NPCs
        GameObject[] allNPCs = GameObject.FindGameObjectsWithTag("NPC");
        foreach (GameObject npc in allNPCs) {
            if (npc != null) {
                Debug.Log($"[FORCE RESET] Destroying NPC: {npc.name}");
                PhotonNetwork.Destroy(npc);
            }
        }

        // Wait a frame to ensure all NPCs are destroyed
        StartCoroutine(DelayedNPCRespawn());
    }

    private IEnumerator DelayedNPCRespawn() {
        yield return new WaitForSeconds(1f); // Wait a second to ensure cleanup
        UpdateNPCCount(); // This will spawn the correct number of NPCs
    }

    private void RemoveExcessNPCs(int count) {
        GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
        Debug.Log($"[NPC Removal] Found {npcs.Length} NPCs, removing {count}");
        
        int removed = 0;
        foreach (GameObject npc in npcs) {
            if (removed >= count) break;
            
            if (npc != null) {
                Debug.Log($"[NPC Removal] Destroying NPC {npc.name}");
                PhotonNetwork.Destroy(npc);
                removed++;
            }
        }
        Debug.Log($"[NPC Removal] Successfully removed {removed} NPCs");
    }

    private IEnumerator SpawnAdditionalNPCs(int count) {
        Debug.Log($"[NPC Spawn] Starting to spawn {count} NPCs");
        for (int i = 0; i < count; i++) {
            GameObject npc = SpawnNPC();
            if (npc != null) {
                Debug.Log($"[NPC Spawn] Successfully spawned NPC {i + 1}/{count}");
            }
            yield return new WaitForSeconds(0.5f); // Small delay between spawns
        }
        Debug.Log("[NPC Spawn] Finished spawning NPCs");
    }

    // Add these new RPCs for better bot synchronization
    [PunRPC]
    private void SyncBotState_RPC(int botCount, int requiredNPCs)
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            // Non-master clients should update their local state
            UpdateLocalBotState(botCount, requiredNPCs);
        }
    }

    private void UpdateLocalBotState(int botCount, int requiredNPCs)
    {
        // Update room properties locally
        ExitGames.Client.Photon.Hashtable roomProps = new ExitGames.Client.Photon.Hashtable();
        int realPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;
        roomProps["RealPlayerCount"] = realPlayerCount;
        roomProps["NPCCount"] = botCount;
        roomProps["TotalPlayers"] = realPlayerCount + botCount;
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);
    }

    // Add this method to check if the user can edit the room name
    public bool CanEditRoomName()
    {
        return isWalletConnected;
    }

    // Add this method to check if the user can edit the time selection
    public bool CanEditTimeSelection()
    {
        return isWalletConnected;
    }

    // Add this helper method to format wallet address
    private string FormatWalletAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || address.Length < 10)
            return address;

        return $"{address.Substring(0, 6)}...{address.Substring(address.Length - 4)}";
    }

    // Add this class with the other serializable classes at the bottom of the file
    [System.Serializable]
    public class StakingUpdateRequest
    {
        public string walletAddress;
        public bool isStaked;
    }

    // Then modify the UpdateStakingStatus method like this:
    private IEnumerator UpdateStakingStatus(string walletAddress, bool isStaked)
    {
        Debug.Log($"[Staking Update] Starting staking status update for wallet: {walletAddress}, setting isStaked to: {isStaked}");

        string url = "https://starkshoot-server.vercel.app/api/stake";
        Debug.Log($"[Staking Update] API URL: {url}");

        // Create and populate the request object
        var requestData = new StakingUpdateRequest
        {
            walletAddress = walletAddress,
            isStaked = isStaked
        };

        // Serialize using JsonUtility
        string jsonData = JsonUtility.ToJson(requestData);
        Debug.Log($"[Staking Update] Request payload: {jsonData}");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            Debug.Log("[Staking Update] Configuring web request...");
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            Debug.Log("[Staking Update] Sending request...");
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string responseText = www.downloadHandler.text;
                Debug.Log($"[Staking Update] Success! Response: {responseText}");
                Debug.Log($"[Staking Update] Successfully updated staking status for wallet: {walletAddress} to {isStaked}");
            }
            else
            {
                Debug.LogError($"[Staking Update] Error updating staking status. Error: {www.error}");
                Debug.LogError($"[Staking Update] Response Code: {www.responseCode}");
                Debug.LogError($"[Staking Update] Full Response: {www.downloadHandler?.text}");
            }
        }
    }

    private void UpdateFieldsInteractability(bool canEdit)
    {
        // Allow editing of username only in debug mode
        if (username != null)
        {
            username.interactable = testDebugMode && canEdit;
        }

        // Room name should only be editable if not set by API
        if (roomName != null)
        {
            roomName.interactable = !isRoomNameFromAPI && isWalletConnected && canEdit;
        }

        // Time selection should only be editable if not set by API
        if (timeSelectionDropdown != null)
        {
            timeSelectionDropdown.interactable = !isTimerFromAPI && isWalletConnected && canEdit;
        }
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        Debug.Log($"Master client switched from previous to {newMasterClient.NickName}");
        
        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log("I am now the new master client - taking over responsibilities");
            isMasterClientSwitching = true;
            
            // Restore game state from backup
            StartCoroutine(RestoreMasterClientState());
        }
        else
        {
            Debug.Log($"New master client is {newMasterClient.NickName}");
        }
    }

    private IEnumerator RestoreMasterClientState()
    {
        yield return new WaitForSeconds(1f); // Wait for network stabilization
        
        Debug.Log("Restoring master client state...");
        
        // Restore game timer and state
        if (backupGameActive && backupGameTime > 0)
        {
            currentGameTime = backupGameTime;
            isGameActive = backupGameActive;
            photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);
            photonView.RPC("SyncGameState", RpcTarget.All, isGameActive);
            Debug.Log($"Restored game timer: {currentGameTime}, active: {isGameActive}");
        }
        
        // Restore player statistics
        foreach (var kvp in backupPlayerStats)
        {
            playerStats[kvp.Key] = kvp.Value;
            photonView.RPC("UpdatePlayerStats_RPC", RpcTarget.All, 
                kvp.Key, kvp.Value.Score, kvp.Value.Kills);
        }
        
        // Restore kill streaks
        foreach (var kvp in backupKillStreaks)
        {
            killStreaks[kvp.Key] = kvp.Value;
        }
        
        // Update room properties to reflect current state
        UpdateRoomPropertiesAsNewMaster();
        
        // Handle NPC management
        yield return StartCoroutine(RestoreNPCState());
        
        isMasterClientSwitching = false;
        Debug.Log("Master client state restoration completed");
    }

    private void UpdateRoomPropertiesAsNewMaster()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        try
        {
            ExitGames.Client.Photon.Hashtable roomProps = new ExitGames.Client.Photon.Hashtable();
            
            // Update basic room info
            roomProps["RealPlayerCount"] = PhotonNetwork.CurrentRoom.PlayerCount;
            roomProps["GameState"] = isGameActive ? "InProgress" : "Waiting";
            roomProps["GameTime"] = currentGameTime;
            
            // Update player stats in room properties
            ExitGames.Client.Photon.Hashtable statsData = new ExitGames.Client.Photon.Hashtable();
            foreach (var kvp in playerStats)
            {
                ExitGames.Client.Photon.Hashtable playerData = new ExitGames.Client.Photon.Hashtable()
                {
                    {"Score", kvp.Value.Score},
                    {"Kills", kvp.Value.Kills}
                };
                statsData[kvp.Key] = playerData;
            }
            roomProps[PLAYER_STATS_PROP_KEY] = statsData;
            
            PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);
            Debug.Log("Updated room properties as new master client");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error updating room properties as new master: {e.Message}");
        }
    }

    private IEnumerator RestoreNPCState()
    {
        // Clean up any orphaned NPCs first
        GameObject[] existingNPCs = GameObject.FindGameObjectsWithTag("NPC");
        List<GameObject> validNPCs = new List<GameObject>();
        
        foreach (GameObject npc in existingNPCs)
        {
            PhotonView npcPV = npc.GetComponent<PhotonView>();
            if (npcPV != null && npcPV.Owner == null)
            {
                // This NPC's owner disconnected, take ownership
                npcPV.TransferOwnership(PhotonNetwork.LocalPlayer);
                validNPCs.Add(npc);
                Debug.Log($"Took ownership of orphaned NPC: {npc.name}");
            }
            else if (npcPV != null)
            {
                validNPCs.Add(npc);
            }
            else
            {
                // NPC without PhotonView, destroy it
                Destroy(npc);
            }
        }
        
        yield return new WaitForSeconds(0.5f);
        
        // Ensure we have the correct number of NPCs
        int requiredNPCs = CalculateRequiredNPCs();
        int currentValidNPCs = validNPCs.Count;
        
        if (currentValidNPCs < requiredNPCs)
        {
            int npcsToSpawn = requiredNPCs - currentValidNPCs;
            Debug.Log($"Spawning {npcsToSpawn} NPCs to maintain correct count");
            
            for (int i = 0; i < npcsToSpawn; i++)
            {
                SpawnNPC();
                yield return new WaitForSeconds(0.5f);
            }
        }
        else if (currentValidNPCs > requiredNPCs)
        {
            int npcsToRemove = currentValidNPCs - requiredNPCs;
            Debug.Log($"Removing {npcsToRemove} excess NPCs");
            
            for (int i = 0; i < npcsToRemove && i < validNPCs.Count; i++)
            {
                if (validNPCs[i] != null)
                {
                    PhotonNetwork.Destroy(validNPCs[i]);
                }
            }
        }
        
        // Update NPC count in room properties
        UpdateNPCCount();
    }

    private void CreateGameStateBackup()
    {
        // Backup player stats
        backupPlayerStats.Clear();
        foreach (var kvp in playerStats)
        {
            backupPlayerStats[kvp.Key] = new PlayerStats
            {
                Score = kvp.Value.Score,
                Kills = kvp.Value.Kills
            };
        }
        
        // Backup kill streaks
        backupKillStreaks.Clear();
        foreach (var kvp in killStreaks)
        {
            backupKillStreaks[kvp.Key] = kvp.Value;
        }
        
        // Backup game state
        backupGameTime = currentGameTime;
        backupGameActive = isGameActive;
        
        // Backup NPC positions
        backupNPCPositions.Clear();
        GameObject[] npcs = GameObject.FindGameObjectsWithTag("NPC");
        foreach (GameObject npc in npcs)
        {
            if (npc != null)
            {
                backupNPCPositions.Add(npc.transform.position);
            }
        }
    }
}

// Add this class to parse the API response
[System.Serializable]
public class UserData
{
    public string _id;
    public string walletAddress;
    public int __v;
    public bool isStaked;
    public int kills;
    public int score;
    public string username;
    public string currentRoom;  // Added new field
    public string duration;     // Added new field
}

[System.Serializable]
public class LeaderboardEntryRequest
{
    public string walletAddress;
    public int kills;
    public int score;
    public string roomId;
    public string username;
}

[System.Serializable]
public class LeaderboardEntryResponse
{
    public string _id;
    public string walletAddress;
    public int kills;
    public int score;
    public string roomId;
    public string username;
    public string createdAt;
    public int __v;
}