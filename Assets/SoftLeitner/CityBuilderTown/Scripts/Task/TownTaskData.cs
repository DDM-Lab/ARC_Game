using System;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// class used when persisting tasks<br/>
    /// tasks that have state of their own can override <see cref="TownTask.saveExtras"/> and <see cref="TownTask.loadExtras(string)"/>
    /// </summary>
    [Serializable]
    public class TownTaskData
    {
        public string Id;
        public string Key;
        public Vector2Int Point;
        public string Extras;
    }
}
