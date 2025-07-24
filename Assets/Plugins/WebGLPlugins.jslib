mergeInto(LibraryManager.library, {
  RedirectToURL: function(url) {
    window.location.href = UTF8ToString(url);
  },
  
  GetLocalStorageData: function(key) {
    var keyStr = UTF8ToString(key);
    var data = localStorage.getItem(keyStr) || "";
    var bufferSize = lengthBytesUTF8(data) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(data, buffer, bufferSize);
    return buffer;
  },

  SetLocalStorageData: function(key, value) {
    localStorage.setItem(UTF8ToString(key), UTF8ToString(value));
  },

  DeleteLocalStorageData: function(key) {
    localStorage.removeItem(UTF8ToString(key));
  },

  WebSocketConnect: function(url) {
    var urlStr = UTF8ToString(url);
    try {
      window.socket = new WebSocket(urlStr);
      return true;
    } catch(e) {
      return false;
    }
  },

  WebSocketClose: function() {
    if (window.socket) {
      window.socket.close();
    }
  },

  WebSocketSend: function(message) {
    if (window.socket && window.socket.readyState === WebSocket.OPEN) {
      window.socket.send(UTF8ToString(message));
    }
  },

  WebSocketState: function() {
    if (!window.socket) return 3; // Closed
    return window.socket.readyState;
  },

  DebugLog: function(message) {
    console.log(UTF8ToString(message));
  }
});
