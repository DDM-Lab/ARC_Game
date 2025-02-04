using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// single step of building within a <see cref="MonumentStage"/>
    /// </summary>
    [Serializable]
    public class MonumentStep
    {
        [Tooltip("path workers take to get to the step before they can start work or supply items")]
        public Transform[] Path;
        [Tooltip("objects that are added once the step is finished(eg the bricks layed down, or the obelisk errected)")]
        public GameObject[] AddObjects;
        [Tooltip("objects that are removed once the step is finished(eg scaffolding)")]
        public GameObject[] RemoveObjects;

        public bool NeedsWorker => !IsFinished && Worker == null;

        public WorkerWalker Worker { get; private set; }

        public bool IsFinished { get; private set; }

        public int QuantityAssigned { get; private set; }
        public int QuantitySupplied { get; private set; }

        public List<WorkerWalker> Suppliers { get; private set; } = new List<WorkerWalker>();

        public void UpdateStep(MonumentStage stage)
        {
            if (IsFinished)
                return;

            if (stage.NeedsSupplier && QuantitySupplied < stage.Items.Quantity)
                return;

            if (stage.NeedsWorker)
            {
                if (Worker == null || !Worker.IsWorking)
                    return;

                Worker.Work(Time.deltaTime / stage.Duration);

                if (!Worker.IsWorkFinished)
                    return;
            }

            finish();
        }

        public void AssignWorker(WorkerWalker walker)
        {
            Worker = walker;
        }
        public void AssignSupplier(WorkerWalker walker, int quantity)
        {
            QuantityAssigned += quantity;
            Suppliers.Add(walker);
        }

        public Vector3[] ArriveWalker(WorkerWalker walker)
        {
            if (Path != null && Path.Length > 0)
                return Path.Select(t => t.position).ToArray();

            walkerOnSite(walker);
            return null;
        }
        public void InsideWalker(WorkerWalker walker)
        {
            walkerOnSite(walker);
        }

        public void FinishObjects()
        {
            AddObjects?.ForEach(o => o.SetActive(true));
            RemoveObjects?.ForEach(o => o.SetActive(false));
        }

        private void walkerOnSite(Walker walker)
        {
            if (Suppliers.Contains(walker))
            {
                var worker = (WorkerWalker)walker;
                Suppliers.Remove(worker);
                QuantitySupplied += worker.Storage.GetItemQuantities().First().Quantity;
                walker.Finish();
            }
        }

        private void finish()
        {
            IsFinished = true;

            if (Worker)
            {
                Worker.FinishWorking(Path.Select(t => t.position).Reverse());
                Worker = null;
            }

            FinishObjects();
        }

        #region Saving
        [Serializable]
        public class MonumentStepData
        {
            public bool IsFinished;
            public int QuantitySupplied;
        }

        public MonumentStepData SaveData()
        {
            return new MonumentStepData()
            {
                IsFinished = IsFinished,
                QuantitySupplied = QuantitySupplied
            };
        }
        public void LoadData(MonumentStepData data)
        {
            IsFinished = data.IsFinished;
            QuantityAssigned = data.QuantitySupplied;
            QuantitySupplied = data.QuantitySupplied;

            if (IsFinished)
            {
                FinishObjects();
            }
        }
        #endregion
    }
}