using UnityEngine;
public abstract class DisasterEvent
{
    public string eventName;
    public abstract void Execute(BuildingComponent target);
}

