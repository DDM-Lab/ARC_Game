using CityBuilderCore;
using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// building component that uses <see cref="TownWorkTask"/>s to drive the buildings efficiency<br/>
    /// the work tasks are only active when the buildings <see cref="IsWorking"/> is true<br/>
    /// this is used in <see cref="TownProductionComponent"/> to only activate work when raw materials are present
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_work_component.html")]
    public class TownWorkComponent : BuildingComponent, IEfficiencyFactor
    {
        [Tooltip("tasks at which walkers with the appropriate job can work to drive efficency, if there are two tasks and one currently has a walker working the efficiency is 0.5")]
        public TownWorkTask[] WorkTasks;

        public override string Key => "WRK";

        public bool IsWorking => true;
        public float Factor => WorkTasks.Count(t => t.IsWorking) / (float)WorkTasks.Length;

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            this.StartChecker(checkWork);
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            WorkTasks.ForEach(t => t.Terminate());
        }

        public override void SuspendComponent()
        {
            base.SuspendComponent();

            WorkTasks.ForEach(t => t.SuspendTask());
        }
        public override void ResumeComponent()
        {
            base.ResumeComponent();

            WorkTasks.ForEach(t => t.ResumeTask());
        }

        private void checkWork()
        {
            WorkTasks.ForEach(t => t.SetNeeded(Building.IsWorking));
        }

        #region Saving
        [Serializable]
        public class TownWorkData
        {
            public TownTaskData[] WorkTasks;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new TownWorkData()
            {
                WorkTasks = WorkTasks.Select(t => t.SaveData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<TownWorkData>(json);

            for (int i = 0; i < data.WorkTasks.Length; i++)
            {
                WorkTasks[i].LoadData(data.WorkTasks[i]);
            }
        }
        #endregion
    }
}
