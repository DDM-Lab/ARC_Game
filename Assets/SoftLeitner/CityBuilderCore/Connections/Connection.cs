using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// Connections are networks of values on the Map<br/>
    /// they consist of feeders which determine the values and passers that just pass them along
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/connections">https://citybuilder.softleitner.com/manual/connections</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connection.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(Connection))]
    public class Connection : KeyedObject
    {
        [Tooltip("name may be used in UI")]
        public string Name;

        [Tooltip("optional layer the connection can spread its values onto")]
        public Layer Layer;
        [Tooltip("range of points outside the affector the value carries without falling off")]
        public int LayerRange;
        [Tooltip("value subtracted for every step outside the range")]
        public int LayerFalloff;
    }
}
