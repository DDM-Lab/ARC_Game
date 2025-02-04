using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// default implementation for the resource systems<br/>
    /// <para>
    /// manages global items as well as finding the right givers, receivers and dispensers for items
    /// </para>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_item_manager.html")]
    public class DefaultItemManager : MonoBehaviour, IGlobalStorage, IGiverPathfinder, IReceiverPathfinder, IItemsDispenserManager
    {
        private class GiverPathCandidate
        {
            public IItemGiver Giver { get; set; }
            public PathQuery Query { get; set; }
            public WalkingPath Path { get; set; }
        }
        private class GiverPathQuery : BuildingComponentPathQuery<IItemGiver>
        {
            private DefaultItemManager _manager;
            private Vector3 _currentPosition;
            private ItemQuantity _items;
            private List<GiverPathCandidate> _candidates;

            public GiverPathQuery(DefaultItemManager manager, Vector3 currentPosition, ItemQuantity items, List<GiverPathCandidate> candidates)
            {
                _manager = manager;
                _currentPosition = currentPosition;
                _items = items;
                _candidates = candidates;
            }

            public override void Cancel()
            {
                foreach (var candidate in _candidates)
                {
                    candidate.Query.Cancel();
                }
            }

            public override BuildingComponentPath<IItemGiver> Complete()
            {
                IItemGiver currentGiver = null;
                WalkingPath currentPath = null;
                float currentDistance = float.MaxValue;

                foreach (var candidate in _candidates)
                {
                    var distance = Vector2.Distance(candidate.Giver.Building.WorldCenter, _currentPosition);
                    if (_manager.isDiscarded(_items, currentGiver, currentDistance, candidate.Giver, distance))
                    {
                        candidate.Query.Cancel();
                    }
                    else
                    {
                        candidate.Path = candidate.Query.Complete();
                        if (candidate.Path == null)
                            continue;

                        if (currentGiver == null || _manager.isMoreImportant(_items, currentGiver, currentDistance, candidate.Giver, distance))
                        {
                            currentGiver = candidate.Giver;
                            currentPath = candidate.Path;
                            currentDistance = candidate.Path.GetDistance();
                        }
                    }
                }

                if (currentGiver == null)
                    return null;
                return new BuildingComponentPath<IItemGiver>(currentGiver.Reference, currentPath);
            }
        }

        private class ReceiverPathCandidate
        {
            public IItemReceiver Receiver { get; set; }
            public PathQuery Query { get; set; }
            public WalkingPath Path { get; set; }
        }
        private class ReceiverPathQuery : BuildingComponentPathQuery<IItemReceiver>
        {
            private DefaultItemManager _manager;
            private Vector3 _currentPosition;
            private ItemQuantity _items;
            private List<ReceiverPathCandidate> _candidates;

            public ReceiverPathQuery(DefaultItemManager manager, Vector3 currentPosition, ItemQuantity items, List<ReceiverPathCandidate> candidates)
            {
                _manager = manager;
                _currentPosition = currentPosition;
                _items = items;
                _candidates = candidates;
            }

            public override void Cancel()
            {
                foreach (var candidate in _candidates)
                {
                    candidate.Query.Cancel();
                }
            }

            public override BuildingComponentPath<IItemReceiver> Complete()
            {
                IItemReceiver currentReceiver = null;
                WalkingPath currentPath = null;
                float currentDistance = float.MaxValue;

                foreach (var candidate in _candidates)
                {
                    var distance = Vector2.Distance(candidate.Receiver.Building.WorldCenter, _currentPosition);
                    if (_manager.isDiscarded(_items, currentReceiver, currentDistance, candidate.Receiver, distance))
                    {
                        candidate.Query.Cancel();
                    }
                    else
                    {
                        candidate.Path = candidate.Query.Complete();
                        if (candidate.Path == null)
                            continue;

                        if (currentReceiver == null || _manager.isMoreImportant(_items, currentReceiver, currentDistance, candidate.Receiver, distance))
                        {
                            currentReceiver = candidate.Receiver;
                            currentPath = candidate.Path;
                            currentDistance = candidate.Path.GetDistance();
                        }
                    }
                }

                if (currentReceiver == null)
                    return null;
                return new BuildingComponentPath<IItemReceiver>(currentReceiver.Reference, currentPath);
            }
        }

        [Tooltip("global item storage")]
        public ItemStorage ItemStorage;
        [Tooltip("items added to global storage when the game starts")]
        public ItemQuantity[] StartItems;
        [Tooltip("how much space remains in the receiver is considered when choosing a receiver instead of just going to the nearest one")]
        public bool PrioritizeEmptyReceivers;

        public ItemStorage Items => ItemStorage;

        private List<IItemsDispenser> _dispensers = new List<IItemsDispenser>();

        protected virtual void Awake()
        {
            Dependencies.Register<IGlobalStorage>(this);
            Dependencies.Register<IGiverPathfinder>(this);
            Dependencies.Register<IReceiverPathfinder>(this);
            Dependencies.Register<IItemsDispenserManager>(this);
        }

        protected virtual void Start()
        {
            StartItems.ForEach(i => ItemStorage.AddItems(i.Item, i.Quantity));
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (ItemStorage.Mode == ItemStorageMode.Global)
            {
                Debug.LogWarning("DefaultItemManager acts as GlobalStorage, its storage mode cannot be Global!");
                ItemStorage.Mode = ItemStorageMode.Free;
            }
        }
#endif

        public void Add(IItemsDispenser dispenser)
        {
            _dispensers.Add(dispenser);
        }
        public void Remove(IItemsDispenser dispenser)
        {
            _dispensers.Remove(dispenser);
        }

        public IItemsDispenser GetDispenser(string key, Vector3 position, float maxDistance)
        {
            return _dispensers
                .Where(d => d.Key == key)
                .Select(d => Tuple.Create(d, Vector3.Distance(d.Position, position)))
                .Where(d => d.Item2 < maxDistance)
                .OrderBy(d => d.Item2)
                .FirstOrDefault()?.Item1;
        }
        public bool HasDispenser(string key, Vector3 position, float maxDistance)
        {
            return _dispensers
                .Where(d => d.Key == key)
                .Select(d => Tuple.Create(d, Vector3.Distance(d.Position, position)))
                .Where(d => d.Item2 < maxDistance)
                .Any();
        }

        public BuildingComponentPath<IItemGiver> GetGiverPath(IBuilding building, Vector2Int? currentPoint, ItemQuantity items, float maxDistance, PathType pathType, object pathTag = null)
        {
            if (items == null)
                return null;

            Vector3 currentPosition;
            if (currentPoint.HasValue)
                currentPosition = Dependencies.Get<IGridPositions>().GetWorldPosition(currentPoint.Value);
            else
                currentPosition = building.WorldCenter;

            var givers = getGivers(building, currentPosition, items, maxDistance);
            if (givers.Count == 1)
            {
                var path = PathHelper.FindPath(building, currentPoint, givers.First.Value.Building, pathType, pathTag);
                if (path == null)
                    return null;

                return new BuildingComponentPath<IItemGiver>(givers.First.Value.Reference, path);
            }
            else if (givers.Count > 1)
            {
                IItemGiver currentGiver = null;
                WalkingPath currentPath = null;
                float currentDistance = float.MaxValue;

                foreach (var giver in givers)
                {
                    var distance = Vector2.Distance(giver.Building.WorldCenter, currentPosition);
                    if (isDiscarded(items, currentGiver, currentDistance, giver, distance))
                        continue;//building is further away then current path distance

                    var path = PathHelper.FindPath(building, currentPoint, giver.Building, pathType, pathTag);
                    if (path == null)
                        continue;

                    if (currentGiver == null || isMoreImportant(items, currentGiver, currentDistance, giver, distance))
                    {
                        currentGiver = giver;
                        currentPath = path;
                        currentDistance = path.GetDistance();
                    }
                }

                if (currentGiver != null)
                    return new BuildingComponentPath<IItemGiver>(currentGiver.Reference, currentPath);
            }

            return null;
        }
        public BuildingComponentPathQuery<IItemGiver> GetGiverPathQuery(IBuilding building, Vector2Int? currentPoint, ItemQuantity items, float maxDistance, PathType pathType, object pathTag = null)
        {
            if (items == null)
                return null;

            Vector3 currentPosition;
            if (currentPoint.HasValue)
                currentPosition = Dependencies.Get<IGridPositions>().GetWorldPosition(currentPoint.Value);
            else
                currentPosition = building.WorldCenter;

            var givers = getGivers(building, currentPosition, items, maxDistance);
            if (givers.Count == 0)
                return null;

            return new GiverPathQuery(this, currentPosition, items, givers.Select(g => new GiverPathCandidate()
            {
                Giver = g,
                Query = PathHelper.FindPathQuery(building, currentPoint, g.Building, pathType, pathTag)
            }).ToList());
        }
        private LinkedList<IItemGiver> getGivers(IBuilding building, Vector3 currentPosition, ItemQuantity items, float maxDistance)
        {
            var givers = new LinkedList<IItemGiver>();
            foreach (var itemGiver in Dependencies.Get<IBuildingManager>().GetBuildingTraits<IItemGiver>())
            {
                if (!isValid(building, items, itemGiver))
                    continue;//dont deliver to self

                var distance = Vector2.Distance(itemGiver.Building.WorldCenter, currentPosition);
                if (distance > maxDistance)
                    continue;//too far away

                if (itemGiver.GetGiveQuantity(items.Item) < items.Quantity)
                    continue;//does not have the items

                var node = givers.First;
                while (node != null && isMoreImportant(items, node.Value, Vector2.Distance(node.Value.Building.WorldCenter, currentPosition), itemGiver, distance))
                    node = node.Next;

                if (node == null)
                    givers.AddLast(itemGiver);
                else
                    givers.AddBefore(node, itemGiver);
            }
            return givers;
        }

        public BuildingComponentPath<IItemReceiver> GetReceiverPath(IBuilding building, Vector2Int? currentPoint, ItemQuantity items, float maxDistance, PathType pathType, object pathTag = null, int currentPriority = 0)
        {
            if (items == null)
                return null;

            Vector3 currentPosition;
            if (currentPoint.HasValue)
                currentPosition = Dependencies.Get<IGridPositions>().GetWorldPosition(currentPoint.Value);
            else
                currentPosition = building.WorldCenter;

            var receivers = getReceivers(building, currentPosition, items, maxDistance, currentPriority);
            if (receivers.Count == 1)
            {
                var path = PathHelper.FindPath(building, currentPoint, receivers.First.Value.Building, pathType, pathTag);
                if (path == null)
                    return null;

                return new BuildingComponentPath<IItemReceiver>(receivers.First.Value.Reference, path);
            }
            else if (receivers.Count > 1)
            {
                IItemReceiver currentReceiver = null;
                WalkingPath currentPath = null;
                float currentDistance = float.MaxValue;

                foreach (var receiver in receivers)
                {
                    var distance = Vector2.Distance(receiver.Building.WorldCenter, currentPosition);
                    if (isDiscarded(items, currentReceiver, currentDistance, receiver, distance))
                        continue;

                    var path = PathHelper.FindPath(building, currentPoint, receiver.Building, pathType, pathTag);
                    if (path == null)
                        continue;

                    if (currentReceiver == null || isMoreImportant(items, currentReceiver, currentDistance, receiver, distance))
                    {
                        currentReceiver = receiver;
                        currentPath = path;
                        currentDistance = path.GetDistance();
                    }
                }

                if (currentReceiver != null)
                    return new BuildingComponentPath<IItemReceiver>(currentReceiver.Reference, currentPath);
            }

            return null;
        }
        public BuildingComponentPathQuery<IItemReceiver> GetReceiverPathQuery(IBuilding building, Vector2Int? currentPoint, ItemQuantity items, float maxDistance, PathType pathType, object pathTag = null, int currentPriority = 0)
        {
            if (items == null)
                return null;

            Vector3 currentPosition;
            if (currentPoint.HasValue)
                currentPosition = Dependencies.Get<IGridPositions>().GetWorldPosition(currentPoint.Value);
            else
                currentPosition = building.WorldCenter;

            var receivers = getReceivers(building, currentPosition, items, maxDistance, currentPriority);
            if (receivers.Count == 0)
                return null;

            return new ReceiverPathQuery(this, currentPosition, items, receivers.Select(r => new ReceiverPathCandidate()
            {
                Receiver = r,
                Query = PathHelper.FindPathQuery(building, currentPoint, r.Building, pathType, pathTag)
            }).ToList());
        }
        private LinkedList<IItemReceiver> getReceivers(IBuilding building, Vector3 currentPosition, ItemQuantity items, float maxDistance, int currentPriority)
        {
            var receivers = new LinkedList<IItemReceiver>();
            foreach (var itemReceiver in Dependencies.Get<IBuildingManager>().GetBuildingTraits<IItemReceiver>())
            {
                if (!isValid(building, items, itemReceiver, currentPriority))
                    continue;

                if (!itemReceiver.GetReceiveItems().Contains(items.Item))
                    continue;

                var distance = Vector2.Distance(itemReceiver.Building.WorldCenter, currentPosition);
                if (distance > maxDistance)
                    continue;//too far away

                var missing = itemReceiver.GetReceiveCapacityRemaining(items.Item) / (float)items.Item.UnitSize;
                if (missing < 1f)
                    continue;//not enough space remaining

                var node = receivers.First;
                while (node != null && isMoreImportant(items, node.Value, Vector2.Distance(node.Value.Building.WorldCenter, currentPosition), itemReceiver, distance))
                    node = node.Next;

                if (node == null)
                    receivers.AddLast(itemReceiver);
                else
                    receivers.AddBefore(node, itemReceiver);
            }
            return receivers;
        }

        protected virtual bool isValid(IBuilding building, ItemQuantity items, IItemGiver giver)
        {
            if (giver.Building == building)
                return false;//dont deliver to self

            return true;
        }
        protected virtual bool isValid(IBuilding building, ItemQuantity items, IItemReceiver receiver, int currentPriority = 0)
        {
            if (receiver.Building == building)
                return false;//dont deliver to self

            if (receiver.Priority <= currentPriority)
                return false;//receiver has same or lower priority then the current storage

            return true;
        }

        protected virtual bool isDiscarded(ItemQuantity items, IItemGiver currentGiver, float currentPathDistance, IItemGiver potentialGiver, float potentialBuildingDistance)
        {
            if (currentGiver == null)
                return false;
            if (potentialGiver == null)
                return true;

            return potentialBuildingDistance > currentPathDistance;//building is further away than path distance, no need to calculate a path for b
        }
        protected virtual bool isDiscarded(ItemQuantity items, IItemReceiver currentReceiver, float currentPathDistance, IItemReceiver potentialReceiver, float potentialBuildingDistance)
        {
            if (currentReceiver == null)
                return false;
            if (potentialReceiver == null)
                return true;

            if (potentialReceiver.Priority < currentReceiver.Priority)
                return true;

            if (PrioritizeEmptyReceivers)
            {
                var currentMissing = (float)currentReceiver.GetReceiveCapacityRemaining(items.Item) / items.Item.UnitSize;
                var potentialMissing = (float)potentialReceiver.GetReceiveCapacityRemaining(items.Item) / items.Item.UnitSize;

                var currentScore = currentMissing * 2f - currentPathDistance;
                var potentialScore = potentialMissing * 2f - potentialBuildingDistance;

                return potentialScore > currentScore;
            }
            else
            {
                return potentialBuildingDistance > currentPathDistance;
            }
        }

        protected virtual bool isMoreImportant(ItemQuantity items, IItemGiver currentGiver, float currentPathDistance, IItemGiver potentialGiver, float potentialPathDistance)
        {
            if (currentGiver == null)
                return true;
            if (potentialGiver == null)
                return false;

            return potentialPathDistance < currentPathDistance;
        }
        protected virtual bool isMoreImportant(ItemQuantity items, IItemReceiver currentReceiver, float currentPathDistance, IItemReceiver potentialReceiver, float potentialPathDistance)
        {
            if (currentReceiver == null)
                return true;
            if (potentialReceiver == null)
                return false;

            if (potentialReceiver.Priority > currentReceiver.Priority)
                return true;

            if (PrioritizeEmptyReceivers)
            {
                var currentMissing = (float)currentReceiver.GetReceiveCapacityRemaining(items.Item) / items.Item.UnitSize;
                var potentialMissing = (float)potentialReceiver.GetReceiveCapacityRemaining(items.Item) / items.Item.UnitSize;

                var currentScore = currentMissing * 2f - currentPathDistance;
                var potentialScore = potentialMissing * 2f - potentialPathDistance;

                return potentialScore > currentScore;
            }
            else
            {
                return potentialPathDistance < currentPathDistance;
            }
        }
    }
}