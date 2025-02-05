using CityBuilderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// periodically spawns a <see cref="TownGatherTask"/> within its radius<br/>
    /// only spawns up to 2 taks so they dont spawn infinitely of there is no walker taking care of them<br/>
    /// used to collect berries from bushes which is better than harvesting because it does not remove the bush and berries will regrow
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_gathering_component.html")]
    public class TownGatheringComponent : BuildingComponent
    {
        public override string Key => "GAT";

        [Tooltip("radius around the building the tasks will be created")]
        public float Radius;
        [Tooltip("created on a random bush(as specified in TownGatherTask.SourceStructure) within the radius")]
        public TownGatherTask TaskPrefab;
        [Tooltip("after each interval one of each task is created unless there are already 2 active")]
        public float Interval;

        private List<TownGatherTask> _tasks = new List<TownGatherTask>();
        private float _time;

        private void Update()
        {
            _time += Time.deltaTime * Building.Efficiency;
            if (_time >= Interval)
            {
                _time = 0;
                _tasks.Cleanup();

                if (_tasks.Count < 2)
                {
                    var structureManager = Dependencies.Get<IStructureManager>();

                    var points = structureManager.GetStructure(TaskPrefab.SourceStructure).GetPoints()
                        .Where(p => Vector2Int.Distance(p, Building.Point) < Radius && !TownManager.Instance.CheckTask(p));

                    if (points.Any())
                        _tasks.Add(TownManager.Instance.CreateTask(TaskPrefab, points.Random(), transform));
                }
            }
        }

        public override void TerminateComponent()
        {
            base.TerminateComponent();

            _tasks.ForEach(t => t.Terminate());
            _tasks.Clear();
        }

        public override void SuspendComponent()
        {
            base.SuspendComponent();

            _tasks.ForEach(t => t.Terminate());
            _tasks.Clear();
        }

        #region Saving
        [Serializable]
        public class TownFarmingData
        {
            public TownTaskData[] Tasks;
            public float Time;
        }

        public override string SaveData()
        {
            _tasks.Cleanup();
            return JsonUtility.ToJson(new TownFarmingData()
            {
                Tasks = _tasks.Select(t => t.SaveData()).ToArray(),
                Time = _time
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<TownFarmingData>(json);

            _tasks = data.Tasks.Select(d => (TownGatherTask)TownManager.Instance.CreateTask(d, transform)).ToList();
            _time = data.Time;
        }
    }
    #endregion
}
