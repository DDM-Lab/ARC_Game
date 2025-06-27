using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// task that completes when a specific number of buildings or buildings of a category exist at the same time <br/>
    /// this could also be done using a score but using this task avoid having to create a seperate score asset
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_task_item.html")]
    public class BuildingTaskItem : TaskItem
    {
        [Tooltip("the building that counts toward completing the task")]
        public BuildingInfo Building;
        [Tooltip("alternatively buildings of a whole category can count toward completing the task")]
        public BuildingCategory BuildingCategory;
        [Tooltip("the task completes when at least this number of the specified buildings are on the map at the same time")]
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
            int count = 0;
            if(Building)
                count = _manager.Count(Building);
            else if(BuildingCategory)
                count = _manager.Count(BuildingCategory);
            
            if (count < Quantity)
            {
                if (Text)
                    Text.text = $"{count}/{Quantity}";
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
    }
}
