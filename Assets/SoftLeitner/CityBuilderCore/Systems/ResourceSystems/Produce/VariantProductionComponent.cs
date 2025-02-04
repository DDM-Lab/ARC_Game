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
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_variant_production_component.html")]
    public class VariantProductionComponent : BuildingComponent, IProductionComponent, IProgressComponent
    {
        public override string Key => "VPRD";

        public enum ProductionState
        {
            Idle = 0,//waiting for raw materials in consumers
            Working = 10,//progress going up according to efficiency
            Done = 20//waiting for producers to deposit goods
        }

        [Serializable]
        public class Variant
        {
            public float Duration;
            public ItemQuantity[] In;
            public ItemCategoryQuantity[] InCategories;
            public ItemQuantity[] Out;

            public bool HasIn(ItemStorage itemStorage)
            {
                if (In != null && !itemStorage.HasItems(In))
                    return false;
                if (InCategories != null && !itemStorage.HasItems(InCategories))
                    return false;
                return true;
            }

            public IEnumerable<Item> GetInItems()
            {
                if (In != null)
                {
                    foreach (var itemQuantity in In)
                    {
                        yield return itemQuantity.Item;
                    }
                }

                if (InCategories != null)
                {
                    foreach (var itemCategoryQuantity in InCategories)
                    {
                        foreach (var item in itemCategoryQuantity.ItemCategory.Items)
                        {
                            yield return item;
                        }
                    }
                }
            }

            public IEnumerable<Item> GetOutItems()
            {
                if (Out != null)
                {
                    foreach (var itemQuantity in Out)
                    {
                        yield return itemQuantity.Item;
                    }
                }
            }
        }

        [Tooltip("storage that contains both raw materials and finished products")]
        public ItemStorage Storage;
        [Tooltip("the different variants the components can produce, if multiple variants can be produced the first one in the list is chosen")]
        public Variant[] Variants;
        [Tooltip("can be used to force a certain variant or stop production when set above variant count, -1 to produce the first possible variant")]
        public int VariantIndex = -1;

        [Tooltip("fired whenever items change, parameter is whether raw materials are in storage")]
        public BoolEvent HasRawMaterialsChanged;
        [Tooltip("fired whenever items change, parameter is whether any products are in storage")]
        public BoolEvent HasProductsChanged;
        [Tooltip("fired whenever the component starts or stops progressing")]
        public BoolEvent IsProgressingChanged;

        public event Action ProgressReset;
        public event Action<float> ProgressChanged;

        public float ProgressInterval => _productionVariant == null ? 0f : _productionVariant.Duration;
        public float Progress => _productionVariant == null ? 0f : _progressTime / _productionVariant.Duration;
        public int Priority => 1000;

        public BuildingComponentReference<IItemReceiver> Reference { get; set; }
        public IItemContainer ItemContainer => Storage;

        public virtual bool HasRawMaterials => Variants.Any(v => v.HasIn(Storage));
        public virtual bool HasProducts => Variants.Any(v => v.Out.Any(i => Storage.HasItem(i.Item)));

        protected bool _isProgressing;
        protected ProductionState _productionState;
        protected Variant _productionVariant;
        protected float _progressTime;
        private HashSet<Item> _receiveItems;

        protected virtual void Awake()
        {

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

            foreach (var i in productionReplacement.ItemContainer.GetItemQuantities())
            {
                Storage.AddItems(i.Item, i.Quantity);
            }
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IItemReceiver>(this);
        }

        public override string GetDebugText()
        {
            if (_productionVariant == null)
                return "0%";
            else if (_productionVariant.Out.Length > 0)
                return _productionVariant.Out[0].Item.Key + " - " + (Progress * 100f).ToString("F0") + "%";
            else
                return (Progress * 100f).ToString("F0") + "%";
        }

        public IEnumerable<Item> GetItemsIn() => Variants.SelectMany(v => v.GetInItems()).Distinct();
        public IEnumerable<Item> GetItemsOut() => Variants.SelectMany(v => v.GetOutItems()).Distinct();

        public IEnumerable<Item> GetReceiveItems()
        {
            if (_receiveItems == null)
                _receiveItems = new HashSet<Item>(GetItemsIn());
            return _receiveItems;
        }
        public int GetReceiveCapacityRemaining(Item item) => Storage.GetItemCapacityRemaining(item);
        public void ReserveCapacity(Item item, int amount) => Storage.ReserveCapacity(item, amount);
        public void UnreserveCapacity(Item item, int amount) => Storage.UnreserveCapacity(item, amount);
        public int Receive(ItemStorage storage, Item item, int quantity) => quantity - storage.MoveItemsTo(Storage, item, quantity);

        public IEnumerable<ItemLevel> GetNeededItems() => GetItemsIn().Select(i => Storage.GetItemLevel(i));
        public IEnumerable<ItemLevel> GetProducedItems() => GetItemsOut().Select(i => Storage.GetItemLevel(i));

        protected virtual void updateProduction()
        {
            switch (_productionState)
            {
                case ProductionState.Idle:
                    if (canWork())
                    {
                        if (VariantIndex >= 0)
                        {
                            if (VariantIndex < Variants.Length)
                            {
                                if (Variants[VariantIndex].HasIn(Storage))
                                {
                                    _productionVariant = Variants[VariantIndex];
                                    _productionState = ProductionState.Working;
                                }
                            }
                            else
                            {
                                //variant index set above actual variants > no production
                            }
                        }
                        else
                        {
                            foreach (var variant in Variants)
                            {
                                if (variant.HasIn(Storage))
                                {
                                    _productionVariant = variant;
                                    _productionState = ProductionState.Working;
                                    break;
                                }
                            }
                        }
                    }
                    break;
                case ProductionState.Working:
                    bool isProgressing = Building.Efficiency > 0f;
                    if (_isProgressing != isProgressing)
                    {
                        _isProgressing = isProgressing;
                        IsProgressingChanged?.Invoke(_isProgressing);
                    }

                    if (addProgress(Building.Efficiency))
                    {
                        Storage.RemoveItems(_productionVariant.In);
                        Storage.RemoveItems(_productionVariant.InCategories);

                        setState(ProductionState.Done);
                        _isProgressing = false;
                        IsProgressingChanged?.Invoke(false);
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
            return Storage.FitsItems(_productionVariant.Out);
        }
        protected virtual void produce()
        {
            Storage.AddItems(_productionVariant.Out);

            onItemsChanged();
        }

        protected virtual void onItemsChanged()
        {
            HasRawMaterialsChanged?.Invoke(HasRawMaterials);
            HasProductsChanged?.Invoke(HasProducts);
        }

        protected bool addProgress(float multiplier)
        {
            _progressTime = Mathf.Min(ProgressInterval, _progressTime + Time.deltaTime * multiplier);
            ProgressChanged?.Invoke(Progress);
            return _progressTime >= ProgressInterval;
        }
        protected void resetProgress()
        {
            _progressTime = 0f;
            ProgressReset?.Invoke();
            ProgressChanged?.Invoke(Progress);
        }

        #region Saving
        [Serializable]
        public class VariantProductionData
        {
            public int State;
            public int Variant;
            public float ProductionTime;
            public ItemStorage.ItemStorageData Storage;
        }

        public override string SaveData()
        {
            var data = new VariantProductionData();

            saveData(data);

            return JsonUtility.ToJson(data);
        }
        public override void LoadData(string json)
        {
            loadData(JsonUtility.FromJson<VariantProductionData>(json));
        }

        protected void saveData(VariantProductionData data)
        {
            data.State = (int)_productionState;
            data.ProductionTime = _progressTime;
            data.Variant = _productionVariant == null ? -1 : Array.IndexOf(Variants, _productionVariant);
            data.Storage = Storage.SaveData();
        }
        protected void loadData(VariantProductionData data)
        {
            _productionState = (ProductionState)data.State;
            _progressTime = data.ProductionTime;
            _productionVariant = data.Variant == -1 ? null : Variants[data.Variant];

            Storage.LoadData(data.Storage);

            onItemsChanged();
        }
        #endregion
    }
}