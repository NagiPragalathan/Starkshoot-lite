using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

public class NPCNameTag : MonoBehaviourPunCallbacks
{
    private Text nameText;
    private Canvas canvas;
    private Camera mainCamera;
    
    void Awake()
    {
        CreateNameTagUI();
        mainCamera = Camera.main;
    }

    void LateUpdate()
    {
        if (mainCamera != null)
        {
            // Make the name tag always face the camera
            transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
                mainCamera.transform.rotation * Vector3.up);
        }
    }

    public void SetName(string name)
    {
        if (nameText == null)
        {
            CreateNameTagUI();
        }
        
        if (nameText != null)
        {
            nameText.text = name;
        }
    }

    void CreateNameTagUI()
    {
        // Create Canvas
        GameObject canvasObj = new GameObject("NameTagCanvas");
        canvasObj.transform.SetParent(transform);
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        
        // Add Canvas Scaler
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        
        // Create Background Panel
        GameObject panelObj = new GameObject("NamePanel");
        panelObj.transform.SetParent(canvasObj.transform);
        Image panel = panelObj.AddComponent<Image>();
        panel.color = new Color(0, 0, 0, 0.5f);
        
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(200, 50);
        
        // Create Text Object
        GameObject textObj = new GameObject("NameText");
        textObj.transform.SetParent(panelObj.transform);
        
        nameText = textObj.AddComponent<Text>();
        nameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        nameText.alignment = TextAnchor.MiddleCenter;
        nameText.color = Color.white;
        nameText.fontSize = 30;
        
        // Position everything
        canvasObj.transform.localPosition = Vector3.up * 2f;
        canvasObj.transform.localScale = Vector3.one * 0.01f;
        
        RectTransform textRect = nameText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }
} 