using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// can be used to define different walking parameters(walkable tiles) for different walkers<br/>
    /// check out the test scene in CityBuilderCore.Tests/City/Movements/StructurePathDebugging
    [Serializable]
    public class StructurePathOption
    {
        [Tooltip("this is used as the key, the same one has to be set in WalkerInfo for these options to be used")]
        public UnityEngine.Object Tag;
        [Tooltip("determines which structures block this pathing")]
        public StructureLevelMask Level;
        [Tooltip("an object the maps ground has to exhibit to be able to path(TILES when using the included maps)")]
        public UnityEngine.Object[] GroundOptions;
    }
}
