using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that periodically spawns a <see cref="RiskWalker"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/risks">https://citybuilder.softleitner.com/manual/risks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_risk_walker_component.html")]
    public class RiskWalkerComponent : BuildingComponent
    {
        public override string Key => "RSW";

        [Tooltip("holds the risk walkers that periodically spawn, roam around and reduce the risks they pass")]
        public CyclicRiskWalkerSpawner RiskWalkers;

        private void Awake()
        {
            RiskWalkers.Initialize(Building);
        }
        private void Update()
        {
            if (Building.IsWorking)
                RiskWalkers.Update();
        }

        #region Saving
        [Serializable]
        public class RiskWalkerData
        {
            public CyclicWalkerSpawnerData SpawnerData;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new RiskWalkerData() { SpawnerData = RiskWalkers.SaveData() });
        }
        public override void LoadData(string json)
        {
            RiskWalkers.LoadData(JsonUtility.FromJson<RiskWalkerData>(json).SpawnerData);
        }
        #endregion
    }
}