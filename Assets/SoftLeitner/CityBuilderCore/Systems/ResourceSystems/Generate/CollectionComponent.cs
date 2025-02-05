using System;
using System.Collections;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that spawns walkers that collect items from <see cref="IGenerationComponent"/><br/>
    /// the collected items are either stored in global storage(set <see cref="Storage"/> to Global) or distributed using a <see cref="DeliveryWalker"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_collection_component.html")]
    public class CollectionComponent : BuildingComponent, IItemGiver
    {
        public override string Key => "COL";

        [Tooltip("stores items until they are delivered or someone gets them through giving")]
        public ItemStorage Storage;

        [Tooltip("spawns roaming walkers on an interval that collect items from generators they come across")]
        public CyclicCollectionWalkerSpawner CollectionWalkers;
        [Tooltip("optional walkers that deliver the collected items to a fitting receiver")]
        public ManualDeliveryWalkerSpawner DeliveryWalkers;

        public bool HasDelivery => Storage.Mode != ItemStorageMode.Global && DeliveryWalkers.Prefab;

        public BuildingComponentReference<IItemGiver> Reference { get; set; }
        public IItemContainer ItemContainer => Storage;

        private Coroutine _deliverRoutine;

        private void Awake()
        {
            CollectionWalkers.Initialize(Building, onFinished: walkerReturning);
            DeliveryWalkers.Initialize(Building);
        }
        private void Update()
        {
            if (Building.IsWorking)
                CollectionWalkers.Update();
        }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<IItemGiver>(this);
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            replaceTrait<IItemGiver>(this, replacement.GetBuildingComponent<CollectionComponent>());
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IItemGiver>(this);
        }

        public override string GetDebugText() => Storage.GetDebugText();

        private void walkerReturning(CollectionWalker walker)
        {
            walker.Storage.MoveItemsTo(Storage);

            if (_deliverRoutine == null && HasDelivery)
                _deliverRoutine = StartCoroutine(deliver());
        }

        private IEnumerator deliver()
        {
            yield return null;

            while (Storage.HasItems())
            {
                tryDeliver();
                yield return new WaitForSeconds(1f);
            }

            _deliverRoutine = null;
        }
        private void tryDeliver()
        {
            if (!DeliveryWalkers.HasWalker)
                return;

            if (!Building.IsWorking)
                return;

            var accessPoint = Building.GetAccessPoint(DeliveryWalkers.Prefab.PathType, DeliveryWalkers.Prefab.PathTag);
            if (!accessPoint.HasValue)
                return;

            DeliveryWalkers.StartDeliver(this, Storage, accessPoint);
        }

        public int GetGiveQuantity(Item item) => Storage.GetItemQuantityRemaining(item);
        public void ReserveQuantity(Item item, int quantity) => Storage.ReserveQuantity(item, quantity);
        public void UnreserveQuantity(Item item, int quantity) => Storage.UnreserveQuantity(item, quantity);
        public int Give(ItemStorage storage, Item item, int quantity) => quantity - Storage.MoveItemsTo(storage, item, quantity);

        #region Saving
        [Serializable]
        public class CollectionData
        {
            public ItemStorage.ItemStorageData Storage;
            public CyclicWalkerSpawnerData CollectionSpawnerData;
            public ManualWalkerSpawnerData DeliverySpawnerData;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new CollectionData()
            {
                Storage = Storage.SaveData(),
                CollectionSpawnerData = CollectionWalkers.SaveData(),
                DeliverySpawnerData = DeliveryWalkers.SaveData(),
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<CollectionData>(json);

            Storage.LoadData(data.Storage);
            CollectionWalkers.LoadData(data.CollectionSpawnerData);
            DeliveryWalkers.LoadData(data.DeliverySpawnerData);

            if (Storage.HasItems() && HasDelivery)
                _deliverRoutine = StartCoroutine(deliver());

        }
        #endregion
    }
}