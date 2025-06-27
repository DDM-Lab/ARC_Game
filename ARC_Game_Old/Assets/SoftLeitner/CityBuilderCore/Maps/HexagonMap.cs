using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// adjusts the <see cref="DefaultMap"/> to work with a hexagon grid
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_hexagon_map.html")]
    [RequireComponent(typeof(Grid))]
    public class HexagonMap : DefaultMap
    {
        public const float HEX_RATIO_HEIGHT = 0.8660254f;
        public const float HEX_RATIO_WIDTH = 1.1547005f;

        public override Vector3 WorldCenter => GetWorldPosition(Size / 2);

        protected override void OnDrawGizmos()
        {
            Gizmos.color = Color.white;

            var origin = GetWorldPosition(Vector2Int.zero);
            var opposite = GetWorldPosition(new Vector2Int(Size.x - 1, Size.y - 1));
            Vector3 c1, c2;

            if (IsXY)
            {
                c1 = new Vector3(origin.x, opposite.y, 0);
                c2 = new Vector3(opposite.x, origin.y, 0);
            }
            else
            {
                c1 = new Vector3(origin.x, 0, opposite.z);
                c2 = new Vector3(opposite.x, 0, origin.z);
            }

            Gizmos.DrawLine(origin, c1);
            Gizmos.DrawLine(c1, opposite);
            Gizmos.DrawLine(opposite, c2);
            Gizmos.DrawLine(c2, origin);
        }

        public override Vector3 ClampPosition(Vector3 position)
        {
            if (IsXY)
                return new Vector3(Mathf.Clamp(position.x, 0, Size.x * CellOffset.x), Mathf.Clamp(position.y, 0, Size.y * CellOffset.y * HEX_RATIO_HEIGHT), position.z);
            else
                return new Vector3(Mathf.Clamp(position.x, 0, Size.x * CellOffset.x), position.y, Mathf.Clamp(position.z, 0, Size.y * CellOffset.y * HEX_RATIO_HEIGHT));
        }
    }
}