using UnityEngine;

public enum ResourceType
{
    Population,
    FoodPacks
}

[System.Serializable]
public class ResourceAmount
{
    public ResourceType type;
    public int amount;
    
    public ResourceAmount(ResourceType type, int amount)
    {
        this.type = type;
        this.amount = amount;
    }
    
    public override string ToString()
    {
        return $"{amount} {type}";
    }
}