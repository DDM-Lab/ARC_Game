using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CityBuilderCore;


/// <summary>
/// -----------------------Manages all disaster systems-------------------------
/// </summary>
public class DisasterManager : MonoBehaviour
{
    [Header("Disaster Systems")]
    public FloodManager floodManager;
    
    [Header("Settings")]
    public float disasterPhaseDelay = 2f;
    
    private void Awake()
    {
        // Get required components
        if (floodManager == null)
            floodManager = FindObjectOfType<FloodManager>();
    }
    
    /// <summary>
    /// Process all disaster events for the current round
    /// </summary>
    public IEnumerator ProcessDisasters()
    {
        Debug.Log("Processing disaster events...");
        
        // Process flooding
        if (floodManager != null)
        {
            floodManager.SimulateFlooding();
        }
        
        // Other disasters can be added here
        
        yield return new WaitForSeconds(disasterPhaseDelay);
    }
}
