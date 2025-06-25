using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walks new population into housing
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_immigration_walker.html")]
    public class ImmigrationWalker : Walker
    {
        [Tooltip("how much population quantity one walker can take")]
        public int Capacity;

        private BuildingComponentReference<IHousing> _housing;
        private Population _population;
        private int _quantity;

        public void StartImmigrating(BuildingComponentReference<IHousing> housing, WalkingPath path, Population population)
        {
            _housing = housing;
            _population = population;

            _quantity = Capacity - _housing.Instance.Reserve(population, Capacity);

            Walk(path);
        }

        protected override void onFinished()
        {
            if (_housing.HasInstance)
                _housing.Instance.Inhabit(_population, _quantity);

            base.onFinished();
        }

        #region Saving
        [Serializable]
        public class ImmigrationWalkerData
        {
            public WalkerData WalkerData;
            public BuildingComponentReferenceData Housing;
            public string Population;
            public int Quantity;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new ImmigrationWalkerData()
            {
                WalkerData = savewalkerData(),
                Housing = _housing.GetData(),
                Population = _population.Key,
                Quantity = _quantity
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<ImmigrationWalkerData>(json);

            loadWalkerData(data.WalkerData);

            _housing = data.Housing.GetReference<IHousing>();
            _population = Dependencies.Get<IKeyedSet<Population>>().GetObject(data.Population);
            _quantity = data.Quantity;

            ContinueWalk();
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualImmigrationWalkerSpawner : ManualWalkerSpawner<ImmigrationWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicImmigrationWalkerSpawner : CyclicWalkerSpawner<ImmigrationWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledImmigrationWalkerSpawner : PooledWalkerSpawner<ImmigrationWalker> { }
}