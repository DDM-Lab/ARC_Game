using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// manages a list of <see cref="TaskStage"/>, useful for showing a list of tutorial task to players<br/>
    /// determines which stage is visible and persists task state and stage completion
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_task_list.html")]
    public class TaskList : ExtraDataBehaviour
    {
        [Tooltip("stages managed by this list, the list manages which stage is visible and persists task state and stage completion")]
        public TaskStage[] Stages;

        public TaskStage CurrentStage => Stages.ElementAtOrDefault(_currentStage);

        private int _currentStage;

        private void Start()
        {
            if (Stages == null || Stages.Length == 0)
                return;

            foreach (var stage in Stages)
            {
                stage.Completed?.AddListener(new UnityAction(() => StartCoroutine(advanceStage())));
            }

            resetStages();
            Stages[0].ShowStage();
        }

        private IEnumerator advanceStage()
        {
            yield return new WaitForSecondsRealtime(1);
            CurrentStage?.HideStage();
            if (CurrentStage != null && CurrentStage.Fader)
                yield return new WaitForSecondsRealtime(CurrentStage.Fader.Duration);
            _currentStage++;
            CurrentStage?.ShowStage();
            CurrentStage?.StartStage();
        }

        private void resetStages()
        {
            foreach (var stage in Stages)
            {
                stage.ResetStage();
            }
        }

        #region Saving
        [Serializable]
        public class TaskListData
        {
            public int Stage;
            public int[] States;
        }

        public override string SaveData()
        {
            if (Stages == null || Stages.Length == 0)
                return string.Empty;

            return JsonUtility.ToJson(new TaskListData()
            {
                Stage = _currentStage,
                States = CurrentStage?.GetItemStates()
            });
        }

        public override void LoadData(string json)
        {
            if (Stages == null || Stages.Length == 0)
                return;

            var data = JsonUtility.FromJson<TaskListData>(json);
            _currentStage = data.Stage;

            resetStages();

            CurrentStage?.SetItemStates(data.States);
            CurrentStage?.ShowStage();
        }
        #endregion
    }
}