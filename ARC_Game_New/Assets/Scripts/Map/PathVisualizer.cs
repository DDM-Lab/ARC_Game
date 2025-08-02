using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class PathVisualizer : MonoBehaviour
{
    [Header("Visual Settings")]
    public Color pathColor = Color.cyan;
    public Color startPointColor = Color.green;
    public Color endPointColor = Color.red;
    public float lineWidth = 0.1f;
    public float pointSize = 0.3f;
    
    [Header("Animation")]
    public bool animatePath = true;
    public float animationSpeed = 2f;
    public float pathDisplayDuration = 5f; // How long to show path (0 = permanent)
    
    [Header("Debug")]
    public bool showWaypoints = true;
    public bool showDistanceLabels = false;
    
    // Visual components
    private LineRenderer pathLineRenderer;
    private List<GameObject> waypointMarkers = new List<GameObject>();
    private List<GameObject> distanceLabels = new List<GameObject>();
    private Coroutine currentAnimation;
    private Coroutine clearPathCoroutine;
    
    // Current path data
    private List<Vector3> currentPath = new List<Vector3>();
    private bool isPathVisible = false;
    
    void Awake()
    {
        CreateLineRenderer();
    }
    
    void CreateLineRenderer()
    {
        // Create LineRenderer for path visualization
        GameObject lineObject = new GameObject("PathLine");
        lineObject.transform.SetParent(transform);
        
        pathLineRenderer = lineObject.AddComponent<LineRenderer>();
        pathLineRenderer.material = CreateLineMaterial();
        pathLineRenderer.startWidth = lineWidth;
        pathLineRenderer.endWidth = lineWidth;
        pathLineRenderer.positionCount = 0;
        pathLineRenderer.useWorldSpace = true;
        pathLineRenderer.sortingOrder = 10; // Render above other sprites
    }
    
    Material CreateLineMaterial()
    {
        // Create a simple colored material for the path line
        Material lineMaterial = new Material(Shader.Find("Sprites/Default"));
        lineMaterial.color = pathColor;
        return lineMaterial;
    }
    
    /// <summary>
    /// Show a path with optional animation
    /// </summary>
    public void ShowPath(List<Vector3> path)
    {
        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("Cannot show empty path");
            return;
        }
        
        // Clear any existing path
        ClearPath();
        
        currentPath = new List<Vector3>(path);
        isPathVisible = true;
        
        if (animatePath)
        {
            // Start animated path display
            if (currentAnimation != null)
                StopCoroutine(currentAnimation);
            currentAnimation = StartCoroutine(AnimatePathDisplay());
        }
        else
        {
            // Show complete path immediately
            DisplayCompletePath();
        }
        
        // Auto-clear path after duration (if set)
        if (pathDisplayDuration > 0)
        {
            if (clearPathCoroutine != null)
                StopCoroutine(clearPathCoroutine);
            clearPathCoroutine = StartCoroutine(ClearPathAfterDelay());
        }
        
        Debug.Log($"Showing path with {path.Count} waypoints");
    }
    
    /// <summary>
    /// Display the complete path immediately
    /// </summary>
    void DisplayCompletePath()
    {
        if (currentPath.Count == 0)
            return;
        
        // Set up line renderer
        pathLineRenderer.positionCount = currentPath.Count;
        pathLineRenderer.SetPositions(currentPath.ToArray());
        
        // Create waypoint markers
        if (showWaypoints)
        {
            CreateWaypointMarkers();
        }
        
        // Create distance labels
        if (showDistanceLabels)
        {
            CreateDistanceLabels();
        }
    }
    
    /// <summary>
    /// Animate path display progressively
    /// </summary>
    IEnumerator AnimatePathDisplay()
    {
        if (currentPath.Count == 0)
            yield break;
        
        float totalDistance = CalculatePathDistance();
        float currentDistance = 0f;
        
        List<Vector3> animatedPath = new List<Vector3>();
        
        for (int i = 0; i < currentPath.Count; i++)
        {
            animatedPath.Add(currentPath[i]);
            
            // Update line renderer
            pathLineRenderer.positionCount = animatedPath.Count;
            pathLineRenderer.SetPositions(animatedPath.ToArray());
            
            // Create waypoint marker for this point
            if (showWaypoints)
            {
                CreateWaypointMarker(currentPath[i], i);
            }
            
            // Wait based on animation speed
            if (i < currentPath.Count - 1)
            {
                float segmentDistance = Vector3.Distance(currentPath[i], currentPath[i + 1]);
                currentDistance += segmentDistance;
                float waitTime = (segmentDistance / totalDistance) * (1f / animationSpeed);
                yield return new WaitForSecondsRealtime(waitTime);
            }
        }
        
        // Create distance labels after animation completes
        if (showDistanceLabels)
        {
            CreateDistanceLabels();
        }
        
        currentAnimation = null;
    }
    
    /// <summary>
    /// Create waypoint markers for the entire path
    /// </summary>
    void CreateWaypointMarkers()
    {
        for (int i = 0; i < currentPath.Count; i++)
        {
            CreateWaypointMarker(currentPath[i], i);
        }
    }
    
    /// <summary>
    /// Create a single waypoint marker
    /// </summary>
    void CreateWaypointMarker(Vector3 position, int index)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.transform.position = position;
        marker.transform.localScale = Vector3.one * pointSize;
        marker.transform.SetParent(transform);
        marker.name = $"Waypoint_{index}";
        
        // Remove collider
        Destroy(marker.GetComponent<Collider>());
        
        // Set color based on position in path
        Renderer markerRenderer = marker.GetComponent<Renderer>();
        if (index == 0)
        {
            markerRenderer.material.color = startPointColor;
        }
        else if (index == currentPath.Count - 1)
        {
            markerRenderer.material.color = endPointColor;
        }
        else
        {
            markerRenderer.material.color = pathColor;
        }
        
        waypointMarkers.Add(marker);
    }
    
    /// <summary>
    /// Create distance labels for path segments
    /// </summary>
    void CreateDistanceLabels()
    {
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Vector3 startPos = currentPath[i];
            Vector3 endPos = currentPath[i + 1];
            Vector3 midPoint = (startPos + endPos) * 0.5f;
            float distance = Vector3.Distance(startPos, endPos);
            
            GameObject labelObject = new GameObject($"DistanceLabel_{i}");
            labelObject.transform.position = midPoint + Vector3.up * 0.5f;
            labelObject.transform.SetParent(transform);
            
            // Create 3D text (you might want to replace this with UI text)
            TextMesh textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = distance.ToString("F1");
            textMesh.fontSize = 20;
            textMesh.color = Color.white;
            textMesh.anchor = TextAnchor.MiddleCenter;
            
            distanceLabels.Add(labelObject);
        }
    }
    
    /// <summary>
    /// Calculate total path distance
    /// </summary>
    float CalculatePathDistance()
    {
        float totalDistance = 0f;
        
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            totalDistance += Vector3.Distance(currentPath[i], currentPath[i + 1]);
        }
        
        return totalDistance;
    }
    
    /// <summary>
    /// Clear the current path visualization
    /// </summary>
    public void ClearPath()
    {
        // Stop any running coroutines
        if (currentAnimation != null)
        {
            StopCoroutine(currentAnimation);
            currentAnimation = null;
        }
        
        if (clearPathCoroutine != null)
        {
            StopCoroutine(clearPathCoroutine);
            clearPathCoroutine = null;
        }
        
        // Clear line renderer
        if (pathLineRenderer != null)
        {
            pathLineRenderer.positionCount = 0;
        }
        
        // Clear waypoint markers
        foreach (GameObject marker in waypointMarkers)
        {
            if (marker != null)
                DestroyImmediate(marker);
        }
        waypointMarkers.Clear();
        
        // Clear distance labels
        foreach (GameObject label in distanceLabels)
        {
            if (label != null)
                DestroyImmediate(label);
        }
        distanceLabels.Clear();
        
        currentPath.Clear();
        isPathVisible = false;
        
        Debug.Log("Path visualization cleared");
    }
    
    /// <summary>
    /// Auto-clear path after specified duration
    /// </summary>
    IEnumerator ClearPathAfterDelay()
    {
        yield return new WaitForSecondsRealtime(pathDisplayDuration);
        ClearPath();
    }
    
    /// <summary>
    /// Toggle path visibility
    /// </summary>
    public void TogglePathVisibility()
    {
        if (pathLineRenderer != null)
        {
            pathLineRenderer.enabled = !pathLineRenderer.enabled;
        }
        
        foreach (GameObject marker in waypointMarkers)
        {
            if (marker != null)
                marker.SetActive(pathLineRenderer.enabled);
        }
        
        foreach (GameObject label in distanceLabels)
        {
            if (label != null)
                label.SetActive(pathLineRenderer.enabled);
        }
    }
    
    /// <summary>
    /// Get current path information for debugging
    /// </summary>
    public void PrintPathInfo()
    {
        if (currentPath.Count == 0)
        {
            Debug.Log("No path currently displayed");
            return;
        }
        
        float totalDistance = CalculatePathDistance();
        Debug.Log($"=== PATH INFORMATION ===");
        Debug.Log($"Waypoints: {currentPath.Count}");
        Debug.Log($"Total Distance: {totalDistance:F2}");
        Debug.Log($"Path Visible: {isPathVisible}");
        
        for (int i = 0; i < currentPath.Count; i++)
        {
            Debug.Log($"Waypoint {i}: {currentPath[i]}");
        }
    }
    
    [ContextMenu("Print Path Info")]
    public void DebugPrintPathInfo()
    {
        PrintPathInfo();
    }
    
    [ContextMenu("Clear Path")]
    public void DebugClearPath()
    {
        ClearPath();
    }
}