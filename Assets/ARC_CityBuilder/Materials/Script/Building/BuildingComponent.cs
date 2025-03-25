using UnityEngine;

public class BuildingComponent : MonoBehaviour
{
    public string buildingName;
    public GlobalEnums.BuildingType buildingType;
    public int capacity;

    [HideInInspector] public bool isFlooded = false;
    [HideInInspector] public bool isEvacuated = false;
    public readonly System.Collections.Generic.List<string> assignedTasks = new();

    public void TriggerEvent(DisasterEvent disasterEvent)
    {
        disasterEvent.Execute(this);
    }
}
