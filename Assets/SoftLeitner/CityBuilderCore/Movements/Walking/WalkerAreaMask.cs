using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// object that just contains a NavMesh area mask<br/>
    /// can be used as a tag for pathfinding(<see cref="WalkerInfo.PathTag"/>)<br/>
    /// so the walker only walks on certain NavMesh areas
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_area_mask.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(WalkerAreaMask))]
    public class WalkerAreaMask : ScriptableObject
    {
        public int AreaMask = -1;
    }
}
