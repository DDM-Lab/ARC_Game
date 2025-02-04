using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// provides a fixed quantity of workers periodically as long as the buildings efficiency is working<br/>
    /// workers need to return and wait out their cooldown before being deployed again
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_pooled_worker_provider_component.html")]
    public class PooledWorkerProviderComponent : BuildingComponent
    {
        public override string Key => "PWP";

        [Tooltip("spawner for configuring and managing the worker walkers")]
        public PooledWorkerWalkerSpawner WorkerWalkers;
        [Tooltip(@"Instant
    find path on spawn
    if none is found the spawn is aborted
Prepared
    the spawner prepares a path query before spawning, no spawn happens if none is found
Delayed
    walkers spawns and looks for path while delay runs")]
        public WalkerInitializationMode InitializationMode = WalkerInitializationMode.Instant;

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
        public class PooledWorkerProviderData
        {
            public PooledWalkerSpawnerData WorkerWalkers;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new PooledWorkerProviderData()
            {
                WorkerWalkers = WorkerWalkers.SaveData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<PooledWorkerProviderData>(json);

            WorkerWalkers.LoadData(data.WorkerWalkers);
        }
        #endregion
    }
}