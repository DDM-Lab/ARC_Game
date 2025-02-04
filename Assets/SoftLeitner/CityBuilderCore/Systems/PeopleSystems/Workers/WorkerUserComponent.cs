using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component using workers for a set duration to influence building efficiency(to drive production, layervalues, ...)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_worker_user_component.html")]
    public class WorkerUserComponent : BuildingComponent, IWorkerUser, IEfficiencyFactor
    {
        public override string Key => "WKU";

        [Tooltip("the type of worker needed")]
        public Worker Worker;
        [Tooltip("how many workers are needed at once for full efficiency")]
        public int Quantity;
        [Tooltip("how many workers can queue up")]
        public int Queue;
        [Tooltip("how long a worker stays working")]
        public float Duration;

        [Tooltip("fired whenever the component starts or stops working")]
        public BoolEvent IsWorkingChanged;

        public BuildingComponentReference<IWorkerUser> Reference { get; set; }

        public bool IsWorking => Quantity == _workers.Count;
        public float Factor => (float)_workers.Count / Quantity;

        private List<WorkerWalker> _assigned = new List<WorkerWalker>();
        private List<WorkerWalker> _workers = new List<WorkerWalker>();
        private Queue<WorkerWalker> _queue = new Queue<WorkerWalker>();

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            Reference = registerTrait<IWorkerUser>(this);

            onWorkingChanged();
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            replaceTrait(this, replacement.GetBuildingComponent<IWorkerUser>());
        }
        public override void TerminateComponent()
        {
            base.TerminateComponent();

            deregisterTrait<IWorkerUser>(this);
        }

        private void Update()
        {
            if (_workers.Count == 0)
                return;

            var currentWorker = _workers[0];

            if (currentWorker && currentWorker.IsWorking)
            {
                currentWorker.Work(Time.deltaTime / Duration);
            }

            if (!currentWorker || currentWorker.IsWorkFinished)
            {
                _workers.RemoveAt(0);
                if (_workers.Count < Quantity && _queue.Count > 0)
                    _workers.Add(_queue.Dequeue());

                if (currentWorker)
                {
                    currentWorker.Show();
                    currentWorker.FinishWorking();
                }

                onWorkingChanged();
            }
        }

        public float GetWorkerNeed(Worker worker)
        {
            if (Building.IsSuspended)
                return 0f;
            if (Worker != worker)
                return 0f;

            int assignedWorkers = Math.Min(Quantity, _assigned.Count);

            return (Quantity - _workers.Count - assignedWorkers) + ((Queue - _queue.Count - (_assigned.Count - assignedWorkers)) / 2f);
        }
        public ItemQuantity GetItemsNeeded(Worker walker) => null;
        public void ReportAssigned(WorkerWalker walker)
        {
            _assigned.Add(walker);
        }
        public Vector3[] ReportArrived(WorkerWalker walker)
        {
            walker.Hide();

            _assigned.Remove(walker);
            if (_workers.Count >= Quantity)
                _queue.Enqueue(walker);
            else
                _workers.Add(walker);

            onWorkingChanged();

            return null;
        }
        public void ReportInside(WorkerWalker walker)
        {
        }

        public IEnumerable<Worker> GetAssigned()
        {
            foreach (var assigned in _assigned)
            {
                yield return Worker;
            }
        }
        public IEnumerable<Worker> GetWorking()
        {
            for (int i = 0; i < Quantity; i++)
            {
                if (_workers.Count > i)
                    yield return Worker;
                else
                    yield return null;
            }
        }
        public IEnumerable<Worker> GetQueued()
        {
            for (int i = 0; i < Quantity; i++)
            {
                if (_queue.Count > i)
                    yield return Worker;
                else
                    yield return null;
            }
        }

        private void onWorkingChanged()
        {
            IsWorkingChanged?.Invoke(IsWorking);
        }
    }
}