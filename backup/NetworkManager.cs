using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

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
    private float[] timeOptions = { 180f, 300f, 600f }; // 3, 5, 10 minutes

    [Header("NPC Settings")]
    [SerializeField] private GameObject[] npcModels;
    [SerializeField] private int maxNPCs = 5;
    [SerializeField] private float npcSpawnDelay = 5f;
    [SerializeField] private float npcRespawnTime = 5f;

    private GameObject player;
    private Queue<string> messages;
    private const int messageCount = 10;
    private string nickNamePrefKey = "PlayerName";
    private Dictionary<string, PlayerStats> playerStats = new Dictionary<string, PlayerStats>();
    private float currentGameTime;
    private bool isGameActive = false;
    private Dictionary<string, int> killStreaks = new Dictionary<string, int>();
    private List<GameObject> activeNPCs = new List<GameObject>();
    private float nextNPCSpawnTime;

    // Add this class to track player statistics
    private class PlayerStats {
        public int Score { get; set; }
        public int Kills { get; set; }

        public PlayerStats() {
            Score = 0;
            Kills = 0;
        }
    }

    /// <summary>
    /// Start is called on the frame when a script is enabled just before
    /// any of the Update methods is called the first time.
    /// </summary>
    void Start() {
        messages = new Queue<string>(messageCount);
        if (PlayerPrefs.HasKey(nickNamePrefKey)) {
            username.text = PlayerPrefs.GetString(nickNamePrefKey);
        }
        
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.ConnectUsingSettings();
        connectionText.text = "Connecting to lobby...";
        
        // Initialize UI
        scoreText.text = "Score: 0";
        killsText.text = "Kills: 0";
        
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
        
        // Initialize player stats
        InitializePlayerStats();
        
        // Make sure UI is initialized with zero values
        if (scoreText != null) scoreText.text = "Score: 0";
        if (killsText != null) killsText.text = "Kills: 0";
        
        // Validate spawn points
        ValidateSpawnPoints();
    }

    void SetupTimeDropdown() {
        if (timeSelectionDropdown != null) {
            timeSelectionDropdown.ClearOptions();
            List<string> options = new List<string>();
            
            foreach (float time in timeOptions) {
                int minutes = Mathf.FloorToInt(time / 60f);
                options.Add($"{minutes} Minutes");
            }
            
            timeSelectionDropdown.AddOptions(options);
            timeSelectionDropdown.value = 1; // Default to second option (5 minutes)
        }
    }

    /// <summary>
    /// Called on the client when you have successfully connected to a master server.
    /// </summary>
    public override void OnConnectedToMaster() {
        PhotonNetwork.JoinLobby();
    }

    /// <summary>
    /// Called on the client when the connection was lost or you disconnected from the server.
    /// </summary>
    /// <param name="cause">DisconnectCause data associated with this disconnect.</param>
    public override void OnDisconnected(DisconnectCause cause) {
        // Add null check before accessing UI elements
        if (connectionText != null) {
            connectionText.text = cause.ToString();
        }
        
        // Reset game state
        isGameActive = false;
        
        // Show cursor in case of disconnect during game
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// Callback function on joined lobby.
    /// </summary>
    public override void OnJoinedLobby() {
        serverWindow.SetActive(true);
        connectionText.text = "";
    }

    /// <summary>
    /// Callback function on reveived room list update.
    /// </summary>
    /// <param name="rooms">List of RoomInfo.</param>
    public override void OnRoomListUpdate(List<RoomInfo> rooms) {
        roomList.text = "";
        foreach (RoomInfo room in rooms) {
            roomList.text += room.Name + "\n";
        }
    }

    /// <summary>
    /// The button click callback function for join room.
    /// </summary>
    public void JoinRoom() {
        serverWindow.SetActive(false);
        connectionText.text = "Joining room...";
        PhotonNetwork.LocalPlayer.NickName = username.text;
        PlayerPrefs.SetString(nickNamePrefKey, username.text);
        
        RoomOptions roomOptions = new RoomOptions() {
            IsVisible = true,
            MaxPlayers = 8,
            CustomRoomProperties = new ExitGames.Client.Photon.Hashtable()
            {
                {"GameTime", timeOptions[timeSelectionDropdown.value]}
            }
        };

        if (PhotonNetwork.IsConnectedAndReady) {
            PhotonNetwork.JoinOrCreateRoom(roomName.text, roomOptions, TypedLobby.Default);
        } else {
            connectionText.text = "PhotonNetwork connection is not ready, try restart it.";
        }
    }

    /// <summary>
    /// Callback function on joined room.
    /// </summary>
    public override void OnJoinedRoom() {
        connectionText.text = "";
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        // Get the game time from room properties
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("GameTime")) {
            float gameTime = (float)PhotonNetwork.CurrentRoom.CustomProperties["GameTime"];
            currentGameTime = gameTime;
        }
        
        // Start the game timer if master client
        if (PhotonNetwork.IsMasterClient) {
            isGameActive = true;
            photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);
            
            // Initialize NPC spawning
            nextNPCSpawnTime = Time.time + npcSpawnDelay;
        }
        
        Respawn(0.0f);
        
        // Initialize stats when joining room
        InitializePlayerStats();
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
    public void AddMessage(string message) {
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
        if (PhotonNetwork.IsMasterClient) {
            AddMessage("Player " + other.NickName + " Left Game.");
        }
    }

    // Add this method to handle UI updates
    private void UpdateUIStats(int score, int kills) {
        // Ensure UI updates happen on the main thread
        if (scoreText != null) {
            scoreText.text = $"Score: {score}";
            Debug.Log($"Updated score text to: {score}");
        } else {
            Debug.LogWarning("scoreText is null!");
        }
        
        if (killsText != null) {
            killsText.text = $"Kills: {kills}";
            Debug.Log($"Updated kills text to: {kills}");
        } else {
            Debug.LogWarning("killsText is null!");
        }
    }

    [PunRPC]
    private void UpdatePlayerStats_RPC(string playerName, int score, int kills) {
        Debug.Log($"UpdatePlayerStats_RPC received for {playerName}. Score: {score}, Kills: {kills}");
        
        if (!playerStats.ContainsKey(playerName)) {
            playerStats[playerName] = new PlayerStats();
        }
        
        playerStats[playerName].Score = score;
        playerStats[playerName].Kills = kills;
        
        // Update UI for the local player
        if (playerName == PhotonNetwork.LocalPlayer.NickName) {
            UpdateUIStats(score, kills);
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
        if (isGameActive && PhotonNetwork.IsMasterClient) {
            if (currentGameTime > 0) {
                currentGameTime -= Time.deltaTime;
                photonView.RPC("SyncTimer", RpcTarget.All, currentGameTime);

                if (currentGameTime <= 0) {
                    currentGameTime = 0;
                    photonView.RPC("EndGame", RpcTarget.All);
                }
            }

            // Handle NPC spawning
            if (Time.time >= nextNPCSpawnTime) {
                SpawnNPC();
                nextNPCSpawnTime = Time.time + npcSpawnDelay;
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
        
        // Disable player controls
        if (player != null) {
            PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth != null) {
                playerHealth.enabled = false;
            }
            
            PlayerNetworkMover playerMover = player.GetComponent<PlayerNetworkMover>();
            if (playerMover != null) {
                playerMover.enabled = false;
            }
        }

        // Disable all NPCs
        foreach (GameObject npc in activeNPCs) {
            if (npc != null) {
                NPCController npcController = npc.GetComponent<NPCController>();
                if (npcController != null) {
                    npcController.enabled = false;
                }
                UnityEngine.AI.NavMeshAgent agent = npc.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) {
                    agent.enabled = false;
                }
            }
        }

        // Ensure cursor is visible and can interact with UI
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Show leaderboard with slight delay to ensure UI setup
        StartCoroutine(ShowLeaderboardDelayed());
    }

    private IEnumerator ShowLeaderboardDelayed() {
        yield return new WaitForSeconds(0.1f); // Small delay to ensure proper setup
        ShowLeaderboard();
    }

    void ShowLeaderboard() {
        if (leaderboardPanel == null || leaderboardContent == null) return;

        // Clear existing entries
        foreach (Transform child in leaderboardContent) {
            if (child != null) {
                Destroy(child.gameObject);
            }
        }

        // Sort players by score and kills
        var sortedPlayers = playerStats.OrderByDescending(p => p.Value.Score)
                                     .ThenByDescending(p => p.Value.Kills)
                                     .ToList();

        // Create leaderboard entries
        foreach (var playerStat in sortedPlayers) {
            GameObject entry = Instantiate(leaderboardEntryPrefab, leaderboardContent);
            LeaderboardEntry entryScript = entry.GetComponent<LeaderboardEntry>();
            entryScript.SetStats(
                playerStat.Key,
                playerStat.Value.Score,
                playerStat.Value.Kills
            );
        }

        // Ensure the panel is visible and in front
        leaderboardPanel.SetActive(true);
        if (leaderboardPanel.GetComponent<Canvas>() != null) {
            leaderboardPanel.GetComponent<Canvas>().sortingOrder = 999;
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

    [PunRPC]
    void ShowFinalLeaderboard() {
        if (leaderboardContent == null || leaderboardPanel == null) return;

        // Clear existing entries
        foreach (Transform child in leaderboardContent) {
            if (child != null) {
                Destroy(child.gameObject);
            }
        }

        // Sort players by score
        var sortedPlayers = playerStats.OrderByDescending(p => p.Value.Score)
                                     .ThenByDescending(p => p.Value.Kills)
                                     .ToList();

        // Create leaderboard entries
        foreach (var playerStat in sortedPlayers) {
            GameObject entry = Instantiate(leaderboardEntryPrefab, leaderboardContent);
            LeaderboardEntry entryScript = entry.GetComponent<LeaderboardEntry>();
            entryScript.SetStats(
                playerStat.Key,
                playerStat.Value.Score,
                playerStat.Value.Kills
            );
        }

        leaderboardPanel.SetActive(true);
    }

    public void ReturnToLobby() {
        // Clean up before leaving
        if (leaderboardPanel != null) {
            leaderboardPanel.SetActive(false);
        }
        
        if (PhotonNetwork.IsConnected) {
            PhotonNetwork.LeaveRoom();
        }
        
        SceneManager.LoadScene("LobbyScene");
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
    }

    // Add method to handle room property updates
    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged) {
        if (propertiesThatChanged.ContainsKey("GameTime")) {
            float newTime = (float)propertiesThatChanged["GameTime"];
            currentGameTime = newTime;
            if (timerText != null) {
                timerText.text = FormatTime(currentGameTime);
            }
        }
    }

    [PunRPC]
    private void AddKill_RPC(string killerName) {
        Debug.Log($"AddKill_RPC called for player: {killerName}");
        
        if (!playerStats.ContainsKey(killerName)) {
            playerStats[killerName] = new PlayerStats();
        }
        
        if (!killStreaks.ContainsKey(killerName)) {
            killStreaks[killerName] = 0;
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
        if (!string.IsNullOrEmpty(notification)) {
            AddMessage($"{killerName} - {notification}!");
        }
        
        Debug.Log($"Updated stats for {killerName}: Kills={playerStats[killerName].Kills}, Score={currentScore}, Streak={killStreaks[killerName]}");
        
        // Update UI if this is the killer's client
        if (killerName == PhotonNetwork.LocalPlayer.NickName) {
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

    // Add this method to validate spawn points at start
    private void ValidateSpawnPoints()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("No spawn points assigned!");
            return;
        }

        List<Transform> validSpawnPoints = new List<Transform>();
        
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == null)
            {
                Debug.LogError($"Spawn point {i} is null!");
                continue;
            }

            NavMeshHit hit;
            if (NavMesh.SamplePosition(spawnPoints[i].position, out hit, 2.0f, NavMesh.AllAreas))
            {
                // Adjust spawn point position to be on NavMesh
                spawnPoints[i].position = hit.position + Vector3.up * 0.1f; // Slight offset to prevent ground clipping
                validSpawnPoints.Add(spawnPoints[i]);
            }
            else
            {
                Debug.LogWarning($"Spawn point {i} at {spawnPoints[i].position} is not on NavMesh. Adjusting position...");
                
                // Try to find a valid position nearby
                Vector3 searchStart = spawnPoints[i].position;
                for (float radius = 1f; radius <= 5f; radius += 1f)
                {
                    Vector3[] testPoints = new Vector3[]
                    {
                        searchStart + Vector3.forward * radius,
                        searchStart + Vector3.back * radius,
                        searchStart + Vector3.right * radius,
                        searchStart + Vector3.left * radius,
                        searchStart + (Vector3.forward + Vector3.right).normalized * radius,
                        searchStart + (Vector3.forward + Vector3.left).normalized * radius,
                        searchStart + (Vector3.back + Vector3.right).normalized * radius,
                        searchStart + (Vector3.back + Vector3.left).normalized * radius
                    };

                    foreach (Vector3 testPoint in testPoints)
                    {
                        if (NavMesh.SamplePosition(testPoint, out hit, 1.0f, NavMesh.AllAreas))
                        {
                            spawnPoints[i].position = hit.position + Vector3.up * 0.1f;
                            validSpawnPoints.Add(spawnPoints[i]);
                            Debug.Log($"Found valid position for spawn point {i} at {hit.position}");
                            goto PointFound;
                        }
                    }
                }
                
                Debug.LogError($"Could not find valid NavMesh position for spawn point {i}");
                continue;

                PointFound:
                continue;
            }
        }

        if (validSpawnPoints.Count == 0)
        {
            Debug.LogError("No valid spawn points found! Please ensure spawn points are placed on walkable areas.");
        }
        else
        {
            Debug.Log($"Found {validSpawnPoints.Count} valid spawn points out of {spawnPoints.Length} total points.");
        }
    }

    // Update the SpawnNPC method
    private void SpawnNPC()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        // Clean up null references
        activeNPCs.RemoveAll(npc => npc == null);
        
        // Check if we can spawn more NPCs
        if (activeNPCs.Count >= maxNPCs) return;

        // Get list of valid spawn points
        List<Transform> validSpawnPoints = new List<Transform>();
        foreach (Transform spawnPoint in spawnPoints)
        {
            if (spawnPoint == null) continue;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(spawnPoint.position, out hit, 1.0f, NavMesh.AllAreas))
            {
                Collider[] colliders = Physics.OverlapSphere(hit.position, 2f);
                bool isBlocked = colliders.Any(col => col.CompareTag("Player") || col.CompareTag("NPC"));
                
                if (!isBlocked)
                {
                    validSpawnPoints.Add(spawnPoint);
                }
            }
        }

        if (validSpawnPoints.Count == 0)
        {
            Debug.LogWarning("No valid spawn points available for NPC!");
            return;
        }

        // Select random spawn point and NPC model
        Transform selectedSpawnPoint = validSpawnPoints[Random.Range(0, validSpawnPoints.Count)];
        int modelIndex = Random.Range(0, npcModels.Length);

        // Get exact spawn position on NavMesh
        NavMeshHit navHit;
        Vector3 spawnPosition;
        if (NavMesh.SamplePosition(selectedSpawnPoint.position, out navHit, 1.0f, NavMesh.AllAreas))
        {
            spawnPosition = navHit.position + Vector3.up * 0.1f;
        }
        else
        {
            Debug.LogError("Failed to find NavMesh position for spawn!");
            return;
        }

        // Spawn the NPC
        GameObject npc = PhotonNetwork.Instantiate(
            npcModels[modelIndex].name,
            spawnPosition,
            selectedSpawnPoint.rotation,
            0
        );

        if (npc != null)
        {
            npc.tag = "NPC";
            DisablePlayerComponents(npc);
            ConfigureNPCComponents(npc);
            activeNPCs.Add(npc);
            
            // Schedule next spawn using npcRespawnTime
            nextNPCSpawnTime = Time.time + npcRespawnTime;
            
            Debug.Log($"Successfully spawned NPC at {spawnPosition}");
        }
    }

    private void DisablePlayerComponents(GameObject npc) {
        // Disable any player-specific scripts
        var playerComponents = npc.GetComponents<MonoBehaviour>();
        foreach (var comp in playerComponents) {
            if (comp.GetType().Name != "PhotonView" && 
                comp.GetType().Name != "NPCController") {
                comp.enabled = false;
            }
        }

        // Disable cameras if any
        var cameras = npc.GetComponentsInChildren<Camera>(true);
        foreach (var cam in cameras) {
            cam.gameObject.SetActive(false);
        }
    }

    private void ConfigureNPCComponents(GameObject npc) {
        // Add NavMeshAgent
        var agent = npc.AddComponent<UnityEngine.AI.NavMeshAgent>();
        agent.speed = 3.5f;
        agent.acceleration = 8f;
        agent.angularSpeed = 120f;
        agent.stoppingDistance = 2f;
        agent.radius = 0.5f;
        agent.height = 2f;

        // Add NPC Controller
        var npcController = npc.AddComponent<NPCController>();

        // Make sure the NPC has a rigidbody for physics
        var rb = npc.GetComponent<Rigidbody>();
        if (rb == null) {
            rb = npc.AddComponent<Rigidbody>();
            rb.isKinematic = true; // Let NavMeshAgent handle movement
        }

        // Ensure it has a collider
        var collider = npc.GetComponent<CapsuleCollider>();
        if (collider == null) {
            collider = npc.AddComponent<CapsuleCollider>();
            collider.height = 2f;
            collider.radius = 0.5f;
            collider.center = new Vector3(0, 1f, 0);
        }
    }

    // Add this method to handle NPC destruction
    public void OnNPCDeath(GameObject npc) {
        if (!PhotonNetwork.IsMasterClient) return;
        
        activeNPCs.Remove(npc);
        // Schedule next NPC spawn
        nextNPCSpawnTime = Time.time + npcSpawnDelay;
    }

    // Add this method to visualize spawn points in the editor
    private void OnDrawGizmos()
    {
        if (spawnPoints == null) return;

        foreach (Transform spawnPoint in spawnPoints)
        {
            if (spawnPoint == null) continue;

            // Check if point is on NavMesh
            NavMeshHit hit;
            bool isOnNavMesh = NavMesh.SamplePosition(spawnPoint.position, out hit, 1.0f, NavMesh.AllAreas);

            // Draw different colored spheres based on validity
            Gizmos.color = isOnNavMesh ? Color.green : Color.red;
            Gizmos.DrawWireSphere(spawnPoint.position, 1f);

            // Draw line to NavMesh if point is off
            if (!isOnNavMesh && hit.position != Vector3.zero)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(spawnPoint.position, hit.position);
            }
        }
    }

    // Modify the AddNPCKillScore method to include NPC name
    public void AddNPCKillScore(string killerName, string npcName)
    {
        if (!playerStats.ContainsKey(killerName))
        {
            playerStats[killerName] = new PlayerStats();
        }
        
        // Add score for killing an NPC (50 points instead of 100 for player kills)
        int scoreToAdd = 50;
        
        // Update killer's stats
        playerStats[killerName].Kills++;
        int currentScore = playerStats[killerName].Score + scoreToAdd;
        playerStats[killerName].Score = currentScore;
        
        // Update kill streak
        if (!killStreaks.ContainsKey(killerName))
        {
            killStreaks[killerName] = 0;
        }
        killStreaks[killerName]++;
        
        // Add kill message with NPC name
        AddMessage($"{killerName} eliminated {npcName} (+{scoreToAdd} points)");
        
        // Add kill streak notification if applicable
        string notification = GetKillStreakNotification(killStreaks[killerName]);
        if (!string.IsNullOrEmpty(notification))
        {
            AddMessage($"{killerName} - {notification}!");
        }
        
        Debug.Log($"Updated stats for {killerName}: Kills={playerStats[killerName].Kills}, Score={currentScore}, Streak={killStreaks[killerName]}");
        
        // Update UI if this is the killer's client
        if (killerName == PhotonNetwork.LocalPlayer.NickName)
        {
            UpdateUIStats(currentScore, playerStats[killerName].Kills);
        }
        
        // Sync stats across network
        photonView.RPC("UpdatePlayerStats_RPC", RpcTarget.All, 
            killerName, 
            currentScore, 
            playerStats[killerName].Kills);
    }

    // Add this method to handle NPC messages
    public void AddNPCMessage(string npcName, string message)
    {
        AddMessage($"{npcName}: {message}");
    }

}
