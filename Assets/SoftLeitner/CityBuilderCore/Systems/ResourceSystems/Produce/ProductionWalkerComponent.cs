using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        #endregion
    }
}