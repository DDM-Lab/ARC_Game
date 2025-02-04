using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that uses workers, needs to call finish on the worker when done
    /// </summary>
    public interface IWorkerUser : IBuildingTrait<IWorkerUser>
    {
        /// <summary>
        /// how much the user needs a worker, makes sure users who already have a worker on site have lower priority
        /// </summary>
        /// <param name="worker"></param>
        /// <returns></returns>
        float GetWorkerNeed(Worker worker);
        /// <summary>
        /// gets the items a worker has to bring if any
        /// </summary>
        /// <param name="worker"></param>
        /// <returns></returns>
        ItemQuantity GetItemsNeeded(Worker worker);

        /// <summary>
        /// worker has been assigned to the user and will start walking there
        /// </summary>
        /// <param name="walker"></param>
        void ReportAssigned(WorkerWalker walker);
        /// <summary>
        /// worker has arrived at the user and receives his path into the user(optional)
        /// </summary>
        /// <param name="walker"></param>
        /// <returns></returns>
        Vector3[] ReportArrived(WorkerWalker walker);
        /// <summary>
        /// if a path inside was returned on arrival the worker will report inside after following that path
        /// </summary>
        /// <param name="walker"></param>
        void ReportInside(WorkerWalker walker);

        /// <summary>
        /// gets which workers are assigned but have not arrived at the user<br/>
        /// does not contain empty entries like queued and working
        /// </summary>
        /// <returns></returns>
        IEnumerable<Worker> GetAssigned();
        /// <summary>
        /// gets the queue slots in its entire length, null for unfilled slots
        /// </summary>
        /// <returns></returns>
        IEnumerable<Worker> GetQueued();
        /// <summary>
        /// gets the worker slots in its entire length, null for unfilled slots
        /// </summary>
        /// <returns></returns>
        IEnumerable<Worker> GetWorking();
    }
}