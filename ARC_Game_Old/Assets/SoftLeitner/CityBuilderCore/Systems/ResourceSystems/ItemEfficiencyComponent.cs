using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that influences efficiency based on what items it has access to<br/>
    /// items are consumed over time and efficiency drops if they run out
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_item_efficiency_component.html")]
    public class ItemEfficiencyComponent : BuildingComponent, IEfficiencyFactor, IItemReceiver
    {
        public override string Key => "IEF";

        [Tooltip("one for each item used to determine efficiency, when 1/2 item recipients are stocked the efficiency is 0.5")]
        public ItemRecipient[] Recipients;

        public bool IsWorking => Recipients.All(r => r.HasAccess);
        public float Factor => (float)Recipients.Where(r => r.HasAccess).Count() / Recipients.Length;

        public BuildingComponentReference<IItemReceiver> Reference { get; set; }
        public IItemContainer ItemContainer { get; private set; }

        public int Priority => 1000;

        private IGameSettings _settings;
        private HashSet<Item> _receiveItems;

        private void Awake()
        {
            ItemContainer = new SplitItemContainer(Recipients.Select(r => r.Storage), i => Recipients.FirstOrDefault(r => r.Item == i)?.Storage);
        }
        private void Start()
        {
            _settings = Dependencies.Get<IGameSettings>();
        }
        private void Update()
        {
            Recipients.ForEach(r => r.Update(_settings.ItemsMultiplier));
        }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<IItemReceiver>(this);
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            replaceTrait<IItemReceiver>(this, replacement.GetBuildingComponent<ItemEfficiencyComponent>());
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IItemReceiver>(this);
        }

        public IEnumerable<Item> GetReceiveItems()
        {
            if (_receiveItems == null)
                _receiveItems = new HashSet<Item>(Recipients.Select(r => r.Item));
            return _receiveItems;
        }
        public int GetReceiveCapacityRemaining(Item item) => Building.IsSuspended ? 0 : ItemContainer.GetItemCapacityRemaining(item);
        public void ReserveCapacity(Item item, int amount)
        {
            ItemContainer.ReserveCapacity(item, amount);
        }
        public void UnreserveCapacity(Item item, int amount)
        {
            ItemContainer.UnreserveCapacity(item, amount);
        }
        public int Receive(ItemStorage storage, Item item, int quantity)
        {
            var recipient = Recipients.FirstOrDefault(r => r.Item == item);
            if (recipient == null)
                return quantity;

            return quantity - storage.MoveItemsTo(recipient.Storage, item, quantity);
        }

        public IEnumerable<ItemQuantity> GetItems()
        {
            List<ItemQuantity> items = new List<ItemQuantity>();

            foreach (var recipient in Recipients)
            {
                items.Add(new ItemQuantity(recipient.Item, recipient.Storage.GetItemQuantity(recipient.Item)));
            }

            return items;
        }

        #region Saving
        [Serializable]
        public class ItemsEfficiencyData
        {
            public ItemRecipient.ItemsRecipientData[] Recipients;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new ItemsEfficiencyData()
            {
                Recipients = Recipients.Select(r => r.SaveData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<ItemsEfficiencyData>(json);

            for (int i = 0; i < data.Recipients.Length; i++)
            {
                Recipients[i].LoadData(data.Recipients[i]);
            }
        }
        #endregion
    }
}