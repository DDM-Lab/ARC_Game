using System;

namespace CityBuilderCore
{
    /// <summary>
    /// reference and path to a workers workplace<br/>
    /// may include a path to an itemGiver to get supplies needed for the work
    /// </summary>
    public class WorkerPath
    {
        public BuildingComponentReference<IWorkerUser> WorkerUser { get; private set; }
        public WalkingPath PlacePath { get; private set; }

        public BuildingComponentReference<IItemGiver> Giver { get; private set; }
        public ItemQuantity Items { get; private set; }
        public WalkingPath SupplyPath { get; private set; }

        private WorkerPath() { }
        public WorkerPath(BuildingComponentReference<IWorkerUser> workerUser, WalkingPath place)
        {
            WorkerUser = workerUser;
            PlacePath = place;
        }

        public void AddSupply(BuildingComponentReference<IItemGiver> giver, ItemQuantity items, WalkingPath supplyPath)
        {
            Giver = giver;
            Items = items;
            SupplyPath = supplyPath;
        }

        #region Saving
        [Serializable]
        public class WorkerPathData
        {
            public BuildingComponentReferenceData WorkerUser;
            public WalkingPath.WalkingPathData PlacePath;

            public BuildingComponentReferenceData Giver;
            public ItemQuantity.ItemQuantityData Items;
            public WalkingPath.WalkingPathData SupplyPath;
        }

        public WorkerPathData GetData() => new WorkerPathData()
        {
            WorkerUser = WorkerUser?.GetData(),
            PlacePath = PlacePath?.GetData(),
            Giver = Giver?.GetData(),
            Items = Items?.GetData(),
            SupplyPath = SupplyPath?.GetData()
        };
        public static WorkerPath FromData(WorkerPathData data) => new WorkerPath()
        {
            WorkerUser = data.WorkerUser?.GetReference<IWorkerUser>(),
            PlacePath = WalkingPath.FromData(data.PlacePath),
            Giver = data.Giver?.GetReference<IItemGiver>(),
            Items = ItemQuantity.FromData(data.Items),
            SupplyPath = WalkingPath.FromData(data.SupplyPath)
        };
        #endregion
    }
}