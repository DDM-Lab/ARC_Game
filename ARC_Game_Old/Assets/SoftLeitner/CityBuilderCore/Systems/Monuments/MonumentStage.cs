using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// represents one stage of the building process of a monument
    /// </summary>
    [Serializable]
    public class MonumentStage
    {
        [Tooltip("name of the stage for use in UI")]
        public string Name;
        [Tooltip("optional notification that is displayed when the stage is finished")]
        public string Notification;
        [Tooltip("the type of worker used for a duration to build(optional)")]
        public Worker Worker;
        [Tooltip("the duration taken by the Worker")]
        public float Duration;
        [Tooltip("the type of woorker used to supply the items")]
        public Worker Supplier;
        [Tooltip("items that have to be supplies by the Supplier")]
        public ItemQuantity Items;
        [Tooltip("steps of the stage, each one needs the worker and supplier")]
        public MonumentStep[] Steps;

        public bool NeedsWorker => Worker;
        public bool NeedsSupplier => Supplier;

        public bool IsFinished => Steps.All(s => s.IsFinished);

        public void UpdateStage()
        {
            Steps.ForEach(s => s.UpdateStep(this));
        }

        public ItemQuantity GetItemsNeeded(Worker worker)
        {
            if (worker == Supplier)
                return new ItemQuantity(Items.Item, Items.Quantity);
            return null;
        }

        public float GetWorkerNeed(Worker worker)
        {
            if (worker == Worker && Steps.Any(s => s.NeedsWorker))
                return 1f;
            if (worker == Supplier && Steps.Any(s => s.QuantityAssigned < Items.Quantity))
                return 1f;
            return 0f;
        }

        public void Assign(WorkerWalker walker)
        {
            if (walker.Worker == Supplier)
                Steps.FirstOrDefault(s => s.QuantityAssigned < Items.Quantity)?.AssignSupplier(walker, walker.Storage.GetItemCapacity(Items.Item));
            else if (walker.Worker == Worker)
                Steps.FirstOrDefault(s => s.NeedsWorker)?.AssignWorker(walker);
        }
        public Vector3[] Arrive(WorkerWalker walker)
        {
            if (walker.Worker == Worker)
                return Steps.FirstOrDefault(s => s.Worker == walker)?.ArriveWalker(walker);
            else if (walker.Worker == Supplier)
                return Steps.FirstOrDefault(s => s.Suppliers.Contains(walker))?.ArriveWalker(walker);
            return null;
        }
        public void Inside(WorkerWalker walker)
        {
            if (walker.Worker == Worker)
                Steps.FirstOrDefault(s => s.Worker == walker)?.InsideWalker(walker);
            else if (walker.Worker == Supplier)
                Steps.FirstOrDefault(s => s.Suppliers.Contains(walker))?.InsideWalker(walker);
        }

        public IEnumerable<Worker> GetAssigned()
        {
            if (NeedsWorker)
            {
                foreach (var step in Steps)
                {
                    if (step.Worker && !step.Worker.IsWorking)
                        yield return Worker;
                    else
                        yield return null;
                }
            }

            if (NeedsSupplier)
            {
                foreach (var step in Steps)
                {
                    if (step.QuantitySupplied >= Items.Quantity)
                        yield return Worker;
                    else
                        yield return null;
                }
            }
        }
        public IEnumerable<Worker> GetQueued()
        {
            return Enumerable.Empty<Worker>();
        }
        public IEnumerable<Worker> GetWorking()
        {
            if (NeedsWorker)
            {
                foreach (var step in Steps)
                {
                    if (step.Worker && step.Worker.IsWorking)
                        yield return Worker;
                    else
                        yield return null;
                }
            }

            if (NeedsSupplier)
            {
                foreach (var step in Steps)
                {
                    if (step.QuantitySupplied >= Items.Quantity)
                        yield return Worker;
                    else
                        yield return null;
                }
            }
        }
    }
}