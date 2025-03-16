using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Now, GlobalManager is accessible from anywhere in the project using
// ex. GlobalManager.Instance.roundCount
public class GlobalManager : MonoBehaviour
{
    public static GlobalManager Instance { get; private set; }  // Singleton

    // Global Variables
    public int roundCount = 1;
    public int round2Day = 9;
    public int dayCount = 1;
    public bool debugMode = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Persist across scenes
        }
        else
        {
            Destroy(gameObject); // Prevent duplicates
        }
    }

    public void AdvanceRound()
    {
        roundCount++;
        if (roundCount % round2Day == 0) // Every round2Day rounds is a new day
        {
            dayCount++;
        }

        Debug.Log($"Round {roundCount}, Day {dayCount}, {round2Day} rounds for a day");
    }

    public void ToggleDebug()
    {
        debugMode = !debugMode;
        Debug.Log($"Debug mode is now: {debugMode}");
    }
}
