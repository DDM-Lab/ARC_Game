using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that uses employees to influence the buildings efficiency<br/>
    /// sends out a walker that determines how good the access to employees is<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_employment_walker_component.html")]
    public class EmploymentWalkerComponent : EmploymentComponent
    {
        public override string Key => "EMW";

        [Tooltip("how much population quantity the walker has to find for full access")]
        public int SufficientPopulationCount;
        [Tooltip("walkers sent out looking for population in housing to determine the employee access")]
        public CyclicEmploymentWalkerSpawner EmploymentWalkers;

        private float _populationFactor = 0;

        private void Awake()
        {
            EmploymentWalkers.Initialize(Building, walkerLeaving, walkerReturned);
        }
        private void Update()
        {
            EmploymentWalkers.Update();
        }

        private bool walkerLeaving(EmploymentWalker walker)
        {
            walker.StartEmployment(PopulationEmployments.Select(p => p.Population));
            return true;
        }
        private void walkerReturned(EmploymentWalker walker)
        {
            if (SufficientPopulationCount == 0)
                _populationFactor = 1f;
            else
                _populationFactor = Mathf.Min(1f, (float)walker.Quantity / SufficientPopulationCount);
        }

        public override int AssignAvailable(EmploymentGroup employmentGroup, Population population, int quantity)
        {
            int assignableQuantity = (int)(quantity * _populationFactor);

            return base.AssignAvailable(employmentGroup, population, assignableQuantity) + (quantity - assignableQuantity);
        }

        #region Saving
        [Serializable]
        public class EmploymentWalkerData
        {
            public CyclicWalkerSpawnerData EmploymentWalkers;
            public float PopulationFactor;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new EmploymentWalkerData()
            {
                EmploymentWalkers = EmploymentWalkers.SaveData(),
                PopulationFactor = _populationFactor
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<EmploymentWalkerData>(json);

            EmploymentWalkers.LoadData(data.EmploymentWalkers);
            _populationFactor = data.PopulationFactor;
        }
        #endregion
    }
}