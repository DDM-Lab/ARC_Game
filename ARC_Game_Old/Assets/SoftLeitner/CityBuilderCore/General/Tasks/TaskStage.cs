using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// stage within a <see cref="TaskList"/> that consists of several <see cref="TaskItem"/>s
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_task_stage.html")]
    public class TaskStage : MonoBehaviour
    {
        [Tooltip("items that are managed by this stage of the task list")]
        public TaskItem[] Items;
        [Tooltip("optional fader that is used when hiding and showing the stage")]
        public Fader Fader;

        [Tooltip("fired when the stage is started(after the previous stage has been completed)")]
        public UnityEvent Started;
        [Tooltip("fired when all the items in this stage are completed")]
        public UnityEvent Completed;

        private UnityAction _check;

        private void OnEnable()
        {
            _check = new UnityAction(check);
            foreach (var item in Items)
            {
                item.Set.AddListener(_check);
                item.Finished.AddListener(_check);
            }
        }
        private void OnDisable()
        {
            foreach (var item in Items)
            {
                item.Set.RemoveListener(_check);
                item.Finished.RemoveListener(_check);
            }
            _check = null;
        }

        public virtual void StartStage()
        {
            Started?.Invoke();
        }
        public virtual void ShowStage()
        {
            if (Fader)
                Fader.Show();
            else
                gameObject.SetActive(true);
        }
        public virtual void HideStage()
        {
            if (Fader)
                Fader.Hide();
            else
                gameObject.SetActive(false);
        }
        public virtual void ResetStage()
        {
            gameObject.SetActive(false);
            foreach (var item in Items)
            {
                item.ResetState();
            }
        }

        public int[] GetItemStates() => Items.Select(i => i.State).ToArray();
        public void SetItemStates(int[] values)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (i >= values.Length)
                    break;
                Items[i].SetState(values[i]);
            }
        }

        private void check()
        {
            if (Items.All(i => i.IsFinished))
            {
                Completed?.Invoke();
            }
        }
    }
}