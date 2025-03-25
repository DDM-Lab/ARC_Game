using UnityEngine;

public class EvacuationEvent : DisasterEvent
{
    public EvacuationEvent()
    {
        eventName = "Evacuation";
    }

    public override void Execute(BuildingComponent target)
    {
        if (!target.isEvacuated)
        {
            Debug.Log($"Evacuating {target.buildingName} due to flooding!");
            target.isEvacuated = true;
            target.assignedTasks.Add("Evacuation started");
        }
    }
}
