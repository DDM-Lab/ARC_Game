using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// task that completes when the current mission is completed within the playthrough<br/>
    /// useful for the final task in a list that gives pointers on how to complete the mission
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_mission_task_item.html")]
    public class MissionTaskItem : TaskItem
    {
        public override bool IsFinished => State > 0;

        private Coroutine _checker;
        private IMissionManager _manager;

        private void Start()
        {
            _manager = Dependencies.Get<IMissionManager>();
            OnEnable();
        }

        private void OnEnable()
        {
            if (_manager == null)
                return;

            if (IsFinished)
            {
                Set?.Invoke();
            }
            else
            {
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
            if (_manager.IsFinished)
            {
                State = 1;

                OnDisable();

                Finished?.Invoke();
            }
        }
    }
}
