using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that periodically consumes and produces items<br/>
    /// production time is only started once the consumption items are all there<br/>
    /// does not take care of item logistics
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_production_component.html")]
    public class ProductionComponent : ProgressComponent, IProductionComponent
    {
        public override string Key => "PRD";

        public enum ProductionState
        {
            Idle = 0,//waiting for raw materials in consumers
            Working = 10,//progress going up according to efficiency
            Done = 20//waiting for producers to deposit goods
        }

        [Tooltip("one for each item that is needed for production and will be consumed")]
        public ItemConsumer[] ItemsConsumers;
        [Tooltip("one for each item that is produced")]
        public ItemProducer[] ItemsProducers;

        [Tooltip("fired whenever items change, parameter is whether raw materials are in storage")]
        public BoolEvent HasRawMaterialsChanged;
        [Tooltip("fired whenever items change, parameter is whether any products are in storage")]
        public BoolEvent HasProductsChanged;

        public int Priority => 1000;
        public ItemConsumer[] Consumers => ItemsConsumers;
        public ItemProducer[] Producers => ItemsProducers;

        public BuildingComponentReference<IItemReceiver> Reference { get; set; }
        public IItemContainer ItemContainer { get; private set; }

        public virtual bool HasRawMaterials => ItemsConsumers.All(c => c.HasItems);
        public virtual bool HasProducts => ItemsProducers.Any(p => p.HasItem);
        public ProductionState CurrentProductionState => _productionState;

        protected bool _isProgressing;
        protected ProductionState _productionState;
        private HashSet<Item> _receiveItems;

        protected virtual void Awake()
        {
            ItemContainer = new SplitItemContainer(ItemsConsumers.Select(i => i.Storage).Union(ItemsProducers.Select(i => i.Storage)), i => getConsumer(i)?.Storage ?? getProducer(i)?.Storage);
        }
        protected virtual void Start()
        {
            onItemsChanged();
        }
        protected virtual void Update()
        {
            updateProduction();
        }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<IItemReceiver>(this);
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var productionReplacement = replacement.GetBuildingComponent<IProductionComponent>();

            replaceTrait<IItemReceiver>(this, productionReplacement);

            if (productionReplacement == null)
                return;

            foreach (var itemsConsumer in ItemsConsumers)
            {
                itemsConsumer.Storage.AddItems(itemsConsumer.Items.Item, productionReplacement.ItemContainer.GetItemQuantity(itemsConsumer.Items.Item));
            }

            foreach (var itemsProducer in ItemsProducers)
            {
                itemsProducer.Storage.AddItems(itemsProducer.Items.Item, productionReplacement.ItemContainer.GetItemQuantity(itemsProducer.Items.Item));
            }
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IItemReceiver>(this);
        }

        public IEnumerable<Item> GetReceiveItems()
        {
            if (_receiveItems == null)
                _receiveItems = new HashSet<Item>(Consumers.Select(c => c.Items.Item));
            return _receiveItems;
        }
        public int GetReceiveCapacityRemaining(Item item)
        {
            var consumer = getConsumer(item);
            if (consumer == null)
                return 0;
            return consumer.Storage.GetItemCapacityRemaining(item);
        }
        public void ReserveCapacity(Item item, int amount)
        {
            getConsumer(item)?.Storage.ReserveCapacity(item, amount);
        }
        public void UnreserveCapacity(Item item, int amount)
        {
            getConsumer(item)?.Storage.UnreserveCapacity(item, amount);
        }
        public int Receive(ItemStorage storage, Item item, int quantity)
        {
            var consumer = getConsumer(item);
            if (consumer == null)
                return quantity;

            var remaining = quantity - storage.MoveItemsTo(consumer.Storage, item, quantity);

            onItemsChanged();

            return remaining;
        }

        public IEnumerable<ItemLevel> GetNeededItems() => Consumers.Select(c => c.ItemLevel);
        public IEnumerable<ItemLevel> GetProducedItems() => Producers.Select(c => c.ItemLevel);

        protected virtual void updateProduction()
        {
            switch (_productionState)
            {
                case ProductionState.Idle:
                    if (ItemsConsumers.All(c => c.HasItems) && canWork())
                    {
                        setState(ProductionState.Working);
                    }
                    break;
                case ProductionState.Working:
                    bool isProgressing = Building.Efficiency > 0f;
                    if (_isProgressing != isProgressing)
                    {
                        _isProgressing = isProgressing;
                        IsProgressing?.Invoke(_isProgressing);
                    }

                    if (addProgress(Building.Efficiency))
                    {
                        foreach (var consumer in ItemsConsumers)
                        {
                            consumer.Consume();
                        }

                        setState(ProductionState.Done);
                        _isProgressing = false;
                        IsProgressing?.Invoke(false);
                    }
                    break;
                case ProductionState.Done:
                    if (canProduce())
                    {
                        produce();
                        setState(ProductionState.Idle);
                        resetProgress();
                    }
                    break;
                default:
                    break;
            }
        }

        protected virtual void setState(ProductionState productionState)
        {
            _productionState = productionState;
        }

        protected virtual bool canWork()
        {
            return true;
        }
        protected virtual bool canProduce()
        {
            return ItemsProducers.All(p => p.FitsItems);
        }
        protected virtual void produce()
        {
            foreach (var itemProducer in ItemsProducers)
            {
                itemProducer.Produce();
                onProduced(itemProducer);
            }

            onItemsChanged();
        }
        protected virtual void onProduced(ItemProducer itemProducer)
        {
        }

        protected ItemConsumer getConsumer(Item item) => ItemsConsumers.FirstOrDefault(c => c.Items.Item == item);
        protected ItemProducer getProducer(Item item) => ItemsProducers.FirstOrDefault(c => c.Items.Item == item);

        protected virtual void onItemsChanged()
        {
            HasRawMaterialsChanged?.Invoke(HasRawMaterials);
            HasProductsChanged?.Invoke(HasProducts);
        }

        #region Saving
        [Serializable]
        public class ProductionData
        {
            public int State;
            public float ProductionTime;
            public ItemStorage.ItemStorageData[] Consumers;
            public ItemStorage.ItemStorageData[] Producers;
        }

        public override string SaveData()
        {
            var data = new ProductionData();

            saveData(data);

            return JsonUtility.ToJson(data);
        }
        public override void LoadData(string json)
        {
            loadData(JsonUtility.FromJson<ProductionData>(json));
        }

        protected void saveData(ProductionData data)
        {
            data.State = (int)_productionState;
            data.ProductionTime = _progressTime;
            data.Consumers = ItemsConsumers.Select(c => c.Storage.SaveData()).ToArray();
            data.Producers = ItemsProducers.Select(c => c.Storage.SaveData()).ToArray();
        }
        protected void loadData(ProductionData data)
        {
            _productionState = (ProductionState)data.State;
            _progressTime = data.ProductionTime;
            for (int i = 0; i < ItemsConsumers.Length; i++)
            {
                ItemsConsumers[i].Storage.LoadData(data.Consumers[i]);
            }
            for (int i = 0; i < ItemsProducers.Length; i++)
            {
                ItemsProducers[i].Storage.LoadData(data.Producers[i]);
            }

            onItemsChanged();
        }
        #endregion
    }
}