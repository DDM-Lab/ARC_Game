using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// contains population that could not be contained by downgraded housing, roams around randomly until housing is found or the range runs out
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_homeless_walker.html")]
    public class HomelessWalker : Walker
    {
        public enum HomelessWalkerState
        {
            Wandering = 0,
            Waiting = 10,
            Inhabiting = 20
        }

        [Tooltip("how many steps the walker takes while wandering before vanishing")]
        public int Range = 64;
        [Tooltip("maximum quantity of population the walker can take")]
        public int Capacity;

        public bool IsAssigned => _housing != null;

        private HomelessWalkerState _state;
        private int _quantity;
        private int _steps;
        private Population _population;
        private BuildingComponentReference<IHousing> _housing;

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            _state = HomelessWalkerState.Wandering;
        }

        public void StartHomelessing(int quantity, Population population)
        {
            _quantity = quantity;
            _population = population;

            reevaluate();
        }

        public void AssignHousing(BuildingComponentReference<IHousing> housing)
        {
            _housing = housing;
            _housing.Instance.Reserve(_population, _quantity);
        }

        private void reevaluate()
        {
            _steps++;

            if (_steps > Range)
            {
                onFinished();
                return;
            }

            if (_housing == null)
            {
                Wander(reevaluate);
            }
            else
            {
                tryInhabiting();
            }
        }

        private void tryInhabiting()
        {
            _state = HomelessWalkerState.Waiting;
            tryWalk(() => PathHelper.FindPathQuery(CurrentPoint, _housing.Instance.Building, PathType, PathTag), planned: () => _state = HomelessWalkerState.Inhabiting, finished: inhabit);
        }

        private void inhabit()
        {
            if (_housing?.HasInstance != true)
            {
                onFinished();
                return;
            }

            _quantity = _housing.Instance.Inhabit(_population, _quantity);

            if (_quantity <= 0)
            {
                onFinished();
            }
            else
            {
                _housing = null;
                reevaluate();
            }
        }

        #region Saving
        [Serializable]
        public class HomelessWalkerData
        {
            public WalkerData WalkerData;
            public int State;
            public int Quantity;
            public int Steps;
            public string Population;
            public BuildingComponentReferenceData Housing;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new HomelessWalkerData()
            {
                WalkerData = savewalkerData(),
                State = (int)_state,
                Quantity = _quantity,
                Steps = _steps,
                Population = _population.Key,
                Housing = _housing?.GetData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<HomelessWalkerData>(json);

            loadWalkerData(data.WalkerData);

            _state = (HomelessWalkerState)data.State;
            _quantity = data.Quantity;
            _steps = data.Steps;
            _population = Dependencies.Get<IKeyedSet<Population>>().GetObject(data.Population);
            _housing = data.Housing?.GetReference<IHousing>();

            switch (_state)
            {
                case HomelessWalkerState.Wandering:
                    ContinueWander(reevaluate);
                    break;
                case HomelessWalkerState.Waiting:
                    tryInhabiting();
                    break;
                case HomelessWalkerState.Inhabiting:
                    ContinueWalk(inhabit);
                    break;
                default:
                    break;
            }
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualHomelessWalkerSpawner : ManualWalkerSpawner<HomelessWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicHomelessWalkerSpawner : CyclicWalkerSpawner<HomelessWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledHomelessWalkerSpawner : PooledWalkerSpawner<HomelessWalker> { }
}