using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// task that gets completed when a specific building or road builder has built a set quantity of buildings/roads
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_builder_task_item.html")]
    public class BuilderTaskItem : TaskItem
    {
        [Tooltip("the building tool that is monitored, when a certain number of buildings has been built by the tool the task is completed")]
        public BuildingBuilder Builder;
        [Tooltip("can be set instead of the above building builder to check for built roads instead")]
        public RoadBuilder RoadBuilder;
        [Tooltip("when this number of buildings/roads has been built by the specified tool the task is completed")]
        public int Quantity;
        [Tooltip("optional text that can be used to display the current progress(for example '5/10')")]
        public TMPro.TMP_Text Text;

        public override bool IsFinished => State > 0;

        private UnityAction<Building> _builtBuilding;
        private UnityAction<Vector2Int[]> _builtRoads;

        private void OnEnable()
        {
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

                if (Builder)
                {
                    _builtBuilding = new UnityAction<Building>(_ => built(1));
                    Builder.Built.AddListener(_builtBuilding);
                }

                if (RoadBuilder)
                {
                    _builtRoads = new UnityAction<Vector2Int[]>(p => built(p.Length));
                    RoadBuilder.Built.AddListener(_builtRoads);
                }
            }
        }

        private void OnDisable()
        {
            if (_builtBuilding != null)
            {
                Builder.Built.RemoveListener(_builtBuilding);
                _builtBuilding = null;
            }

            if (_builtRoads != null)
            {
                RoadBuilder.Built.RemoveListener(_builtRoads);
                _builtRoads = null;
            }
        }

        private void built(int quantity)
        {
            State += quantity;

            if (State < Quantity)
            {
                if (Text)
                    Text.text = $"{State}/{Quantity}";
            }
            else
            {
                if (Text)
                    Text.text = $"{Quantity}/{Quantity}";

                OnDisable();

                Finished?.Invoke();
            }
        }
    }
}
