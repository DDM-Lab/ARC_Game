using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// blocker that simply blocks its own position in the <see cref="IRoadManager"/>
    /// blocking prevents a <see cref="Walker"/> with <see cref="PathType.RoadBlocked"/> from using a point
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_transform_road_blocker.html")]
    public class TransformRoadBlocker : MonoBehaviour
    {
        private Vector2Int _position;

        private void Start()
        {
            _position = Dependencies.Get<IGridPositions>().GetGridPoint(transform.position);

            Dependencies.Get<IRoadManager>().Block(new Vector2Int[] { _position });
        }

        private void OnDestroy()
        {
            if (gameObject.scene.isLoaded)
                Dependencies.Get<IRoadManager>().Unblock(new Vector2Int[] { _position });
        }
    }
}