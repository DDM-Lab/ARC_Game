using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// implementation for various map and grid functions on an isometric grid with 2d sprites
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_isometric_map.html")]
    [RequireComponent(typeof(Grid))]
    public class IsometricMap : DefaultMap
    {
        public override Vector3 WorldCenter => GetWorldPosition(Size / 2);

        protected override void OnDrawGizmos()
        {
            Gizmos.color = Color.white;

            Gizmos.DrawLine(GetWorldPosition(new Vector2Int(0, 0)), GetWorldPosition(new Vector2Int(Size.x, 0)));
            Gizmos.DrawLine(GetWorldPosition(new Vector2Int(Size.x, 0)), GetWorldPosition(new Vector2Int(Size.x, Size.y)));
            Gizmos.DrawLine(GetWorldPosition(new Vector2Int(Size.x, Size.y)), GetWorldPosition(new Vector2Int(0, Size.y)));
            Gizmos.DrawLine(GetWorldPosition(new Vector2Int(0, Size.y)), GetWorldPosition(new Vector2Int(0, 0)));
        }

        public override Vector3 ClampPosition(Vector3 position)
        {
            if (IsXY)
                return new Vector3(Mathf.Clamp(position.x, -Size.x * CellOffset.x / 2f, Size.x * CellOffset.x / 2f), Mathf.Clamp(position.y, 0, Size.y * CellOffset.y), position.z);
            else
                return new Vector3(Mathf.Clamp(position.x, -Size.x * CellOffset.x / 2f, Size.x * CellOffset.x / 2f), position.y, Mathf.Clamp(position.z, 0, Size.y * CellOffset.y));
        }

        public override Vector3 GetCenterFromPosition(Vector3 position)
        {
            if (IsXY)
                return position + new Vector3(0, Grid.cellSize.y / 2f, 0);
            else
                return position + new Vector3(0, 0, Grid.cellSize.y / 2f);
        }

        public override Vector3 GetPositionFromCenter(Vector3 center)
        {
            if (IsXY)
                return center - new Vector3(0, Grid.cellSize.y / 2f, 0);
            else
                return center - new Vector3(0, 0, Grid.cellSize.y / 2f);
        }

        public override Vector3 GetVariance()
        {
            if (Variance == 0f)
            {
                return Vector3.zero;
            }
            else
            {
                if (IsXY)
                    return new Vector3(Random.Range(-Variance, Variance), Random.Range(-Variance / 2f, Variance / 2f), 0f);
                else
                    return new Vector3(Random.Range(-Variance, Variance), 0f, Random.Range(-Variance / 2f, Variance / 2f));
            }
        }
    }
}