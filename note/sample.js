const Photon = require("photon-realtime")

// Initialize the LoadBalancing client with your Unity settings
const LBC = Photon.LoadBalancing.LoadBalancingClient;
const APP_ID = "67d44a00-4f4b-437e-b8b5-a3305f2522c8"; // Your Unity App ID
const APP_VERSION = "1.0"; // Your Unity App Version

// We have to use WebSocket (Wss) here as Node.js doesn't support UDP for Photon
const lbc = new LBC(Photon.ConnectionProtocol.Wss, APP_ID, APP_VERSION);

// Enable auto-join lobby to match Unity's behavior
lbc.autoJoinLobby = true;

console.log("Connecting to Photon (WebSocket protocol)...");

// Set up event handlers
lbc.onStateChange = function (state) {
    console.log("State:", LBC.StateToName(state));
    switch (state) {
        case LBC.State.ConnectedToMaster:
            console.log("Connected to master");
            // Auto-join lobby is enabled, so we don't need to manually join
            break;
        case LBC.State.JoinedLobby:
            console.log("Joined lobby");
            // List any existing rooms
            const rooms = lbc.availableRooms();
            console.log("Available rooms:", rooms.length);
            rooms.forEach(room => {
                console.log("Room:", {
                    name: room.name,
                    playerCount: room.playerCount,
                    maxPlayers: room.maxPlayers,
                    isOpen: room.isOpen,
                    isVisible: room.isVisible,
                    properties: room.getCustomProperties()
                });
            });
            break;
    }
};

// Enhanced room list updates
lbc.onRoomList = function(rooms) {
    console.log("Room list updated. Total rooms:", rooms.length);
    rooms.forEach(room => {
        console.log("Room:", {
            name: room.name,
            playerCount: room.playerCount,
            maxPlayers: room.maxPlayers,
            isOpen: room.isOpen,
            isVisible: room.isVisible,
            properties: room.getCustomProperties()
        });
    });
};

lbc.onRoomListUpdate = function(rooms, roomsUpdated, roomsAdded, roomsRemoved) {
    console.log("Room list updated:");
    console.log("- Updated rooms:", roomsUpdated);
    console.log("- Added rooms:", roomsAdded);
    console.log("- Removed rooms:", roomsRemoved);
};

// More detailed error handling
lbc.onError = function(errorCode, errorMsg) {
    console.error("Photon Error:", errorCode, errorMsg);
    // Log the state when error occurs
    console.log("Current state:", LBC.StateToName(lbc.state()));
};

// Add these additional event handlers
lbc.onOperationResponse = function(errorCode, errorMsg, code, content) {
    console.log("Operation Response:", {
        operation: code,
        errorCode: errorCode,
        errorMsg: errorMsg,
        content: content
    });
};

lbc.onEvent = function(code, content, actorNr) {
    console.log("Event Received:", {
        code: code,
        content: content,
        actorNr: actorNr
    });
};

// Set the region to match your Unity settings
lbc.connectToRegionMaster("eu"); // Using your EU region setting

// Keep the process running
process.on('SIGINT', () => {
    console.log("Disconnecting...");
    lbc.disconnect();
    process.exit();
});