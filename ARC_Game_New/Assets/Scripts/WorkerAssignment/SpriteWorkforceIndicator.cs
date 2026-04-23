using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SpriteWorkforceIndicator : MonoBehaviour
{
    [Header("Sprite Renderers")]
    public SpriteRenderer[] indicatorRenderers = new SpriteRenderer[4]; // sprite, not Image, for 2D sprite rendering

    [Header("Worker Sprites")]
    public Sprite emptySprite;          // empty state sprite (empty circle)
    public Sprite trainedWorkerSprite;  // trained worker sprite (blue circle)
    public Sprite untrainedWorkerSprite; // untrained worker sprite (green circle)

    void Start()
    {
        // initially set all indicators to empty state
        UpdateIndicator(0, 0);
    }
    
    /// <summary>
    /// update workforce indicator
    /// </summary>
    /// <param name="trainedWorkers">Trained worker num</param>
    /// <param name="untrainedWorkers">Untrained worker num</param>
    public void UpdateIndicator(int trainedWorkers, int untrainedWorkers)
    {
        // total workforce calculation
        int totalWorkforce = (trainedWorkers * 2) + untrainedWorkers;
        
        // capping total workforce to 4
        totalWorkforce = Mathf.Min(totalWorkforce, 4);
        
        bool[] workforceTypes = CreateWorkforceArray(trainedWorkers, untrainedWorkers, totalWorkforce);
        
        // update each indicator sprite
        for (int i = 0; i < indicatorRenderers.Length; i++)
        {
            if (indicatorRenderers[i] != null)
            {
                UpdateSingleIndicator(indicatorRenderers[i], i < totalWorkforce, 
                                    i < workforceTypes.Length ? workforceTypes[i] : false);
            }
        }
        
        Debug.Log($"Workforce indicator updated: {trainedWorkers} trained, {untrainedWorkers} untrained (Total workforce: {totalWorkforce})");
    }
    
    /// <summary>
    /// create workforce array based on trained and untrained workers
    /// </summary>
    bool[] CreateWorkforceArray(int trainedWorkers, int untrainedWorkers, int totalWorkforce)
    {
        List<bool> workforceList = new List<bool>();
        
        // first add trained workers' workforce
        for (int i = 0; i < trainedWorkers; i++)
        {
            workforceList.Add(true);  // trained workforce
            if (workforceList.Count < totalWorkforce)
                workforceList.Add(true);
        }
        
        // add untrained workers' workforce 
        for (int i = 0; i < untrainedWorkers && workforceList.Count < totalWorkforce; i++)
        {
            workforceList.Add(false); // untrained workforce
        }
        
        return workforceList.ToArray();
    }
    
    /// <summary>
    /// update each indicator sprite based on worker type
    /// </summary>
    void UpdateSingleIndicator(SpriteRenderer indicator, bool isActive, bool isTrained)
    {

        // use different sprites based on worker type
        if (!isActive)
        {
            indicator.sprite = emptySprite;
        }
        else if (isTrained)
        {
            indicator.sprite = trainedWorkerSprite;
        }
        else
        {
            indicator.sprite = untrainedWorkerSprite;
        }

    }
    
    /// <summary>
    /// from building update the workforce indicator
    /// </summary>
    public void UpdateFromBuilding(Building building, WorkerSystem workerSystem)
    {
        if (building == null || workerSystem == null) return;
        
        // get assigned workers from the worker system
        var assignedWorkers = workerSystem.GetWorkersByBuildingId(building.GetOriginalSiteId());
        
        int trainedCount = 0;
        int untrainedCount = 0;
        
        foreach (var worker in assignedWorkers)
        {
            if (worker.Type == WorkerType.Trained)
                trainedCount++;
            else
                untrainedCount++;
        }
        
        UpdateIndicator(trainedCount, untrainedCount);
    }
    
    /// <summary>
    /// manual set empty state
    /// </summary>
    public void SetEmpty()
    {
        UpdateIndicator(0, 0);
    }
    
    /// <summary>
    /// test methods for quick testing in editor
    /// </summary>
    [ContextMenu("Test: 2 Trained Workers")]
    void TestTwoTrained()
    {
        UpdateIndicator(2, 0); // 2 trained workers = 2*2 = 4 workforce
    }
    
    [ContextMenu("Test: 1 Trained + 2 Untrained")]
    void TestMixed()
    {
        UpdateIndicator(1, 2); // 1 trained + 2 untrained = 2 + 2 = 4 workforce
    }
    
    [ContextMenu("Test: 4 Untrained")]
    void TestFourUntrained()
    {
        UpdateIndicator(0, 4); // 4 untrained workers = 4 workforce
    }
    
    [ContextMenu("Test: Empty")]
    void TestEmpty()
    {
        SetEmpty();
    }
}