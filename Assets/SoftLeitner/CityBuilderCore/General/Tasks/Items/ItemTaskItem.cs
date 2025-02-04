using UnityEngine;
using static CityBuilderCore.ItemScore;

namespace CityBuilderCore
{
    /// <summary>
    /// task that completes when a specific number of items is found<br/>
    /// this could also be done using a score but using this task avoid having to create a seperate score asset
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_item_task_item.html")]
    public class ItemTaskItem : TaskItem
    {
        [Tooltip("the quantity of this item is checked to determine if the task is complete")]
        public Item Item;
        [Tooltip(@"determines how item quantity is calculated
Global		global storage
Stored		storage components
Owned		ALL item owners
OwnedBuild	building item owners
OwnedWalker	walker item owners")]
        public CalculationMode Mode;
        [Tooltip("when at least this number of items is found the task completes")]
        public int Quantity;
        [Tooltip("optional text that can be used to display the current progress(for example '5/10')")]
        public TMPro.TMP_Text Text;

        public override bool IsFinished => State > 0;

        private Coroutine _checker;
        private IBuildingManager _manager;

        private void Start()
        {
            _manager = Dependencies.Get<IBuildingManager>();
            OnEnable();
        }

        private void OnEnable()
        {
            if (_manager == null)
                return;

            if (IsFinished)
            {
                if (Text)
                    Text.text = $"{Quantity}/{Quantity}";

                Set?.Invoke();
            }
            else
            {
                if (Text)
                    Text.text = $"0/{Quantity}";

                _checker = this.StartChecker(check);
            }
        }

        private void OnDisable()
        {
            if (_checker != null)
            {
                StopCoroutine(_checker);
                _checker = null;
            }
        }

        private void check()
        {
            int quantity = getQuantity();
            if (quantity < Quantity)
            {
                if (Text)
                    Text.text = $"{quantity}/{Quantity}";
            }
            else
            {
                State = 1;
                if (Text)
                    Text.text = $"{Quantity}/{Quantity}";

                OnDisable();

                Finished?.Invoke();
            }
        }

        private int getQuantity()
        {
            switch (Mode)
            {
                case CalculationMode.Global:
                    return Item.GetGlobalQuantity();
                case CalculationMode.Stored:
                    return Item.GetStoredQuantity();
                case CalculationMode.Owned:
                    return Item.GetBuildingOwnedQuantity() + Item.GetWalkerOwnedQuantity();
                case CalculationMode.OwnedBuildings:
                    return Item.GetBuildingOwnedQuantity();
                case CalculationMode.OwnedWalkers:
                    return Item.GetWalkerOwnedQuantity();
                default:
                    return Item.GetGlobalQuantity();
            }
        }
    }
}
