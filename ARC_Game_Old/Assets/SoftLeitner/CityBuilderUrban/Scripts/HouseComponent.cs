using CityBuilderCore;
using UnityEngine;

namespace CityBuilderUrban
{
    /// <summary>
    /// can spawn randomly roaming <see cref="VanWalker"/>s or <see cref="PickupWalker"/>s it sends directly to a shop<br/>
    /// the pickups are only used in the Tunnel scene to demonstrate how they drive through the tunnel
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/urban">https://citybuilder.softleitner.com/manual/urban</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_urban_1_1_house_component.html")]
    public class HouseComponent : BuildingComponent
    {
        public override string Key => "HOS";

        public CyclicVanWalkerSpawner Vans;
        public CyclicPickupWalkerSpawner Pickups;

        private float _consumerism;

        private void Awake()
        {
            Vans.Initialize(Building);
            Pickups.Initialize(Building, w =>
             {
                 var path = Dependencies.Get<UrbanManager>().GetShopPath(Building.BuildingReference, w.PathType, w.PathTag);
                 if (path == null)
                     return false;
                 w.StartWalker(path);
                 return true;
             });
        }
        private void Update()
        {
            if (Building.IsWorking)
            {
                Vans.Update(_consumerism * Building.Efficiency);
                Pickups.Update(_consumerism * Building.Efficiency);
            }
        }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            _consumerism = Random.Range(0.8f, 1.2f);
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var houseReplacement = replacement.GetBuildingComponent<HouseComponent>();
            if (houseReplacement != null)
                houseReplacement._consumerism = _consumerism;
        }

        #region Saving
        [System.Serializable]
        public class RailwayData
        {
            public CyclicWalkerSpawnerData Vans;
            public CyclicWalkerSpawnerData Pickups;
            public float Consumerism;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new RailwayData()
            {
                Vans = Vans.SaveData(),
                Pickups = Pickups.SaveData(),
                Consumerism = _consumerism
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<RailwayData>(json);

            Vans.LoadData(data.Vans);
            Pickups.LoadData(data.Pickups);
            _consumerism = data.Consumerism;
        }
        #endregion
    }
}
