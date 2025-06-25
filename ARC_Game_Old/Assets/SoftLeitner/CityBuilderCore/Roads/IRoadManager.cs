using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    public interface IRoadManager : ISaveData
    {
        void Add(IEnumerable<Vector2Int> points, Road road);
        void Register(IEnumerable<Vector2Int> points, Road road);
        void Deregister(IEnumerable<Vector2Int> points, Road road);

        void RegisterSwitch(Vector2Int point, Road roadA, Road roadB);
        void RegisterSwitch(Vector2Int entry, Vector2Int point, Vector2Int exit, Road roadEntry, Road roadExit);

        bool CheckRequirement(Vector2Int point, RoadRequirement requirement);

        void Block(IEnumerable<Vector2Int> points, Road road = null);
        void Unblock(IEnumerable<Vector2Int> points, Road road = null);
        void BlockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags, Road road = null);
        void UnblockTags(IEnumerable<Vector2Int> points, IEnumerable<object> tags, Road road = null);
    }
}