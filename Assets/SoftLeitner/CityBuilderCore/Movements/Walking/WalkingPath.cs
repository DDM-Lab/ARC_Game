using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// collection of map points or world positions that can be followed by a walker
    /// </summary>
    public class WalkingPath
    {
        /// <summary>
        /// how many points/positions the path contains, not physical world distance
        /// </summary>
        public int Length => _isPointPath ? _points.Length : _positions.Length;

        /// <summary>
        /// first map point in the path
        /// </summary>
        public Vector2Int StartPoint => _isPointPath ? _points.FirstOrDefault() : _grid.GetGridPoint(StartPosition);
        /// <summary>
        /// first world position in the path
        /// </summary>
        public Vector3 StartPosition => _isPointPath ? _grid.GetWorldPosition(StartPoint) : _positions.FirstOrDefault();

        /// <summary>
        /// last map point in the path
        /// </summary>
        public Vector2Int EndPoint => _isPointPath ? _points.LastOrDefault() : _grid.GetGridPoint(EndPosition);
        /// <summary>
        /// last world position in the path
        /// </summary>
        public Vector3 EndPosition => _isPointPath ? _grid.GetWorldPosition(EndPoint) : _positions.LastOrDefault();

        private bool _isPointPath;
        private Vector2Int[] _points;
        private Vector3[] _positions;

        private IGridPositions _grid;

        public WalkingPath(Vector2Int[] points)
        {
            _grid = Dependencies.Get<IGridPositions>();

            _isPointPath = true;
            _points = points;
        }
        public WalkingPath(Vector3[] positions)
        {
            _grid = Dependencies.Get<IGridPositions>();

            _isPointPath = false;
            _positions = positions;
        }

        /// <summary>
        /// gets all the points in the path in order
        /// </summary>
        /// <returns>map points</returns>
        public IEnumerable<Vector2Int> GetPoints()
        {
            for (int i = 0; i < Length; i++)
            {
                yield return GetPoint(i);
            }
        }
        /// <summary>
        /// gets the map point at a specific position
        /// </summary>
        /// <param name="index">which point to get, starting at 0</param>
        /// <returns>a single map point</returns>
        public Vector2Int GetPoint(int index)
        {
            if (_isPointPath)
                return _points.ElementAtOrDefault(index);
            else
                return _grid.GetGridPoint(GetPosition(index));
        }

        /// <summary>
        /// gets all the positions in the path in order
        /// </summary>
        /// <returns>world positions</returns>
        public IEnumerable<Vector3> GetPositions()
        {
            for (int i = 0; i < Length; i++)
            {
                yield return GetPosition(i);
            }
        }
        /// <summary>
        /// gets the world position at a specific position
        /// </summary>
        /// <param name="index">which position to get, starting at 0</param>
        /// <returns>a single world position</returns>
        public Vector3 GetPosition(int index)
        {
            if (_isPointPath)
                return _grid.GetWorldPosition(GetPoint(index));
            else
                return _positions.ElementAtOrDefault(index);
        }

        /// <summary>
        /// interpolates a position between the one at the index and the next one
        /// </summary>
        /// <param name="index">index of the start point</param>
        /// <param name="time">how long the walker has already walked</param>
        /// <param name="timePerStep">total time the walker needs to walk between the two points</param>
        /// <returns>a world position</returns>
        public Vector3 GetPosition(int index, float time, float timePerStep) => Vector3.Lerp(GetPreviousPosition(index), GetNextPosition(index), time / timePerStep);

        /// <summary>
        /// checks if the index points to the last element in the path or beyond
        /// </summary>
        /// <param name="index"></param>
        /// <returns>true if there is no next point</returns>
        public bool HasEnded(int index) => index >= Length - 1;
        /// <summary>
        /// returns the starting point of the n-th segment in the path
        /// </summary>
        /// <param name="index">index of the segment(0 is the segment between points 0 and 1)</param>
        /// <returns>a map point</returns>
        public Vector2Int GetPreviousPoint(int index)
        {
            if (_isPointPath)
                return _points.ElementAtOrDefault(index);
            else
                return _grid.GetGridPoint(GetPreviousPosition(index));
        }
        /// <summary>
        /// returns the ending point of the n-th segment in the path
        /// </summary>
        /// <param name="index">index of the segment(0 is the segment between points 0 and 1)</param>
        /// <returns>a map point</returns>
        public Vector2Int GetNextPoint(int index)
        {
            if (_isPointPath)
                return index + 1 < _points.Length ? _points.ElementAtOrDefault(index + 1) : _points.Last();
            else
                return _grid.GetGridPoint(GetNextPosition(index));
        }
        /// <summary>
        /// returns the starting position of the n-th segment in the path
        /// </summary>
        /// <param name="index">index of the segment(0 is the segment between points 0 and 1)</param>
        /// <returns>a world position</returns>
        public Vector3 GetPreviousPosition(int index)
        {
            if (_isPointPath)
                return _grid.GetWorldPosition(GetPreviousPoint(index));
            else
                return _positions.ElementAtOrDefault(index);
        }
        /// <summary>
        /// returns the ending position of the n-th segment in the path
        /// </summary>
        /// <param name="index">index of the segment(0 is the segment between points 0 and 1)</param>
        /// <returns>a world position</returns>
        public Vector3 GetNextPosition(int index)
        {
            if (_isPointPath)
                return _grid.GetWorldPosition(GetNextPoint(index));
            else
                return index + 1 < _positions.Length ? _positions.ElementAtOrDefault(index + 1) : _positions.Last();
        }
        /// <summary>
        /// returns the length of the n-th segment in the path
        /// </summary>
        /// <param name="index">index of the segment(0 is the segment between points 0 and 1)</param>
        /// <returns>world distance between the points</returns>
        public float GetDistance(int index) => Vector3.Distance(GetPreviousPosition(index), GetNextPosition(index));
        /// <summary>
        /// returns the direction of the n-th segment in the path
        /// </summary>
        /// <param name="index">index of the segment(0 is the segment between points 0 and 1)</param>
        /// <returns>a vector(end-start)</returns>
        public Vector3 GetDirection(int index)
        {
            return GetNextPosition(index) - GetPreviousPosition(index);
        }

        /// <summary>
        /// calculates the total length of the path in the world
        /// </summary>
        /// <returns></returns>
        public float GetDistance()
        {
            float distance = 0f;
            for (int i = 0; i < Length; i++)
            {
                distance += GetDistance(i);
            }
            return distance;
        }

        /// <summary>
        /// creates a new path going in the opposite direction of this one
        /// </summary>
        /// <returns>the reversed path</returns>
        public WalkingPath GetReversed()
        {
            if (_isPointPath)
                return new WalkingPath(_points.Reverse().ToArray());
            else
                return new WalkingPath(_positions.Reverse().ToArray());
        }

        /// <summary>
        /// initializes the walkers walking state and starts moving the walker along the walking path
        /// </summary>
        /// <param name="walker">the walker that will be moved</param>
        /// <param name="delay">duration to wait before starting to walk in seconds(immediately moving can look abrupt)</param>
        /// <param name="finished">called when walking is finished either by reaching the end or cancellation</param>
        /// <param name="moved">called whenever the walker reaches on of the path points</param>
        /// <returns>the coroutine enumerator</returns>
        public IEnumerator Walk(Walker walker, float delay, Action finished, Action<Vector2Int> moved = null)
        {
            walker.transform.position = StartPosition;

            if (Length > 1)
            {
                walker.onDirectionChanged(GetDirection(0));
            }

            walker.CurrentWalking = new WalkingState(this);

            if (delay > 0f)
            {
                walker.CurrentWaiting = new WaitingState(walker.Info.Delay);
            }

            yield return ContinueWalk(walker, finished, moved);
        }
        /// <summary>
        /// when the game is loaded and the walker was previously walking it is continued here
        /// </summary>
        /// <param name="walker">the walker that will be moved</param>
        /// <param name="finished">called when walking is finished either by reaching the end or cancellation</param>
        /// <param name="moved">called whenever the walker reaches on of the path points</param>
        /// <returns>the coroutine enumerator</returns>
        public IEnumerator ContinueWalk(Walker walker, Action finished, Action<Vector2Int> moved = null)
        {
            var heights = Dependencies.GetOptional<IGridHeights>();

            heights?.ApplyHeight(walker.Pivot, walker.PathType, walker.HeightOverride);

            if (walker.CurrentWaiting != null)
            {
                yield return walker.CurrentWaiting.Wait();
                walker.CurrentWaiting = null;
            }

            walker.IsWalking = true;

            var rotations = Dependencies.GetOptional<IGridRotations>();
            var w = walker.CurrentWalking;

            var last = GetPreviousPosition(w.Index);
            var next = GetNextPosition(w.Index);

            float distance;

            var link = PathHelper.GetLink(GetPreviousPoint(w.Index), GetNextPoint(w.Index), walker.PathType, walker.PathTag);
            if (link == null)
                distance = GetDistance(w.Index);
            else
                distance = link.Distance;

            while (true)
            {
                w.Moved += Time.deltaTime * walker.Speed;

                if (w.Moved >= distance)
                {
                    moved?.Invoke(GetNextPoint(w.Index));

                    w.Moved -= distance;
                    w.Index++;

                    if (w.IsCanceled || HasEnded(w.Index))
                    {
                        if (!w.IsCanceled)
                            walker.transform.position = EndPosition;
                        walker.CurrentWalking = null;
                        walker.IsWalking = false;
                        finished();
                        yield break;
                    }
                    else
                    {
                        last = GetPreviousPosition(w.Index);
                        next = GetNextPosition(w.Index);

                        link = PathHelper.GetLink(GetPreviousPoint(w.Index), GetNextPoint(w.Index), walker.PathType, walker.PathTag);
                        if (link == null)
                            distance = GetDistance(w.Index);
                        else
                            distance = link.Distance;

                        walker.onDirectionChanged(GetDirection(w.Index));
                    }
                }

                if (link == null)
                {
                    var position = Vector3.Lerp(last, next, w.Moved / distance);

                    walker.transform.position = position;
                    heights?.ApplyHeight(walker.Pivot, walker.PathType, walker.HeightOverride);
                }
                else
                {
                    link.Walk(walker, w.Moved, GetPreviousPoint(w.Index));
                }

                yield return null;
            }
        }

        /// <summary>
        /// initializes the walkers agent walking state and starts setting destinations for the navmesh agent along the path
        /// </summary>
        /// <param name="walker">the walker that will be moved</param>
        /// <param name="delay">duration to wait before starting to walk in seconds(immediately moving can look abrupt)</param>
        /// <param name="finished">called when walking is finished either by reaching the end or cancellation</param>
        /// <param name="moved">called whenever the walker reaches on of the path points</param>
        /// <returns>the coroutine enumerator</returns>
        public IEnumerator WalkAgent(Walker walker, float delay, Action finished, Action<Vector2Int> moved = null, float distance = 0f)
        {
            var targetPosition = _grid.GetCenterFromPosition(EndPosition);
            var gridHeight = Dependencies.GetOptional<IGridHeights>();
            if (gridHeight != null)
                targetPosition = gridHeight.ApplyHeight(targetPosition, walker.PathType);
            return WalkAgent(walker, targetPosition, delay, finished, moved, distance);
        }
        /// <summary>
        /// initializes the walkers agent walking state and starts setting destinations for the navmesh agent along the path
        /// </summary>
        /// <param name="walker">the walker that will be moved</param>
        /// <param name="delay">duration to wait before starting to walk in seconds(immediately moving can look abrupt)</param>
        /// <param name="finished">called when walking is finished either by reaching the end or cancellation</param>
        /// <param name="moved">called whenever the walker reaches on of the path points</param>
        /// <returns>the coroutine enumerator</returns>
        public static IEnumerator WalkAgent(Walker walker, Vector3 destination, float delay, Action finished, Action<Vector2Int> moved = null, float distance = 0f)
        {
            destination += Dependencies.Get<IMap>().GetVariance();

            walker.CurrentAgentWalking = new WalkingAgentState(destination, distance);

            if (delay > 0f)
            {
                walker.CurrentWaiting = new WaitingState(walker.Info.Delay);
            }

            yield return ContinueWalkAgent(walker, finished, moved);
        }
        /// <summary>
        /// when the game is loaded and the walker was previously walking it is continued here
        /// </summary>
        /// <param name="walker">the walker that will be moved</param>
        /// <param name="finished">called when walking is finished either by reaching the end or cancellation</param>
        /// <param name="moved">called whenever the walker reaches on of the path points</param>
        /// <returns>the coroutine enumerator</returns>
        public static IEnumerator ContinueWalkAgent(Walker walker, Action finished, Action<Vector2Int> moved = null)
        {
            if (walker.CurrentWaiting != null)
            {
                yield return walker.CurrentWaiting.Wait();
                walker.CurrentWaiting = null;
            }

            var saver = Dependencies.GetOptional<IGameSaver>();
            while (saver != null && saver.IsLoading)
                yield return null;//nav mesh may be invalid

            walker.IsWalking = true;
            walker.Agent.enabled = true;
            walker.Agent.stoppingDistance = walker.CurrentAgentWalking.Distance;
            walker.Agent.SetDestination(walker.CurrentAgentWalking.Destination);

            if (walker.Agent.velocity != Vector3.zero)
                walker.Agent.velocity = walker.CurrentAgentWalking.Velocity;

            yield return null;

            while (true)
            {
                walker.CurrentAgentWalking.Velocity = walker.Agent.velocity;

                if (hasAgentFinished(walker))
                {
                    walker.Agent.enabled = false;

                    var position = walker.Pivot.position;
                    var point = walker.GridPoint;

                    walker.transform.position = Dependencies.Get<IGridPositions>().GetWorldPosition(point);
                    walker.Pivot.position = position;

                    walker.CurrentAgentWalking = null;
                    walker.IsWalking = false;

                    moved?.Invoke(point);

                    finished();
                    yield break;
                }

                yield return null;
            }
        }
        private static bool hasAgentFinished(Walker walker)
        {
            if (walker.CurrentAgentWalking.IsCanceled)
                return true;
            if (!walker.Agent.pathPending && !walker.Agent.hasPath)
                return true;
            if (Vector3.Distance(walker.Agent.transform.position, walker.CurrentAgentWalking.Destination) <= walker.CurrentAgentWalking.Distance)
                return true;
            return false;
        }

        /// <summary>
        /// tries to walk a path that is calculated externaly, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <param name="walker">the walker that is moved</param>
        /// <param name="delay">duration in seconds the walker will always wait before moving(instead of the default <see cref="WalkerInfo.Delay"/>)</param>
        /// <param name="pathGetter">function that returns the path the walker should walk or null if no path could be found currently</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        /// <param name="moved">called whenever the walker reaches on of the path points</param>
        /// <returns>the coroutine enumerator</returns>
        public static IEnumerator TryWalk(Walker walker, float delay, Func<WalkingPath> pathGetter, Action planned, Action finished, Action canceled = null, Action<Vector2Int> moved = null)
        {
            if (walker.CurrentWaiting == null)
                walker.CurrentWaiting = new WaitingState(walker.Info.MaxWait);

            do
            {
                var path = pathGetter();
                if (path != null)
                {
                    var remainingDelay = delay - walker.CurrentWaiting.WaitTime;
                    walker.CurrentWaiting = null;
                    planned?.Invoke();
                    yield return path.Walk(walker, remainingDelay, finished, moved);
                    yield break;
                }

                yield return new WaitForSeconds(1f);
                walker.CurrentWaiting.WaitTime++;
            }
            while (!walker.CurrentWaiting.IsFinished);

            walker.CurrentWaiting = null;
            if (canceled == null)
                finished();
            else
                canceled();
        }

        public static IEnumerator TryWalk(Walker walker, float delay, Func<PathQuery> queryGetter, Action planned, Action finished, Action canceled = null, Action<Vector2Int> moved = null)
        {
            if (walker.CurrentWaiting == null)
                walker.CurrentWaiting = new WaitingState(walker.Info.MaxWait);

            do
            {
                var query = queryGetter();

                if (delay > 0f)
                    yield return new WaitForSeconds(delay);

                var result = query?.Complete();
                if (result != null)
                {
                    var remainingDelay = delay - walker.CurrentWaiting.WaitTime;
                    walker.CurrentWaiting = null;
                    planned?.Invoke();
                    yield return result.Walk(walker, remainingDelay, finished, moved);
                    yield break;
                }

                walker.CurrentWaiting.WaitTime += delay;

                if (delay <= 0f)
                    delay = 1f;
            }
            while (!walker.CurrentWaiting.IsFinished);

            walker.CurrentWaiting = null;
            if (canceled == null)
                finished();
            else
                canceled();
        }

        public static IEnumerator TryWalk<T>(Walker walker, float delay, Func<BuildingComponentPathQuery<T>> queryGetter, Action<BuildingComponentPath<T>> planned, Action finished, Action canceled = null, Action<Vector2Int> moved = null)
            where T : IBuildingComponent
        {
            if (walker.CurrentWaiting == null)
                walker.CurrentWaiting = new WaitingState(walker.Info.MaxWait);

            do
            {
                var query = queryGetter();

                if (delay > 0f)
                    yield return new WaitForSeconds(delay);

                var result = query?.Complete();
                if (result != null)
                {
                    var remainingDelay = delay - walker.CurrentWaiting.WaitTime;
                    walker.CurrentWaiting = null;
                    planned?.Invoke(result);
                    yield return result.Path.Walk(walker, remainingDelay, finished, moved);
                    yield break;
                }

                walker.CurrentWaiting.WaitTime += delay;

                if (delay <= 0f)
                    delay = 1f;
            }
            while (!walker.CurrentWaiting.IsFinished);

            walker.CurrentWaiting = null;
            if (canceled == null)
                finished();
            else
                canceled();
        }

        public static IEnumerator TryWalk<Q, P>(Walker walker, float delay, Func<Q> preparer, Func<Q, P> planner, Func<P, WalkingPath> planned, Action finished, Action canceled = null, Action<Vector2Int> moved = null)
        {
            if (walker.CurrentWaiting == null)
                walker.CurrentWaiting = new WaitingState(walker.Info.MaxWait);

            do
            {
                var query = preparer();

                if (delay > 0f)
                    yield return new WaitForSeconds(delay);

                if (query != null)
                {
                    var result = planner(query);
                    if (result != null)
                    {
                        var remainingDelay = delay - walker.CurrentWaiting.WaitTime;
                        walker.CurrentWaiting = null;
                        var path = planned(result);
                        yield return path.Walk(walker, remainingDelay, finished, moved);
                        yield break;
                    }
                }

                walker.CurrentWaiting.WaitTime += delay;

                if (delay <= 0f)
                    delay = 1f;
            }
            while (!walker.CurrentWaiting.IsFinished);

            walker.CurrentWaiting = null;
            if (canceled == null)
                finished();
            else
                canceled();
        }

        /// <summary>
        /// tries to walk to a structure, if no path is found the walker waits a second and retries until <see cref="WalkerInfo.MaxWait"/> times it out
        /// </summary>
        /// <param name="walker">the walker that is moved</param>
        /// <param name="delay">duration in seconds the walker will always wait before moving(instead of the default <see cref="WalkerInfo.Delay"/>)</param>
        /// <param name="start">the point to start from</param>
        /// <param name="structure">the target structure to walk to</param>
        /// <param name="pathType">which kind of pathfinding will be performed to check which points are available</param>
        /// <param name="pathTag">additional parameter for pathfinding</param>
        /// <param name="planned">called when a path is found</param>
        /// <param name="finished">called when the walker reaches its target</param>
        /// <param name="canceled">called if the walker finds no path and times out or waiting gets canceled</param>
        /// <param name="moved">called whenever the walker reaches on of the path points</param>
        /// <returns>the coroutine enumerator</returns>
        public static IEnumerator TryWalk(Walker walker, float delay, IBuilding structure, PathType pathType, object pathTag, Action planned, Action finished, Action canceled = null, Action<Vector2Int> moved = null)
        {
            yield return TryWalk(walker, delay, () => PathHelper.FindPathQuery(walker.CurrentPoint, structure, pathType, pathTag), planned, finished, canceled, moved);
        }

        /// <summary>
        /// walker starts walking around semi randomly, visited points are memorized and avoided
        /// </summary>
        /// <param name="walker">the walker to move</param>
        /// <param name="delay">duration in seconds the walker waits before starting to roam</param>
        /// <param name="start">the point to start roaming from</param>
        /// <param name="memoryLength">how many points the walker will memorize and try to avoid</param>
        /// <param name="range">how many steps the walker roams before returning</param>
        /// <param name="pathType">which kind of pathfinding will be performed to check which points are available</param>
        /// <param name="pathTag">additional parameter for pathfinding</param>
        /// <param name="finished">called when roaming is finished(regularly or canceled)</param>
        /// <param name="moved">called whenever a new point is reached</param>
        /// <returns>the coroutine enumarator</returns>
        public static IEnumerator Roam(Walker walker, float delay, Vector2Int start, int memoryLength, int range, PathType pathType, object pathTag, Action finished, Action<Vector2Int> moved = null)
        {
            walker.CurrentRoaming = new RoamingState();

            if (delay > 0f)
            {
                walker.CurrentWaiting = new WaitingState(walker.Info.Delay);
            }

            var roaming = walker.CurrentRoaming;

            roaming.Steps = 0;
            roaming.Moved = 0f;

            memorize(start, roaming.Memory, memoryLength);

            roaming.Current = start;
            roaming.Next = roam(roaming.Current, roaming.Memory, pathType, pathTag);

            walker.transform.position = Dependencies.Get<IGridPositions>().GetWorldPosition(roaming.Current);

            yield return null;

            yield return ContinueRoam(walker, memoryLength, range, pathType, pathTag, finished, moved);
        }
        /// <summary>
        /// continues roaming when the game is loaded and the walker was roaming previously
        /// </summary>
        /// <param name="walker">the walker to move</param>
        /// <param name="memoryLength">how many points the walker will memorize and try to avoid</param>
        /// <param name="range">how many steps the walker roams before returning</param>
        /// <param name="pathType">which kind of pathfinding will be performed to check which points are available</param>
        /// <param name="pathTag">additional parameter for pathfinding</param>
        /// <param name="finished">called when roaming is finished(regularly or canceled)</param>
        /// <param name="moved">called whenever a new point is reached</param>
        /// <returns>the coroutine enumarator</returns>
        public static IEnumerator ContinueRoam(Walker walker, int memoryLength, int range, PathType pathType, object pathTag, Action finished, Action<Vector2Int> moved = null)
        {
            var heights = Dependencies.GetOptional<IGridHeights>();

            heights?.ApplyHeight(walker.Pivot, walker.PathType, walker.HeightOverride);

            if (walker.CurrentWaiting != null)
            {
                yield return walker.CurrentWaiting.Wait();
                walker.CurrentWaiting = null;
            }

            walker.IsWalking = true;

            var positions = Dependencies.Get<IGridPositions>();
            var rotations = Dependencies.GetOptional<IGridRotations>();
            var roaming = walker.CurrentRoaming;

            var from = positions.GetWorldPosition(roaming.Current);
            var to = positions.GetWorldPosition(roaming.Next);

            float distance;

            var link = PathHelper.GetLink(roaming.Current, roaming.Next, walker.PathType, walker.PathTag);
            if (link == null)
                distance = Vector3.Distance(from, to);
            else
                distance = link.Distance;

            while (roaming.Steps < range)
            {
                roaming.Moved += Time.deltaTime * walker.Speed;

                if (roaming.Moved >= distance)
                {
                    moved?.Invoke(roaming.Next);

                    roaming.Moved -= distance;
                    roaming.Current = roaming.Next;

                    roaming.Steps++;

                    if (roaming.Steps >= range || roaming.IsCanceled)
                    {
                        break;
                    }
                    else
                    {
                        memorize(roaming.Current, roaming.Memory, memoryLength);
                        roaming.Next = roam(roaming.Current, roaming.Memory, pathType, pathTag);
                    }

                    from = positions.GetWorldPosition(roaming.Current);
                    to = positions.GetWorldPosition(roaming.Next);

                    link = PathHelper.GetLink(roaming.Current, roaming.Next, walker.PathType, walker.PathTag);
                    if (link == null)
                        distance = Vector3.Distance(from, to);
                    else
                        distance = link.Distance;
                }


                if (link == null)
                {
                    var position = Vector3.Lerp(from, to, roaming.Moved / distance);

                    walker.transform.position = position;
                    heights?.ApplyHeight(walker.Pivot, walker.PathType, walker.HeightOverride);

                    walker.onDirectionChanged(to - from);
                }
                else
                {
                    link.Walk(walker, roaming.Moved, roaming.Current);
                }

                yield return null;
            }

            walker.transform.position = positions.GetWorldPosition(roaming.Current);
            yield return null;

            walker.CurrentRoaming = null;
            walker.IsWalking = false;
            finished();
        }
        private static void memorize(Vector2Int point, List<Vector2Int> memory, int maxMemory)
        {
            memory.Remove(point);
            memory.Add(point);

            while (memory.Count > maxMemory)
                memory.RemoveAt(0);
        }
        private static Vector2Int roam(Vector2Int current, List<Vector2Int> memory, PathType pathType, object pathTag = null)
        {
            var options = PathHelper.GetAdjacent(current, pathType, pathTag).ToArray();

            if (options.Length == 0)
            {
                return current;
            }

            var firstTime = options.Where(o => !memory.Contains(o)).ToArray();

            if (firstTime.Length == 0)
            {
                return options.OrderBy(o => memory.IndexOf(o)).First();
            }
            else if (firstTime.Length == 1)
            {
                return firstTime[0];
            }
            else
            {
                return firstTime[UnityEngine.Random.Range(0, firstTime.Length)];
            }
        }

        #region Saving
        [Serializable]
        public class WalkingPathData
        {
            public bool IsPointPath;
            public bool IsCanceled;
            public List<Vector2Int> Points;
            public List<Vector3> Positions;

            public WalkingPath GetPath() => FromData(this);
        }

        public WalkingPathData GetData()
        {
            return new WalkingPathData()
            {
                IsPointPath = _isPointPath,
                Points = _isPointPath ? _points.ToList() : null,
                Positions = _isPointPath ? null : _positions?.ToList()
            };
        }
        public static WalkingPath FromData(WalkingPathData data)
        {
            if (data == null)
                return null;

            if (data.IsPointPath)
                return new WalkingPath(data.Points.ToArray());
            else
                return new WalkingPath(data.Positions.ToArray());
        }
        #endregion
    }
}