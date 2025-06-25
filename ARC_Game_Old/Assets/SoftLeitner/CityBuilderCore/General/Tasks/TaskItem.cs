using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for tasks within <see cref="TaskList"/>/<see cref="TaskStage"/><br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_task_item.html")]
    public abstract class TaskItem : MonoBehaviour
    {
        [Tooltip("fired when a save game is loaded and the whole list is reset(hide the checkmark)")]
        public UnityEvent Reset;
        [Tooltip("fired when a save game is loaded i which the task has previously been completed(show the checkmark without animation or sound)")]
        public UnityEvent Set;
        [Tooltip("fired when the task is completed, for exmaple when a certain score is reached or a building has been built(show checkmark with animation and sound)")]
        public UnityEvent Finished;

        public abstract bool IsFinished { get; }
        public int State { get; protected set; }

        public void SetState(int value)
        {
            State = value;
        }
        public void ResetState()
        {
            State = 0;
            Reset?.Invoke();
        }
    }
}