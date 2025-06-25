using CityBuilderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// building component that creates and manages a <see cref="TownFieldTask"/> for every point of its building<br/>
    /// actual farming mechanics are performed in those tasks
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_farming_component.html")]
    public class TownFarmingComponent : BuildingComponent
    {
        [Tooltip("prefab for the tasks that is created at every point of the building, the task manages the actual farming mechanics")]
        public TownFieldTask FieldPrefab;

        public override string Key => "FRM";

        private List<TownFieldTask> _fields = new List<TownFieldTask>();

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            foreach (var point in Building.GetPoints())
            {
                _fields.Add(TownManager.Instance.CreateTask(FieldPrefab, point, Building.Pivot));
            }
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            _fields.ForEach(f => f.Terminate());
        }

        public override void SuspendComponent()
        {
            base.SuspendComponent();

            _fields.ForEach(f => f.SuspendTask());
        }
        public override void ResumeComponent()
        {
            base.ResumeComponent();

            _fields.ForEach(f => f.ResumeTask());
        }

        #region Saving
        [Serializable]
        public class TownFarmingData
        {
            public TownTaskData[] Fields;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new TownFarmingData()
            {
                Fields = _fields.Select(t => t.SaveData()).ToArray()
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<TownFarmingData>(json);

            for (int i = 0; i < data.Fields.Length; i++)
            {
                _fields[i].LoadData(data.Fields[i]);
            }
        }
        #endregion
    }
}
