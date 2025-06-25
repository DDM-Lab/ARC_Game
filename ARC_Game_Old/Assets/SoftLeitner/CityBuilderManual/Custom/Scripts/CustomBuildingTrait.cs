using CityBuilderCore;
using System;
using UnityEngine;

namespace CityBuilderManual.Custom
{
    public class CustomBuildingTrait : BuildingComponent, ICustomBuildingTrait
    {
        public override string Key => "CTR";

        public CyclicCustomRoamingWalkerSpawner CustomRoamingWalkers;
        public CyclicCustomDestinationWalkerSpawner CustomDestinationWalkers;
        public float CustomValue => 1;

        public BuildingComponentReference<ICustomBuildingTrait> Reference { get; set; }

        private void Awake()
        {
            CustomRoamingWalkers.Initialize(Building);
            CustomDestinationWalkers.Initialize(Building, destinationWalkerSpawning);
        }
        private void Update()
        {
            if (Building.IsWorking)
            {
                CustomRoamingWalkers.Update();
                CustomDestinationWalkers.Update();
            }
        }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<ICustomBuildingTrait>(this);

            Dependencies.Get<ICustomManager>().Add(Reference);
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var replacementTrait = replacement.GetBuildingComponent<ICustomBuildingTrait>();

            replaceTrait(this, replacementTrait);
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<ICustomBuildingTrait>(this);

            Dependencies.Get<ICustomManager>().Remove(Reference);
        }

        public void DoSomething()
        {
            Debug.Log("Hello!");
        }

        private bool destinationWalkerSpawning(CustomDestinationWalker walker)
        {
            var path = Dependencies.Get<ICustomManager>().GetPath(Building.BuildingReference, walker.PathType, walker.PathTag);
            if (path == null)
                return false;
            walker.StartWalker(path);
            return true;
        }

        #region Saving
        [Serializable]
        public class CustomTraitData
        {
            public CyclicWalkerSpawnerData RoamingWalkersData;
            public CyclicWalkerSpawnerData DestinationWalkersData;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new CustomTraitData()
            {
                RoamingWalkersData = CustomRoamingWalkers.SaveData(),
                DestinationWalkersData = CustomDestinationWalkers.SaveData()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<CustomTraitData>(json);

            CustomRoamingWalkers.LoadData(data.RoamingWalkersData);
            CustomDestinationWalkers.LoadData(data.DestinationWalkersData);
        }
        #endregion
    }
}
