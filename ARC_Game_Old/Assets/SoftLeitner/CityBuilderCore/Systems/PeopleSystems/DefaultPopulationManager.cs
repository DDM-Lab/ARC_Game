using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// default implementation of all people systems bundeld up into one for convenience<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_population_manager.html")]
    public class DefaultPopulationManager : MonoBehaviour, IPopulationManager, IEmploymentManager, IWorkplaceFinder
    {
        private class WorkerPathCandidate
        {
            public Worker Worker { get; set; }
            public IWorkerUser WorkerUser { get; set; }
            public PathQuery Query { get; set; }
            public WalkingPath Path { get; set; }
            public ItemQuantity Items { get; set; }
            public BuildingComponentPathQuery<IItemGiver> SupplierQuery { get; set; }
            public BuildingComponentPath<IItemGiver> SupplierPath { get; set; }
        }
        private class DefaultWorkerPathQuery : WorkerPathQuery
        {
            private List<WorkerPathCandidate> _candidates;

            public DefaultWorkerPathQuery(List<WorkerPathCandidate> candidates)
            {
                _candidates = candidates;
            }

            public override WorkerPath Complete()
            {
                float currentNeed = float.MinValue;
                WorkerPath currentPath = null;

                foreach (var candidate in _candidates)
                {
                    var need = candidate.WorkerUser.GetWorkerNeed(candidate.Worker);

                    if (need <= 0f || need < currentNeed)
                    {
                        candidate.Query.Cancel();
                        candidate.SupplierQuery?.Cancel();
                    }
                    else
                    {
                        candidate.Path = candidate.Query.Complete();
                        if (candidate.Path == null)
                        {
                            candidate.SupplierQuery?.Cancel();
                        }
                        else
                        {
                            var workerPath = new WorkerPath(candidate.WorkerUser.Reference, candidate.Path);

                            if (candidate.SupplierQuery != null)
                            {
                                var items = candidate.WorkerUser.GetItemsNeeded(candidate.Worker);
                                if (items?.Item != items.Item)
                                    continue;//someone else came first, conditions have changed, abort

                                candidate.SupplierPath = candidate.SupplierQuery?.Complete();
                                if (candidate.SupplierPath == null)
                                    continue;

                                workerPath.AddSupply(candidate.SupplierPath.Component, candidate.Items, candidate.SupplierPath.Path);
                            }

                            currentNeed = need;
                            currentPath = workerPath;
                        }
                    }
                }

                return currentPath;
            }
        }

        private Migration[] _migrations;
        private Dictionary<Population, EmploymentPopulation> _populations = new Dictionary<Population, EmploymentPopulation>();
        private Dictionary<string, int> _employmentGroupPriorities = new Dictionary<string, int>();

        protected virtual void Awake()
        {
            _migrations = this.FindObjects<Migration>();

            Dependencies.Register<IPopulationManager>(this);
            Dependencies.Register<IEmploymentManager>(this);
            Dependencies.Register<IWorkplaceFinder>(this);
        }

        protected virtual void Start()
        {
            foreach (var population in Dependencies.Get<IObjectSet<Population>>().Objects)
            {
                if (_populations.ContainsKey(population))
                    continue;
                _populations.Add(population, new EmploymentPopulation(population));
            }

            foreach (var employmentGroup in Dependencies.Get<IObjectSet<EmploymentGroup>>().Objects)
            {
                if (_employmentGroupPriorities.ContainsKey(employmentGroup.Key))
                    continue;
                _employmentGroupPriorities.Add(employmentGroup.Key, employmentGroup.Priority);
            }

            this.StartChecker(CheckEmployment);
        }

        #region Population
        public Migration GetMigration(Population population) => _migrations.FirstOrDefault(m => m.Population == population);

        public IEnumerable<IHousing> GetHousings()
        {
            return Dependencies.Get<IBuildingManager>().GetBuildingTraits<IHousing>();
        }
        public int GetQuantity(Population population, bool includeReserved = false)
        {
            var sum = 0;   
            foreach (var housing in GetHousings())
            {
                sum += housing.GetQuantity(population, includeReserved);
            }
            return sum;
        }
        public int GetCapacity(Population population)
        {
            var sum = 0;
            foreach (var housing in GetHousings())
            {
                sum += housing.GetCapacity(population);
            }
            return sum;
        }
        public int GetRemainingCapacity(Population population)
        {
            var sum = 0;
            foreach (var housing in GetHousings())
            {
                sum += housing.GetRemainingCapacity(population);
            }
            return sum;
        }

        public void AddHomeless(Population population, IHousing housing, int quantity)
        {
            var migration = _migrations?.FirstOrDefault(m => m.Population == population);
            if (migration == null)
                return;
            migration.AddHomeless(housing, quantity);
        }
        #endregion

        #region Employment
        public void AddEmployment(IEmployment employment)
        {
            foreach (var population in employment.GetPopulations())
            {
                if (!_populations.ContainsKey(population))
                    _populations.Add(population, new EmploymentPopulation(population));
                _populations[population].Add(employment);
            }
        }
        public void RemoveEmployment(IEmployment employment)
        {
            foreach (var population in employment.GetPopulations())
            {
                if (!_populations.ContainsKey(population))
                    continue;
                _populations[population].Remove(employment);
                if (_populations[population].IsEmpty)
                    _populations.Remove(population);
            }
        }

        public int GetNeeded(Population population, EmploymentGroup group = null)
        {
            if (!_populations.ContainsKey(population))
                return 0;

            var employmentPopulation = _populations[population];
            if (group == null)
                return employmentPopulation.WorkersNeeded;

            return employmentPopulation.GetNeeded(group);
        }
        public int GetAvailable(Population population, EmploymentGroup group = null)
        {
            if (!_populations.ContainsKey(population))
                return 0;

            var employmentPopulation = _populations[population];
            if (group == null)
                return employmentPopulation.WorkersAvailable;

            return employmentPopulation.GetAvailable(group);
        }
        public int GetEmployed(Population population, EmploymentGroup group = null) => Mathf.Min(GetNeeded(population, group), GetAvailable(population, group));

        public float GetEmploymentRate(Population population)
        {
            if (_populations.ContainsKey(population))
                return _populations[population].EmploymentRate;
            else
                return 0f;
        }
        public float GetWorkerRate(Population population)
        {
            if (_populations.ContainsKey(population))
                return _populations[population].WorkerRate;
            else
                return 0f;
        }

        public void CheckEmployment()
        {
            foreach (var population in _populations.Keys)
            {
                _populations[population].CalculateNeeded();
                _populations[population].Distribute(GetQuantity(population), _employmentGroupPriorities);
            }
        }

        public int GetPriority(EmploymentGroup group)
        {
            return _employmentGroupPriorities[group.Key];
        }
        public void SetPriority(EmploymentGroup group, int priority)
        {
            _employmentGroupPriorities[group.Key] = priority;
        }
        #endregion

        #region Work
        public WorkerPath GetWorkerPath(IBuilding building, Vector2Int? currentPoint, Worker worker, ItemStorage storage, float maxDistance, PathType pathType, object pathTag)
        {
            if (building == null || worker == null)
                return null;

            var workerUsers = getWorkerUsers(building, worker, maxDistance);

            if (workerUsers.Count == 1)
            {
                var workerUser = workerUsers.First.Value;

                var path = PathHelper.FindPath(building, currentPoint, workerUser.Building, pathType, pathTag);
                if (path == null)
                    return null;

                var workerPath = new WorkerPath(workerUser.Reference, path);

                var items = workerUsers.First.Value.GetItemsNeeded(worker);
                if (items != null)
                {
                    items.Quantity = Math.Min(items.Quantity, storage.GetItemCapacity(items.Item));
                    var supplyPath = Dependencies.Get<IGiverPathfinder>().GetGiverPath(building, currentPoint, items, maxDistance, pathType, pathTag);
                    if (supplyPath == null)
                        return null;

                    workerPath.AddSupply(supplyPath.Component, items, supplyPath.Path);
                }

                return workerPath;
            }
            else if (workerUsers.Count > 1)
            {
                WorkerPath currentPath = null;
                float currentNeed = float.MinValue;

                foreach (var workerUser in workerUsers)
                {
                    var need = workerUser.GetWorkerNeed(worker);
                    if (need < currentNeed)
                        continue;

                    var path = PathHelper.FindPath(building, currentPoint, workerUser.Building, pathType, pathTag);
                    if (path == null)
                        continue;

                    var workerPath = new WorkerPath(workerUser.Reference, path);

                    var items = workerUser.GetItemsNeeded(worker);
                    if (items != null)
                    {
                        items.Quantity = Math.Min(items.Quantity, storage.GetItemCapacity(items.Item));
                        var supplyPath = Dependencies.Get<IGiverPathfinder>().GetGiverPath(building, currentPoint, items, maxDistance, pathType, pathTag);
                        if (supplyPath == null)
                            continue;

                        workerPath.AddSupply(supplyPath.Component, items, supplyPath.Path);
                    }

                    currentNeed = need;
                    currentPath = workerPath;
                }

                return currentPath;
            }

            return null;
        }
        public WorkerPathQuery GetWorkerPathQuery(IBuilding building, Vector2Int? currentPoint, Worker worker, ItemStorage storage, float maxDistance, PathType pathType, object pathTag)
        {
            if (building == null || worker == null)
                return null;

            var workerUsers = getWorkerUsers(building, worker, maxDistance);
            if (workerUsers.Count == 0)
                return null;

            return new DefaultWorkerPathQuery(workerUsers.Select(w =>
            {
                var candidate = new WorkerPathCandidate()
                {
                    Worker = worker,
                    WorkerUser = w,
                    Query = PathHelper.FindPathQuery(building, currentPoint, w.Building, pathType, pathTag),
                    Items = w.GetItemsNeeded(worker),
                };

                if (candidate.Items != null)
                {
                    candidate.Items.Quantity = Math.Min(candidate.Items.Quantity, storage.GetItemCapacity(candidate.Items.Item));
                    candidate.SupplierQuery = Dependencies.Get<IGiverPathfinder>().GetGiverPathQuery(building, currentPoint, candidate.Items, maxDistance, pathType, pathTag);
                    if (candidate.SupplierQuery == null)
                    {
                        candidate.Query.Cancel();
                        return null;
                    }
                }

                return candidate;
            }).Where(c => c != null).ToList());
        }
        private LinkedList<IWorkerUser> getWorkerUsers(IBuilding building, Worker worker, float maxDistance)
        {
            var workerUsers = new LinkedList<IWorkerUser>();
            foreach (var workerUser in Dependencies.Get<IBuildingManager>().GetBuildingTraits<IWorkerUser>())
            {
                var distance = Vector2.Distance(workerUser.Building.WorldCenter, building.WorldCenter);
                if (distance > maxDistance)
                    continue;

                var need = workerUser.GetWorkerNeed(worker);
                if (need <= 0f)
                    continue;

                var node = workerUsers.First;
                while (node != null && node.Value.GetWorkerNeed(worker) > need)
                    node = node.Next;

                if (node == null)
                    workerUsers.AddLast(workerUser);
                else
                    workerUsers.AddBefore(node, workerUser);
            }
            return workerUsers;
        }
        #endregion

        #region Saving
        [Serializable]
        public class PopulationData
        {
            public Migration.MigrationData[] Migrations;
        }

        string IPopulationManager.SaveData() => JsonUtility.ToJson(new PopulationData()
        {
            Migrations = _migrations?.Select(m => m.SaveData()).ToArray()
        });
        void IPopulationManager.LoadData(string json)
        {
            var data = JsonUtility.FromJson<PopulationData>(json);

            if (_migrations == null)
                return;

            foreach (var migration in _migrations)
            {
                migration.LoadData(data.Migrations.First(m => m.Population == migration.Population.Key));
            }
        }

        [Serializable]
        public class EmploymentData
        {
            public EmploymentGroupData[] Groups;
        }
        [Serializable]
        public class EmploymentGroupData
        {
            public string Key;
            public int Priority;
        }

        string IEmploymentManager.SaveData() => JsonUtility.ToJson(new EmploymentData()
        {
            Groups = _employmentGroupPriorities.Select(p => new EmploymentGroupData() { Key = p.Key, Priority = p.Value }).ToArray()
        });
        void IEmploymentManager.LoadData(string json)
        {
            var data = JsonUtility.FromJson<EmploymentData>(json);

            foreach (var group in data.Groups)
            {
                _employmentGroupPriorities[group.Key] = group.Priority;
            }
        }
        #endregion
    }
}