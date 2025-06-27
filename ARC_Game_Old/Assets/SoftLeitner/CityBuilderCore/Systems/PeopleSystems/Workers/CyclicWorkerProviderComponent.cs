using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// provides workery periodically as long as the buildings is working, spawn rate is influenced by building efficiency
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_cyclic_worker_provider_component.html")]
    public class CyclicWorkerProviderComponent : BuildingComponent
    {
        public override string Key => "CWP";

        [Tooltip("spawner for configuring and managing the worker walkers")]
        public CyclicWorkerWalkerSpawner WorkerWalkers;

        private void Awake()
        {
            WorkerWalkers.InitializeWork(Building, this);
        }

        private void Update()
        {
            if (Building.IsWorking)
                WorkerWalkers.Update(Building.Efficiency);
        }

        #region Saving
        [Serializable]
        public class CyclicWorkerProviderData
        {
            public CyclicWalkerSpawnerData WorkerWalkers;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new CyclicWorkerProviderData()
            {
                WorkerWalkers = WorkerWalkers.SaveData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<CyclicWorkerProviderData>(json);

            WorkerWalkers.LoadData(data.WorkerWalkers);
        }
        #endregion
    }
}