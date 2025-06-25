using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// sums up population count of the housings it passes so a <see cref="EmploymentWalkerComponent"/> can determine its access to employees
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_employment_walker.html")]
    public class EmploymentWalker : BuildingComponentWalker<IHousing>
    {
        public int Quantity => _quantity;

        private List<BuildingComponentReference<IHousing>> _enteredHousings;
        private Population[] _populations;
        private int _quantity;

        public void StartEmployment(IEnumerable<Population> populations)
        {
            _enteredHousings = new List<BuildingComponentReference<IHousing>>();
            _populations = populations.ToArray();
            _quantity = 0;
        }

        protected override void onComponentEntered(IHousing housing)
        {
            base.onComponentEntered(housing);

            if (_enteredHousings.Contains(housing.Reference))
                return;
            _enteredHousings.Add(housing.Reference);

            _quantity += _populations.Sum(p => housing.GetQuantity(p));
        }

        #region Saving
        [Serializable]
        public class EmploymentWalkerData : RoamingWalkerData
        {
            public string[] Populations;
            public int Quantity;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new EmploymentWalkerData()
            {
                WalkerData = savewalkerData(),
                State = (int)_state,
                Populations = _populations.Select(p => p.Key).ToArray(),
                Quantity = _quantity
            });
        }
        public override void LoadData(string json)
        {
            base.LoadData(json);

            var data = JsonUtility.FromJson<EmploymentWalkerData>(json);
            var populations = Dependencies.Get<IKeyedSet<Population>>();

            _populations = data.Populations.Select(k => populations.GetObject(k)).ToArray();
            _quantity = data.Quantity;
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualEmploymentWalkerSpawner : ManualWalkerSpawner<EmploymentWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicEmploymentWalkerSpawner : CyclicWalkerSpawner<EmploymentWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledEmploymentWalkerSpawner : PooledWalkerSpawner<EmploymentWalker> { }
}