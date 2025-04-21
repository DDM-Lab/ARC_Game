using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CityBuilderCore;


/// <summary>
/// -----------------------Manages round progression and timing
/// </summary>
public class RoundManager : MonoBehaviour
{
    [Header("Round Settings")]
    public float startRoundDelay = 1f;
    public float endRoundDelay = 1f;

    [Header("References")]
    public MonoBehaviour uiManager;
    
    /// <summary>
    /// Handle start of round behaviors
    /// </summary>
    public IEnumerator StartRound(int roundNumber, int dayNumber)
    {
        Debug.Log($"Round {roundNumber} (Day {dayNumber}) begins!");
        
        // Update UI if available
        if (uiManager != null)
        {
            // Use reflection to call the method if it exists
            var method = uiManager.GetType().GetMethod("UpdateRoundText");
            if (method != null)
            {
                method.Invoke(uiManager, new object[] { roundNumber, dayNumber });
            }
            else
            {
                Debug.LogWarning("[RoundManager] UIManager doesn't have UpdateRoundText method");
            }
        }
        
        // Add any other start of round logic here
        
        // Pause for transitions
        yield return new WaitForSeconds(startRoundDelay);
    }
    
    /// <summary>
    /// Handle end of round behaviors
    /// </summary>
    public IEnumerator EndRound()
    {
        Debug.Log("Round ends!");
        
        // Add any end of round logic here
        
        // Pause for transitions
        yield return new WaitForSeconds(endRoundDelay);
    }
}