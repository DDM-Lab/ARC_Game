using UnityEngine;
using CityBuilderCore;
using System.Collections.Generic;

public class CustomBuildingBuilder : BuildingBuilder
{
    protected override void build(IEnumerable<Vector2Int> points)
    {
        base.build(points); // Or override with your own logic

        Debug.Log($"[CustomBuildingBuilder] Built {BuildingInfo.Name} at {string.Join(",", points)}");

    }
} 
