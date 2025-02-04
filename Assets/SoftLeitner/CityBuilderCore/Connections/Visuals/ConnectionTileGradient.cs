using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// colors tiles on a tilemap based on a connection passer<br/>
    /// in urban this is used to visualize whether water/electricity is running through the pipes/power lines
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/connections">https://citybuilder.softleitner.com/manual/connections</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connection_tile_gradient.html")]
    public class ConnectionTileGradient : MonoBehaviour
    {
        [Tooltip("the passer whose points will be used")]
        public ConnectionPasserBase ConnectionPasser;
        [Tooltip("the tilemap the color is applied to")]
        public Tilemap Tilemap;
        [Tooltip("gradient of color that will be applied")]
        public Gradient Gradient;
        [Tooltip("maximum value the gradiant is scaled to")]
        public int Maximum;

        private void Awake()
        {
            if (ConnectionPasser)
                ConnectionPasser.PointValueChanged.AddListener(new UnityAction<Vector2Int, int>(Apply));
        }

        public void Apply(Vector2Int point, int value)
        {
            Tilemap.SetTileFlags((Vector3Int)point, TileFlags.None);
            Tilemap.SetColor((Vector3Int)point, Gradient.Evaluate(value / (float)Maximum));
        }
    }
}
