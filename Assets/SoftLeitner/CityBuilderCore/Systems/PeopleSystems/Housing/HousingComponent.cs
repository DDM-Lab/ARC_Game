using System;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// prodvides housing for the population, influences efficiency by its occuption(efficiency could be used for tax generation for example)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_housing_component.html")]
    public class HousingComponent : BuildingComponent, IHousing, IEfficiencyFactor
    {
        public override string Key => "HSG";

        [Tooltip("the different housings available, a single housing may house multiple population types")]
        public PopulationHousing[] PopulationHousings;

        public bool IsWorking => true;
        public float Factor => PopulationHousings.Select(p => (float)p.Quantity / p.Capacity).Aggregate((a, b) => a * b);

        public BuildingComponentReference<IHousing> Reference { get; set; }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<IHousing>(this);
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var housingReplacement = replacement.GetBuildingComponent<IHousing>();

            replaceTrait(this, housingReplacement);

            if (housingReplacement == null)
                return;

            foreach (var populationHousing in PopulationHousings)
            {
                int remainingInhabitants = housingReplacement.Inhabit(populationHousing.Population, populationHousing.Quantity);
                housingReplacement.Reserve(populationHousing.Population, populationHousing.Reserved);

                if (remainingInhabitants > 0)
                    Dependencies.Get<IPopulationManager>().AddHomeless(populationHousing.Population, this, remainingInhabitants);
            }
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IHousing>(this);
        }

        public override void SuspendComponent()
        {
            base.SuspendComponent();

            foreach (var populationHousing in PopulationHousings)
            {
                if (populationHousing.Quantity == 0)
                    continue;

                Dependencies.Get<IPopulationManager>().AddHomeless(populationHousing.Population, this, populationHousing.Quantity);
                populationHousing.Quantity = 0;
            }
        }

        public int GetQuantity(Population population, bool includeReserved = false)
        {
            if (Building != null && !Building.IsWorking)
                return 0;
            else
                return PopulationHousings.GetQuantity(population, includeReserved);
        }
        public int GetCapacity(Population population)
        {
            if (Building != null && !Building.IsWorking)
                return 0;

            return PopulationHousings.GetCapacity(population);
        }
        public int GetRemainingCapacity(Population population)
        {
            if (Building != null && !Building.IsWorking)
                return 0;

            return PopulationHousings.GetRemainingCapacity(population);
        }

        public int Reserve(Population population, int quantity) => PopulationHousings.Reserve(population, quantity);
        public int Inhabit(Population population, int quantity) => PopulationHousings.Inhabit(population, quantity);
        public int Abandon(Population population, int quantity) => PopulationHousings.Abandon(population, quantity);
        public void Kill(float ratio) => PopulationHousings.Kill(ratio);

        public override string GetDescription()
        {
            StringBuilder sb = new StringBuilder();

            foreach (var population in PopulationHousings)
            {
                sb.AppendLine($"{population.Population.Name}: {population.Quantity}/{population.Capacity}");
            }

            return sb.ToString();
        }

        #region Saving
        [Serializable]
        public class HousingData
        {
            public PopulationHousing.PopulationHousingData[] PopulationHousings;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new HousingData()
            {
                PopulationHousings = PopulationHousings.Select(p => p.SaveData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<HousingData>(json);

            for (int i = 0; i < data.PopulationHousings.Length; i++)
            {
                PopulationHousings[i].LoadData(data.PopulationHousings[i]);
            }
        }
        #endregion
    }
}