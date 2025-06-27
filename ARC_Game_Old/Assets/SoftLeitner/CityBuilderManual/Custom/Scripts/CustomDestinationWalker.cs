using CityBuilderCore;
using System;
using UnityEngine;

namespace CityBuilderManual.Custom
{
    /// <summary>
    /// walks along a passed path and then signals its target
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/custom">https://citybuilder.softleitner.com/manual/custom</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_manual._custom_1_1_custom_destination_walker.html")]
    public class CustomDestinationWalker : Walker
    {
        public enum CustomDestinationWalkerState
        {
            Inactive = 0,
            Walking = 1
        }

        private CustomDestinationWalkerState _state = CustomDestinationWalkerState.Inactive;
        private BuildingComponentReference<ICustomBuildingTrait> _target;

        public void StartWalker(BuildingComponentPath<ICustomBuildingTrait> customPath)
        {
            _state = CustomDestinationWalkerState.Walking;
            _target = customPath.Component;
            Walk(customPath.Path);
        }

        protected override void onFinished()
        {
            if (_target.HasInstance)
                _target.Instance.DoSomething();

            base.onFinished();
        }

        #region Saving
        [Serializable]
        public class CustomDestinationWalkerData
        {
            public WalkerData WalkerData;
            public int State;
            public BuildingComponentReferenceData Target;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new CustomDestinationWalkerData()
            {
                WalkerData = savewalkerData(),
                State = (int)_state,
                Target = _target.GetData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<CustomDestinationWalkerData>(json);

            loadWalkerData(data.WalkerData);

            _state = (CustomDestinationWalkerState)data.State;
            _target = data.Target.GetReference<ICustomBuildingTrait>();

            switch (_state)
            {
                case CustomDestinationWalkerState.Walking:
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
    public class ManualCustomDestinationWalkerSpawner : ManualWalkerSpawner<CustomDestinationWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicCustomDestinationWalkerSpawner : CyclicWalkerSpawner<CustomDestinationWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledCustomDestinationWalkerSpawner : PooledWalkerSpawner<CustomDestinationWalker> { }
}