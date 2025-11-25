using UnityEngine;
using UnityEngine.EventSystems;
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

    [Header("Selection State")]
    private bool isSelected = false;
    
    public event Action<AbandonedSite> OnSiteSelected;
    
    private bool isMouseOver = false;
    
    void Start()
    {
        if (siteRenderer == null)
            siteRenderer = GetComponent<SpriteRenderer>();
        
        UpdateVisualState();
    }
    
    void OnMouseEnter()
    {
        if (ShouldBlockInteraction()) return;

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
        if (ShouldBlockInteraction()) return;
        
        if (isAvailable)
        {
            AudioManager.Instance.PlayClickSFX();
            OnSiteSelected?.Invoke(this);
        }
    }

    bool ShouldBlockInteraction()
    {
        if (isInSimulation()) return true;
        
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return true;
        
        return false;
    }

    bool isInSimulation()
    {
        return GlobalClock.Instance != null && GlobalClock.Instance.IsSimulationRunning();
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
        else if (isSelected || isMouseOver)
        {
            siteRenderer.color = hoverColor;
        }
        else
        {
            siteRenderer.color = normalColor;
        }
    }
    
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateVisualState();
    }
    
    public void ConvertToBuilding()
    {
        isAvailable = false;

        if (siteRenderer != null)
            siteRenderer.enabled = false;

        Collider2D siteCollider = GetComponent<Collider2D>();
        if (siteCollider != null)
            siteCollider.enabled = false;

        Debug.Log($"Site {siteId} converted to building");
    }
    
    public int GetId() => siteId;
    public bool IsAvailable() => isAvailable;
    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
    }
}