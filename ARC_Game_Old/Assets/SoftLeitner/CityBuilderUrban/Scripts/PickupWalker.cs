using CityBuilderCore;
using System;
using UnityEngine;

namespace CityBuilderUrban
{
    /// <summary>
    /// can be spawned from a house to drive directly to a shop, purchase and then drive back
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/urban">https://citybuilder.softleitner.com/manual/urban</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_urban_1_1_pickup_walker.html")]
    public class PickupWalker : Walker
    {
        public ItemQuantity Items;

        public enum CustomDestinationWalkerState
        {
            Inactive = 0,
            Walking = 1,
            Returning = 2
        }

        private CustomDestinationWalkerState _state = CustomDestinationWalkerState.Inactive;
        private BuildingComponentReference<ShopComponent> _target;

        public void StartWalker(BuildingComponentPath<ShopComponent> customPath)
        {
            _state = CustomDestinationWalkerState.Walking;
            _target = customPath.Component;
            Walk(customPath.Path,purchase);
        }

        private void purchase()
        {
            if (_target.HasInstance)
                _target.Instance.Purchase(Items);

            if (Walk(Home.Instance))
                _state = CustomDestinationWalkerState.Returning;
            else
                onFinished();
        }

        protected override void onFinished()
        {
            _state = CustomDestinationWalkerState.Inactive;
            base.onFinished();
        }

        #region Saving
        [Serializable]
        public class PickupWalkerData
        {
            public WalkerData WalkerData;
            public int State;
            public BuildingComponentReferenceData Target;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new PickupWalkerData()
            {
                WalkerData = savewalkerData(),
                State = (int)_state,
                Target = _target.GetData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<PickupWalkerData>(json);

            loadWalkerData(data.WalkerData);

            _state = (CustomDestinationWalkerState)data.State;
            _target = data.Target.GetReference<ShopComponent>();

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
    public class CyclicPickupWalkerSpawner : CyclicWalkerSpawner<PickupWalker> { }
}