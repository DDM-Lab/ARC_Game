using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;


namespace CityBuilderCore
{
    /// <summary>
    /// building component that periodically consumes and produces items<br/>
    /// production time is only started once the consumption items are all there<br/>
    /// consumption items have to be provided by others, produced items get shipped with <see cref="DeliveryWalker"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_production_walker_component.html")]
    public class ProductionWalkerComponent : ProductionComponent
    {
        [Tooltip("walkers that spawn with produced items and try to deliver items to receivers")]
        public ManualDeliveryWalkerSpawner DeliveryWalkers;

        private Dictionary<ItemProducer, Coroutine> _deliveryRoutines = new Dictionary<ItemProducer, Coroutine>();

        protected override void Awake()
        {
            base.Awake();

            DeliveryWalkers.Initialize(Building);
        }

        protected override void onItemsChanged()
        {
            base.onItemsChanged();

            foreach (var itemsProducer in ItemsProducers)
            {
                if (!itemsProducer.HasItem)
                    continue;

                if (_deliveryRoutines.ContainsKey(itemsProducer))
                    continue;

                _deliveryRoutines.Add(itemsProducer, StartCoroutine(deliver(itemsProducer)));
            }
        }
        private IEnumerator deliver(ItemProducer itemsProducer)
        {
            yield return null;

            while (itemsProducer.HasItem)
            {
                tryDeliver(itemsProducer);
                yield return new WaitForSeconds(1f);
            }

            _deliveryRoutines.Remove(itemsProducer);
        }
        private void tryDeliver(ItemProducer itemsProducer)
        {
            if (!DeliveryWalkers.HasWalker)
                return;

            if (!Building.IsWorking)
                return;

            var accessPoint = Building.GetAccessPoint(DeliveryWalkers.Prefab.PathType, DeliveryWalkers.Prefab.PathTag);
            if (!accessPoint.HasValue)
                return;

            DeliveryWalkers.StartDeliver(this, itemsProducer.Storage, accessPoint);
        }

        #region Saving
        [Serializable]
        public class ProductionWalkerData : ProductionData
        {
            public ManualWalkerSpawnerData SpawnerData;
        }

        public override string SaveData()
        {
            var data = new ProductionWalkerData();

            saveData(data);

            data.SpawnerData = DeliveryWalkers.SaveData();

            return JsonUtility.ToJson(data);
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<ProductionWalkerData>(json);

            loadData(data);

            DeliveryWalkers.LoadData(data.SpawnerData);
        }

        /*public void TriggerDeliveryCheck()
        {
            onItemsChanged(); // for starting delivery coroutines
        }
        public void RestartDeliveryRoutine()
        {
            foreach (var itemProducer in ItemsProducers)
            {
                // Always restart, even if currently no item, to trigger future deliveries
                if (_deliveryRoutines.ContainsKey(itemProducer))
                {
                    StopCoroutine(_deliveryRoutines[itemProducer]);
                    _deliveryRoutines.Remove(itemProducer);
                }

                _deliveryRoutines.Add(itemProducer, StartCoroutine(deliver(itemProducer)));
                Debug.Log($"[ProductionWalker] 🌀 Restarted delivery coroutine");
            }
        }
        public void ForceTryDelivery()
        {
            if (DeliveryWalkers == null || DeliveryWalkers.Prefab == null)
                return;

            foreach (var producer in ItemsProducers)
            {
                if (!producer.HasItem)
                    continue;

                var firstItem = producer.Storage.GetItemQuantities().FirstOrDefault();
                if (firstItem.Item == null || firstItem.Quantity <= 0)
                    continue;

                foreach (var shelterBuilding in GameDatabase.Instance.GetAllShelters())
                {
                    if (!shelterBuilding.TryGetComponent<StorageComponent>(out var shelterStorage))
                        continue;

                    // Check if this shelter still needs the item
                    var remainingCapacity = shelterStorage.GetReceiveCapacityRemaining(firstItem.Item);
                    if (remainingCapacity <= 0)
                        continue;

                    var path = DeliveryWalkers.Prefab.GetReceiverPath(
                        firstItem,
                        Building,
                        Building.GetAccessPoint(DeliveryWalkers.Prefab.PathType, DeliveryWalkers.Prefab.PathTag)
                    );

                    if (path == null)
                        continue;

                    DeliveryWalkers.Spawn(w =>
                    {
                        w.StartDelivery(producer.Storage, firstItem.Item, path);
                        Debug.Log($"[Kitchen] 🍱 Delivering {firstItem.Item.Key} to {shelterBuilding.name}");
                    }, Building.GetAccessPoint(DeliveryWalkers.Prefab.PathType, DeliveryWalkers.Prefab.PathTag));
                }
            }
        }*/
        public void TryRestartDelivery()
        {
            foreach (var itemsProducer in ItemsProducers)
            {
                if (!itemsProducer.HasItem)
                    continue;

                // Skip if already delivering
                if (_deliveryRoutines.ContainsKey(itemsProducer))
                    continue;

                _deliveryRoutines.Add(itemsProducer, StartCoroutine(deliver(itemsProducer)));

                Debug.Log($"[ProductionWalker] ♻️ Retried delivery");
            }
        }

        #endregion
    }

    

}