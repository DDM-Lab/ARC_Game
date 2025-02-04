using CityBuilderCore;
using System;
using UnityEngine;

namespace CityBuilderUrban
{
    /// <summary>
    /// drives to the trains station, signals the station to spawn trucks and then drives to the exit
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/urban">https://citybuilder.softleitner.com/manual/urban</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_urban_1_1_train_walker.html")]
    public class TrainWalker : Walker
    {
        private int _state = 0;

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, Dependencies.Get<UrbanManager>().GetRailwayEntry());

            Enter();
        }

        public void Enter()
        {
            _state = 0;
            tryWalk(Home.Instance, finished: Exit);
        }

        public void Exit()
        {
            Home.Instance.GetBuildingComponent<RailwayComponent>().TrainArrival();
            _state = 1;
            tryWalk(Dependencies.Get<UrbanManager>().GetRailwayExit());
        }

        #region Saving
        [Serializable]
        public class TrainWalkerData
        {
            public WalkerData WalkerData;
            public int State;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new TrainWalkerData()
            {
                WalkerData = savewalkerData(),
                State = (int)_state,
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<TrainWalkerData>(json);

            loadWalkerData(data.WalkerData);

            _state = data.State;

            switch (_state)
            {
                case 0:
                    ContinueWalk(Exit);
                    break;
                case 1:
                    ContinueWalk();
                    break;
            }
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicTrainWalkerSpawner : CyclicWalkerSpawner<TrainWalker> { }
}
