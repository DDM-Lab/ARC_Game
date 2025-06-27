using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walks leaving population out of the map
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_emigration_walker.html")]
    public class EmigrationWalker : Walker
    {
        [Tooltip("quantity of population the emigration walker can take, influences how fast people can leave)")]
        public int Capacity;

        public void StartEmigrating(WalkingPath path)
        {
            Walk(path);
        }
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualEmigrationWalkerSpawner : ManualWalkerSpawner<EmigrationWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class CyclicEmigrationWalkerSpawner : CyclicWalkerSpawner<EmigrationWalker> { }
    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class PooledEmigrationWalkerSpawner : PooledWalkerSpawner<EmigrationWalker> { }
}