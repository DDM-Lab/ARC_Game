using System.Collections;
using System.Collections.Generic;
using UnityEngine;
/*
This script will:
- Track the current round number.
- Control the game phases.
- Invoke necessary events (like flooding).
- Allow for future expansions (like AI behavior).

*/

public class GameManager : MonoBehaviour
{
    public static GameManager Instance; // Singleton for easy access

    public GlobalEnums.GamePhase currentPhase;

    public FloodManager floodManager;  // Reference to the flood disaster system
    public UIManager uiManager;  // Reference to UI for round updates
    public bool Game_Debug = true;  // Toggle debug logs

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void Start()
    {
        StartCoroutine(RoundLoop()); // Start the first round
    }

    private IEnumerator RoundLoop()
    {
        while (true) // Game runs indefinitely unless a condition ends it
        {
            yield return StartCoroutine(StartRound());

            yield return StartCoroutine(PlayerTurn());

            yield return StartCoroutine(DisasterEvents());

            yield return StartCoroutine(EndRound());
        }
    }

    private IEnumerator StartRound()
    {
        currentPhase = GlobalEnums.GamePhase.Start;
        DebugLog($"Round {GlobalManager.Instance.roundCount} begins!");

        // Update UI
        uiManager.UpdateRoundText(GlobalManager.Instance.roundCount, GlobalManager.Instance.dayCount);

        // Pause for a moment (simulating animations or transitions)
        yield return new WaitForSeconds(1f);
    }

    private IEnumerator PlayerTurn()
    {
        currentPhase = GlobalEnums.GamePhase.PlayerTurn;
        DebugLog("Player's turn starts! You can build, move, or manage resources.");

        // Here, you may want to wait for the player to press "End Turn" before continuing
        yield return new WaitUntil(() => Input.GetKeyDown(KeyCode.Space));

        DebugLog("Player's turn has ended.");
    }

    private IEnumerator DisasterEvents()
    {
        currentPhase = GlobalEnums.GamePhase.DisasterEvents;
        DebugLog("Checking for disaster events...");

        // Trigger flood disaster based on probability
        floodManager.SimulateFlooding();
        
        BuildingManager.Instance.CheckFloodingAndTriggerEvents();


        // Other disasters can be added here later
        yield return new WaitForSeconds(2f);
    }

    private IEnumerator EndRound()
    {
        currentPhase = GlobalEnums.GamePhase.End;
        DebugLog($"Round {GlobalManager.Instance.roundCount} ends!");

        // Advance the round using GlobalManager
        GlobalManager.Instance.AdvanceRound();

        // Allow a short pause before moving to the next round
        yield return new WaitForSeconds(1f);
    }

    // publicly exposes turn advancement while keeping disaster logic internal.
    public void EndTurn()
    {
        if (currentPhase == GlobalEnums.GamePhase.PlayerTurn)
        {
            DebugLog("Ending player's turn...");
            StartCoroutine(DisasterEvents()); // Proceed to disaster phase
        }
        else
        {
            DebugLog("Cannot end turn at this phase.");
        }
    }

    private void DebugLog(string message)
    {
        if (Game_Debug)
        {
            Debug.Log(message);
        }
    }

}