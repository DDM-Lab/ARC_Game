using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes the quantity of an item in unity UI
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_inventory_visualizer.html")]
    public class InventoryVisualizer : TooltipOwnerBase
    {
        /// <summary>
        /// determines how the visualized quantity is calculated
        /// </summary>
        public enum InventoryMode
        {
            /// <summary>
            /// gets quantity from <see cref="IGlobalStorage"/>
            /// </summary>
            Global = 0,
            /// <summary>
            /// sums up quantities in all <see cref="IStorageComponent"/>s
            /// </summary>
            Stored = 10,
            /// <summary>
            /// sums up quantities in all <see cref="IItemOwner"/>s
            /// </summary>
            Owned = 20,
            /// <summary>
            /// sums up quantities in <see cref="IItemOwner"/> buildings
            /// </summary>
            OwnedBuildings = 21,
            /// <summary>
            /// sums up quantities in <see cref="IItemOwner"/> walkers
            /// </summary>
            OwnedWalkers = 22,
            /// <summary>
            /// gets quantity from a specific <see cref="ItemStore"/>
            /// </summary>
            Store = 30
        }

        [Tooltip("the item that will shown by this visualizer")]
        public Item Item;
        [Tooltip(@"determines how item quantity is calculated
Global		global storage
Stored		storage components
Owned		ALL item owners
OwnedBuild	building item owners
OwnedWalker	walker item owners")]
        public InventoryMode Mode;
        [Tooltip("text field that will display the item quantity")]
        public TMPro.TMP_Text Text;
        [Tooltip("when set displays cost, otherwise cost is displayed in regular text")]
        public TMPro.TMP_Text TextCost;
        [Tooltip("store used when the Mode is Store")]
        public ItemStore Store;

        public override string TooltipName => Item.Name;

        private IToolsManager _toolsManager;

        private void Start()
        {
            _toolsManager = Dependencies.Get<IToolsManager>();
        }

        private void Update()
        {
            int quantity;

            switch (Mode)
            {
                case InventoryMode.Stored:
                    quantity = Item.GetStoredQuantity();
                    break;
                case InventoryMode.Owned:
                    quantity = Item.GetBuildingOwnedQuantity() + Item.GetWalkerOwnedQuantity();
                    break;
                case InventoryMode.OwnedBuildings:
                    quantity = Item.GetBuildingOwnedQuantity();
                    break;
                case InventoryMode.OwnedWalkers:
                    quantity = Item.GetWalkerOwnedQuantity();
                    break;
                case InventoryMode.Store:
                    quantity = Store.Storage.GetItemQuantity(Item);
                    break;
                default:
                    quantity = Item.GetGlobalQuantity();
                    break;
            }

            int cost = _toolsManager.GetCost(Item);

            if (TextCost)
            {
                Text.text = quantity.ToString();

                if (cost == 0)
                    TextCost.text = string.Empty;
                else
                    TextCost.text = $"{cost}";
            }
            else
            {
                if (cost == 0)
                    Text.text = quantity.ToString();
                else
                    Text.text = $"{quantity}<color=red>({-cost})</color>";
            }
        }
    }
}