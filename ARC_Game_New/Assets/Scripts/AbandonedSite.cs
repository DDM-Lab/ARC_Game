using UnityEngine;
using System;

public class AbandonedSite : MonoBehaviour
{
    [Header("Site Information")]
    [SerializeField] private int siteId;
    [SerializeField] private bool isAvailable = true;
    
    [Header("Visual Components")]
    public SpriteRenderer siteRenderer;
    
    [Header("Site Colors")]
    public Color normalColor = Color.gray;
    public Color hoverColor = Color.white;
    public Color unavailableColor = Color.red;
    
    // Events
    public event Action<AbandonedSite> OnSiteSelected;
    
    private bool isMouseOver = false;
    
    void Start()
    {
        // Initialize visual components
        if (siteRenderer == null)
            siteRenderer = GetComponent<SpriteRenderer>();
        
        UpdateVisualState();
    }
    
    void OnMouseEnter()
    {
        if (isAvailable)
        {
            isMouseOver = true;
            UpdateVisualState();
        }
    }
    
    void OnMouseExit()
    {
        isMouseOver = false;
        UpdateVisualState();
    }
    
    void OnMouseDown()
    {
        // Handle site selection - show building selection UI
        if (isAvailable)
        {
            OnSiteSelected?.Invoke(this);
        }
    }
    
    public void Initialize(int id)
    {
        siteId = id;
        isAvailable = true;
        UpdateVisualState();
    }
    
    public void SetAvailability(bool available)
    {
        isAvailable = available;
        UpdateVisualState();
    }
    
    void UpdateVisualState()
    {
        if (siteRenderer == null) return;
        
        if (!isAvailable)
        {
            siteRenderer.color = unavailableColor;
        }
        else if (isMouseOver)
        {
            siteRenderer.color = hoverColor;
        }
        else
        {
            siteRenderer.color = normalColor;
        }
    }
    
    // Called when this site is converted to a building
    public void ConvertToBuilding()
    {
        isAvailable = false;
        
        // Disable this gameobject's components
        if (siteRenderer != null)
            siteRenderer.enabled = false;
        
        Collider2D siteCollider = GetComponent<Collider2D>();
        if (siteCollider != null)
            siteCollider.enabled = false;
        
        Debug.Log($"Site {siteId} converted to building");
    }
    
    // Getters
    public int GetId() => siteId;
    public bool IsAvailable() => isAvailable;
    
    // Debug info
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
        
        if (Application.isPlaying)
        {
            Vector3 labelPos = transform.position + Vector3.up * 0.8f;
            UnityEditor.Handles.Label(labelPos, $"Site {siteId}\n{(isAvailable ? "Available" : "Unavailable")}");
        }
    }
}