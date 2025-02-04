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
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_variant_production_walker_component.html")]
    public class VariantProductionWalkerComponent : VariantProductionComponent
    {
        [Tooltip("walkers that spawn with produced items and try to deliver items to receivers")]
        public ManualDeliveryWalkerSpawner DeliveryWalkers;

        private Dictionary<Item, Coroutine> _deliveryRoutines = new Dictionary<Item, Coroutine>();

        protected override void Awake()
        {
            base.Awake();

            DeliveryWalkers.Initialize(Building);
        }

        protected override void onItemsChanged()
        {
            base.onItemsChanged();

            foreach (var item in GetItemsOut())
            {
                if (!Storage.HasItem(item))
                    continue;

                if (_deliveryRoutines.ContainsKey(item))
                    continue;

                _deliveryRoutines.Add(item, StartCoroutine(deliver(item)));
            }
        }

        private IEnumerator deliver(Item item)
        {
            yield return null;

            while (Storage.HasItem(item))
            {
                tryDeliver(item);
                yield return new WaitForSeconds(1f);
            }

            _deliveryRoutines.Remove(item);
        }

        private void tryDeliver(Item item)
        {
            if (!DeliveryWalkers.HasWalker)
                return;

            if (!Building.IsWorking)
                return;

            var accessPoint = Building.GetAccessPoint(DeliveryWalkers.Prefab.PathType, DeliveryWalkers.Prefab.PathTag);
            if (!accessPoint.HasValue)
                return;

            DeliveryWalkers.StartDeliver(this, Storage, new ItemQuantity(item, Storage.GetItemQuantity(item)), accessPoint);
        }

        #region Saving
        [Serializable]
        public class VariantProductionWalkerData : VariantProductionData
        {
            public ManualWalkerSpawnerData SpawnerData;
        }

        public override string SaveData()
        {
            var data = new VariantProductionWalkerData();

            saveData(data);

            data.SpawnerData = DeliveryWalkers.SaveData();

            return JsonUtility.ToJson(data);
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<VariantProductionWalkerData>(json);

            loadData(data);

            DeliveryWalkers.LoadData(data.SpawnerData);
        }
        #endregion
    }
}