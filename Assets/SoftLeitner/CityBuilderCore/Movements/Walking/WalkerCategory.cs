using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// category for bundling and filtering walkers, used in <see cref="WalkerScore"/><br/>
    /// <br/>
    /// may be used for grouping road blocking by setting it as PathTag in WalkerInfo and adding it to RoadBlockerComponent Tags<br/>
    /// in this case the actual objects in the collection dont matter, walking is blocked purely by the key of this object
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_category.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(WalkerCategory))]
    public class WalkerCategory : KeyedObject
    {
        [Tooltip("name used when refering to a singular building of the category('you need to build a X')")]
        public string NameSingular;
        [Tooltip("name used when refering to multiple buildings of the category('you need to build 10 Y')")]
        public string NamePlural;
        [Tooltip("collection of all the buildings in the category")]
        public WalkerInfo[] Walkers;

        private HashSet<WalkerInfo> _walkers;

        public bool Contains(WalkerInfo walker)
        {
            if (_walkers == null)
                _walkers = new HashSet<WalkerInfo>(Walkers);
            return _walkers.Contains(walker);
        }

        public string GetName(int quantity)
        {
            if (quantity > 1)
                return $"{quantity} {NamePlural}";
            else
                return NameSingular;
        }
    }
}