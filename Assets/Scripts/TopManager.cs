using UnityEngine;
using System.Collections.Generic;

public class TopManager : MonoBehaviour
{
    // List of all your top prefabs - assign these in Inspector
    public List<GameObject> topPrefabs = new List<GameObject>();
    private int currentIndex = 0;

    void Start()
    {
        // If we have prefabs, show the first one
        if (topPrefabs.Count > 0)
        {
            AppendTop(topPrefabs[0]);
        }
    }

    // Switch to next prefab in the list
    public void SwitchToNext()
    {
        // Remove current top if it exists
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // Move to next index
        currentIndex = (currentIndex + 1) % topPrefabs.Count;
        
        // Append new top
        AppendTop(topPrefabs[currentIndex]);
    }

    // Switch to previous prefab in the list
    public void SwitchToPrevious()
    {
        // Remove current top if it exists
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // Move to previous index
        currentIndex--;
        if (currentIndex < 0) 
            currentIndex = topPrefabs.Count - 1;
        
        // Append new top
        AppendTop(topPrefabs[currentIndex]);
    }

    private void AppendTop(GameObject topPrefab)
    {
        if (topPrefab == null) return;

        // Create new top as child
        GameObject newTop = Instantiate(topPrefab, transform);
        newTop.transform.localPosition = Vector3.zero;
        newTop.transform.localRotation = Quaternion.identity;
    }

    // Optional: Add keyboard controls
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E)) // Next top
        {
            SwitchToNext();
        }
        if (Input.GetKeyDown(KeyCode.Q)) // Previous top
        {
            SwitchToPrevious();
        }
    }
}
