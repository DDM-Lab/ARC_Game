using System;
using System.Collections;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// periodically spawns <see cref="RetrieverWalkers"/> to get items from dispensers<br/>
    /// the retrieved items are either stored in global storage(set <see cref="Storage"/> to Global) or distributed using a <see cref="DeliveryWalker"/><br/>
    /// (eg hunter or lumberjack hut)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_items_retriever_component.html")]
    public class ItemsRetrieverComponent : BuildingComponent, IItemOwner
    {
        public override string Key => "ITR";

        [Tooltip("stores items until they are delivered(unless it just stores them globally)")]
        public ItemStorage Storage;

        [Tooltip("spawns walkers on an interval that get items from the closest dispenser")]
        public CyclicItemsRetrieverWalkerSpawner RetrieverWalkers;
        [Tooltip("optional walkers that deliver the dispensed items to a receiver")]
        public ManualDeliveryWalkerSpawner DeliveryWalkers;

        public bool HasDelivery => Storage.Mode != ItemStorageMode.Global && DeliveryWalkers.Prefab;

        public IItemContainer ItemContainer => Storage;

        private Coroutine _deliverRoutine;

        private void Awake()
        {
            RetrieverWalkers.InitializeRetrieving(Building, this, onFinished: walkerReturned);
            DeliveryWalkers.Initialize(Building);
        }
        private void Update()
        {
            if (Building.IsWorking)
                RetrieverWalkers.Update();
        }

        public override string GetDebugText() => Storage.GetDebugText();

        private void walkerReturned(ItemsRetrieverWalker walker)
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

        #region Saving
        [Serializable]
        public class ItemsRetrieverData
        {
            public ItemStorage.ItemStorageData Storage;
            public CyclicWalkerSpawnerData RetrieverSpawnerData;
            public ManualWalkerSpawnerData DeliverySpawnerData;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new ItemsRetrieverData()
            {
                Storage = Storage.SaveData(),
                RetrieverSpawnerData = RetrieverWalkers.SaveData(),
                DeliverySpawnerData = DeliveryWalkers.SaveData(),
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<ItemsRetrieverData>(json);

            Storage.LoadData(data.Storage);
            RetrieverWalkers.LoadData(data.RetrieverSpawnerData);
            DeliveryWalkers.LoadData(data.DeliverySpawnerData);

            if (Storage.HasItems() && HasDelivery)
                _deliverRoutine = StartCoroutine(deliver());
        }
        #endregion
    }
}
