using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper for scores that combines a building with a value<br/>
    /// used to define how much value a building type carries in coverage or average building scores
    /// </summary>
    [Serializable]
    public class BuildingEvaluation
    {
        [Tooltip("the building the value will be assigned to ")]
        public BuildingInfo Building;
        [Tooltip("the value that will contribute to the score")]
        public int Value;

        public int GetCount() => Dependencies.Get<IBuildingManager>().Count(Building);
        public int GetValue() => Value;
        public int GetEvaluation() => GetCount() * GetValue();
    }
}