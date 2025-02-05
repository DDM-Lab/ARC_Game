using CityBuilderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// periodically spawns a <see cref="TownPlantTask"/> and a <see cref="TownHarvestTask"/> within a specified radius<br/>
    /// only spawns up to 2 of each of them so they dont spawn infinitely of there is no walker taking care of them
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_foresting_component.html")]
    public class TownForestingComponent : BuildingComponent
    {
        public override string Key => "FOR";

        [Tooltip("radius around the building the tasks will be created")]
        public float Radius;
        [Tooltip("spawned in a random empty spot within the radius")]
        public TownPlantTask PlantPrefab;
        [Tooltip("created on a random tree(as specified in TownHarvestTask.PrimaryStructureKey) within the radius")]
        public TownHarvestTask HarvestPrefab;
        [Tooltip("after each interval one of each task is created unless there are already 2 active")]
        public float Interval;

        private List<TownPlantTask> _plantTasks = new List<TownPlantTask>();
        private List<TownHarvestTask> _harvestTasks = new List<TownHarvestTask>();
        private float _time;

        private void Update()
        {
            _time += Time.deltaTime * Building.Efficiency;
            if (_time >= Interval)
            {
                _time = 0;

                var townManager = TownManager.Instance;
                var structureManager = Dependencies.Get<IStructureManager>();

                _harvestTasks.Cleanup();
                if (_harvestTasks.Count < 2)
                {
                    var points = Dependencies.Get<IStructureManager>().GetStructure(HarvestPrefab.PrimaryStructureKey).GetPoints()
                        .Where(p => Vector2Int.Distance(p, Building.Point) < Radius && !townManager.CheckTask(p));

                    if (points.Any())
                        _harvestTasks.Add(townManager.CreateTask(HarvestPrefab, points.Random(), transform));
                }

                _plantTasks.Cleanup();
                if (_plantTasks.Count < 2)
                {
                    foreach (var point in structureManager.GetRandomAvailablePoints(Building.Point, Radius, 1, predicate: p => Vector2.Distance(p, Building.Point) < Radius))
                    {
                        _plantTasks.Add(townManager.CreateTask(PlantPrefab, point, transform));
                    }
                }
            }
        }

        public override void TerminateComponent()
        {
            base.TerminateComponent();

            _plantTasks.ForEach(p => p.Terminate());
            _plantTasks.Clear();
            _harvestTasks.ForEach(h => h.Terminate());
            _harvestTasks.Clear();
        }

        public override void SuspendComponent()
        {
            base.SuspendComponent();

            _plantTasks.ForEach(p => p.Terminate());
            _plantTasks.Clear();
            _harvestTasks.ForEach(h => h.Terminate());
            _harvestTasks.Clear();
        }

        #region Saving
        [Serializable]
        public class TownForestingData
        {
            public TownTaskData[] PlantTasks;
            public TownTaskData[] HarvestTasks;
            public float Time;
        }

        public override string SaveData()
        {
            _plantTasks.Cleanup();
            _harvestTasks.Cleanup();

            return JsonUtility.ToJson(new TownForestingData()
            {
                PlantTasks = _plantTasks.Select(t => t.SaveData()).ToArray(),
                HarvestTasks = _harvestTasks.Select(t => t.SaveData()).ToArray(),
                Time = _time
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<TownForestingData>(json);

            _plantTasks = data.PlantTasks.Select(d => (TownPlantTask)TownManager.Instance.CreateTask(d, transform)).ToList();
            _harvestTasks = data.HarvestTasks.Select(d => (TownHarvestTask)TownManager.Instance.CreateTask(d, transform)).ToList();
            _time = data.Time;
        }
    }
    #endregion
}
