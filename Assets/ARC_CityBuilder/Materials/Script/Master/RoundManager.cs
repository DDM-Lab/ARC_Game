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
    
    /// <summary>
    /// Handle start of round behaviors
    /// </summary>
    public IEnumerator StartRound(int roundNumber, int dayNumber)
    {
        Debug.Log($"Round {roundNumber} (Day {dayNumber}) begins!");
        
        // Update UI
        var uiManager = FindObjectOfType<UIManager>();
        if (uiManager != null)
        {
            uiManager.UpdateRoundText(roundNumber, dayNumber);
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