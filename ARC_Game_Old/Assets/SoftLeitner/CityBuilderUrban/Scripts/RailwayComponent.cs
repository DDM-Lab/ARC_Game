using CityBuilderCore;
using System;
using UnityEngine;

namespace CityBuilderUrban
{
    /// <summary>
    /// spawns trains cyclically, whenever the train arrives it spawns trucks that supply shops
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/urban">https://citybuilder.softleitner.com/manual/urban</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_urban_1_1_railway_component.html")]
    public class RailwayComponent : BuildingComponent
    {
        public override string Key => "RWA";

        public Item Item;
        public ManualTruckWalkerSpawner Trucks;
        public CyclicTrainWalkerSpawner Trains;

        private void Awake()
        {
            Trucks.Initialize(Building);
            Trains.Initialize(Building);
        }
        private void Update()
        {
            Trains.Update();
        }

        public void TrainArrival()
        {
            Dependencies.Get<UrbanManager>().RemoveRunningCost();

            while (Trucks.HasWalker)
                Trucks.Spawn();
        }

        #region Saving
        [Serializable]
        public class RailwayData
        {
            public ManualWalkerSpawnerData Trucks;
            public CyclicWalkerSpawnerData Trains;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new RailwayData()
            {
                Trucks = Trucks.SaveData(),
                Trains = Trains.SaveData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<RailwayData>(json);

            Trucks.LoadData(data.Trucks);
            Trains.LoadData(data.Trains);
        }
        #endregion
    }
}
