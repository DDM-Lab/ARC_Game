using CityBuilderCore;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// produces items using specified raw materials and places them in specified points<br/>
    /// used in combination with <see cref="TownWorkComponent"/> for the woodcutter<br/>
    /// the production component sets IsWorking when all the raw materials have arrived<br/>
    /// when IsWorking is true the work component activates its work tasks which drive efficiency<br/>
    /// production is dependent on efficiency so progress is only made when walkers are working
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_production_component.html")]
    public class TownProductionComponent : ProductionComponent, IEfficiencyFactor
    {
        [Tooltip("task that supplies the raw materials")]
        public TownDeliverTask DeliverTask;
        [Tooltip("points at which the created item tasks are placed")]
        public Vector2Int[] ItemPoints;
        [Tooltip("item tasks that is created by production")]
        public TownItemTask ItemPrefab;

        private List<TownItemTask> _itemTasks = new List<TownItemTask>();

        public bool IsWorking => _productionState == ProductionState.Working;
        public float Factor => 1f;

        public override void TerminateComponent()
        {
            base.TerminateComponent();

            _itemTasks.ForEach(i => i.Terminate());
        }

        public override void SuspendComponent()
        {
            base.SuspendComponent();

            DeliverTask.SuspendTask();
        }
        public override void ResumeComponent()
        {
            base.ResumeComponent();

            DeliverTask.ResumeTask();
        }

        protected override bool canWork()
        {
            return !TownManager.Instance.GetItemLimitReached(ItemPrefab.Items.Item);
        }
        protected override bool canProduce()
        {
            cleanupItemTasks();

            return _itemTasks.Count < ItemPoints.Length;
        }
        protected override void produce()
        {
            foreach (var point in ItemPoints)
            {
                var worldPoint = Building.RotateBuildingPoint(point);

                if (_itemTasks.Any(t => t.Point == worldPoint))
                    continue;

                _itemTasks.Add(TownManager.Instance.CreateTask(ItemPrefab, worldPoint, transform));
                return;
            }
        }

        private void cleanupItemTasks()
        {
            foreach (var itemTask in _itemTasks.ToArray())
            {
                if (!itemTask)
                    _itemTasks.Remove(itemTask);
            }
        }

        #region Saving
        [Serializable]
        public class TownProductionData : ProductionData
        {
            public TownTaskData DeliverTask;
            public TownTaskData[] ItemTasks;
        }

        public override string SaveData()
        {
            var data = new TownProductionData();

            saveData(data);

            data.DeliverTask = DeliverTask.SaveData();

            cleanupItemTasks();
            data.ItemTasks = _itemTasks.Select(t => t.SaveData()).ToArray();

            return JsonUtility.ToJson(data);
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<TownProductionData>(json);

            loadData(data);

            DeliverTask.LoadData(data.DeliverTask);

            foreach (var itemData in data.ItemTasks)
            {
                var itemTask = TownManager.Instance.CreateTask(ItemPrefab, itemData.Point, transform);
                itemTask.LoadData(itemData);
                _itemTasks.Add(itemTask);
            }
        }
        #endregion
    }
}
