using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// waits to be occupied, is then replaced be the actual building
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_housing_placeholder_component.html")]
    public class HousingPlaceholderComponent : BuildingComponent, IHousing
    {
        public override string Key => "HSP";

        [Tooltip("building used as soon as the placeholder is occupied")]
        public Building Prefab;

        public BuildingComponentReference<IHousing> Reference { get; set; }

        private Population _reservedPopulation;
        private int _reservedQuantity = 0;

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<IHousing>(this);
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            replaceTrait(this, replacement.GetBuildingComponent<IHousing>());
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IHousing>(this);
        }

        public int GetQuantity(Population population, bool includeReserved = false)
        {
            if (!includeReserved)
                return 0;
            if (_reservedPopulation == null && _reservedPopulation != population)
                return 0;
            return _reservedQuantity;
        }
        public int GetCapacity(Population population) => getCapacity(population);
        public int GetRemainingCapacity(Population population) => _reservedQuantity == 0 ? getCapacity(population) : 0;

        public int Reserve(Population population, int quantity)
        {
            if (_reservedQuantity > 0)
                return quantity;
            _reservedPopulation = population;
            _reservedQuantity = Mathf.Min(getCapacity(population), quantity);
            return quantity - _reservedQuantity;
        }
        public int Inhabit(Population population, int quantity)
        {
            var building = Building.Replace(Prefab.Info.GetPrefab(Building.Index));
            building.GetBuildingComponent<IEvolution>()?.InitializeComponent();
            return building.GetBuildingComponent<IHousing>().Inhabit(population, quantity);
        }
        public int Abandon(Population population, int quantity) { return 0; }
        public void Kill(float ratio) { }

        private int getCapacity(Population population)
        {
            if (Building.IsWorking)
                return Prefab.GetBuildingComponent<IHousing>().GetCapacity(population);
            else
                return 0;
        }

        #region Saving
        [Serializable]
        public class HousingPlaceholderData
        {
            public string ReservedPopulation;
            public int ReservedQuantity;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new HousingPlaceholderData()
            {
                ReservedPopulation=_reservedPopulation?.Key,
                ReservedQuantity=_reservedQuantity
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<HousingPlaceholderData>(json);

            if (!string.IsNullOrWhiteSpace(data.ReservedPopulation))
            {
                _reservedPopulation = Dependencies.Get<IKeyedSet<Population>>().GetObject(data.ReservedPopulation);
                _reservedQuantity = data.ReservedQuantity;
            }
        }
        #endregion
    }
}