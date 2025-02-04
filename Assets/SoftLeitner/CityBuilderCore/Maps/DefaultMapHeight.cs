using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// sets the heights of entities depending on whether an entity is on a road or the map<br/>
    /// a terrain can be defined that will be sampled and added for entities on the map
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_map_height.html")]
    public class DefaultMapHeight : MonoBehaviour, IGridHeights
    {
        [Tooltip("optional terrain that will sampled and added to heights")]
        public Terrain Terrain;
        [Tooltip("height for entities on roads")]
        public float RoadHeight;
        [Tooltip("height for entities not on roads, can be modified by terrain")]
        public float MapHeight;

        private LazyDependency<IMap> _map = new LazyDependency<IMap>();

        private void Awake()
        {
            Dependencies.Register<IGridHeights>(this);
        }

        public float GetHeight(Vector3 position, PathType pathType = PathType.Map)
        {
            float height;

            switch (pathType)
            {
                case PathType.Road:
                case PathType.RoadBlocked:
                    height = RoadHeight;
                    break;
                default:
                    height = MapHeight;
                    break;
            }

            if (Terrain)
                height += Terrain.SampleHeight(position);

            return height;
        }

        public void ApplyHeight(Transform transform, Vector3 position, PathType pathType = PathType.Map, float? overrideValue = null)
        {
            var height = overrideValue.HasValue ? overrideValue.Value : GetHeight(position, pathType);

            if (_map.Value.IsXY)
                transform.position = new Vector3(transform.position.x, transform.position.y, height);
            else
                transform.position = new Vector3(transform.position.x, height, transform.position.z);
        }

        public Vector3 ApplyHeight(Vector3 position, PathType pathType = PathType.Map, float? overrideValue = null)
        {
            var height = overrideValue.HasValue ? overrideValue.Value : GetHeight(position, pathType);

            if (_map.Value.IsXY)
                return new Vector3(position.x, position.y, height);
            else
                return new Vector3(position.x, height, position.z);
        }
    }
}
