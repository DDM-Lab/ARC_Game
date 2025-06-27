using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for maps that provides overridable default implementations for all the grid handling<br/>
    /// which points on the map can be walked or built upon is up to the concrete map implementation that inherits from this base
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_map_base.html")]
    [RequireComponent(typeof(Grid))]
    public abstract class MapBase : MonoBehaviour, IMap, IGridOverlay, IGridPositions, IGridRotations
    {
        public enum RotationMode
        {
            Rotate,
            Mirror,
            MirrorAndFlip,
            Disabled
        }

        [Tooltip("size of the map in points, the maps world size is this multiplied with the grids cellsize")]
        public Vector2Int Size;
        [Tooltip("visual shown as an overlay when building")]
        public Renderer GridVisual;
        [Tooltip("walkers will be randomly offset within variance so they dont overlap")]
        public float Variance;
        [Tooltip("how direction is visualized (for example in walker movement)")]
        public RotationMode Rotation;

        public virtual bool IsHex => Grid.cellLayout == GridLayout.CellLayout.Hexagon;
        public virtual bool IsXY => Grid.cellSwizzle == GridLayout.CellSwizzle.XYZ || Grid.cellSwizzle == GridLayout.CellSwizzle.YXZ;
        public virtual Vector3 CellOffset => Grid.cellSize + Grid.cellGap;
        public virtual Vector3 WorldCenter => IsXY ? new Vector3(Size.x / 2f * CellOffset.x, Size.y / 2f * CellOffset.y, 0) : new Vector3(Size.x / 2f * CellOffset.x, 0, Size.y / 2f * CellOffset.y);
        public virtual Vector3 WorldSize => IsXY ? new Vector3(Size.x * CellOffset.x, Size.y * CellOffset.y, 1) : new Vector3(Size.x * CellOffset.x, 1, Size.y * CellOffset.y);

        Vector2Int IMap.Size => Size;

        private Grid _grid;
        public Grid Grid => _grid ? _grid : GetComponent<Grid>();

        protected virtual void Awake()
        {
            _grid = Grid;

            Dependencies.Register<IMap>(this);
            Dependencies.Register<IGridOverlay>(this);
            Dependencies.Register<IGridPositions>(this);
            Dependencies.Register<IGridRotations>(this);
        }

        protected virtual void Start()
        {
            if (GridVisual)
            {
                GridVisual.transform.position = WorldCenter;
                GridVisual.transform.localScale = WorldSize / 10f;
                GridVisual.material.mainTextureScale = new Vector2(Size.x, Size.y);
                GridVisual.gameObject.SetActive(false);
            }
        }

        protected virtual void OnDrawGizmos()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(WorldCenter, WorldSize);
        }

        public bool IsInside(Vector2Int point)
        {
            if (point.x < 0 || point.y < 0)
                return false;
            if (point.x >= Size.x || point.y >= Size.y)
                return false;
            return true;
        }

        public abstract bool IsBuildable(Vector2Int point, int mask, object tag = null);
        public abstract bool IsWalkable(Vector2Int point);

        public abstract bool CheckGround(Vector2Int point, Object[] options);

        public void Show() => setGridVisibility(true);
        public void Hide() => setGridVisibility(false);
        private void setGridVisibility(bool visible)
        {
            if (GridVisual)
                GridVisual.gameObject.SetActive(visible);
        }

        public virtual Vector3 ClampPosition(Vector3 position)
        {
            if (IsXY)
                return new Vector3(Mathf.Clamp(position.x, 0, Size.x * CellOffset.x), Mathf.Clamp(position.y, 0, Size.y * CellOffset.y), position.z);
            else
                return new Vector3(Mathf.Clamp(position.x, 0, Size.x * CellOffset.x), position.y, Mathf.Clamp(position.z, 0, Size.y * CellOffset.y));
        }

        public virtual Vector2Int ClampPoint(Vector2Int point)
        {
            return new Vector2Int(Mathf.Clamp(point.x, 0, Size.x - 1), Mathf.Clamp(point.y, 0, Size.y - 1));
        }

        public virtual Vector2Int GetGridPoint(Vector3 position)
        {
            return (Vector2Int)Grid.WorldToCell(position);
        }

        public virtual Vector3 GetWorldPosition(Vector2Int point)
        {
            return Grid.CellToWorld((Vector3Int)point);
        }

        public virtual Vector3 GetCenterFromPosition(Vector3 position)
        {
            if (IsXY)
                return position + new Vector3(Grid.cellSize.x / 2f, Grid.cellSize.y / 2f, 0f);
            else
                return position + new Vector3(Grid.cellSize.x / 2f, 0f, Grid.cellSize.y / 2f);
        }

        public virtual Vector3 GetPositionFromCenter(Vector3 center)
        {
            if (IsXY)
                return center - new Vector3(Grid.cellSize.x / 2f, Grid.cellSize.y / 2f, 0f);
            else
                return center - new Vector3(Grid.cellSize.x / 2f, 0f, Grid.cellSize.y / 2f);
        }

        public virtual Vector3 GetVariance()
        {
            if (Variance == 0f)
            {
                return Vector3.zero;
            }
            else
            {
                if (IsXY)
                    return new Vector3(Random.Range(-Variance, Variance), Random.Range(-Variance, Variance), 0f);
                else
                    return new Vector3(Random.Range(-Variance, Variance), 0f, Random.Range(-Variance, Variance));
            }
        }

        public virtual void SetRotation(Transform transform, Vector3 direction)
        {
            if (!transform || direction == Vector3.zero)
                return;

            if (Rotation == RotationMode.Rotate)
            {
                if (IsXY)
                {
                    float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                }
                else
                {
                    transform.rotation = Quaternion.LookRotation(direction);
                }
            }
            else if (Rotation == RotationMode.Mirror || Rotation == RotationMode.MirrorAndFlip)
            {
                transform.localScale = new Vector3(direction.x > 0 ? 1 : -1, transform.localScale.y, transform.localScale.z);

                if (Rotation == RotationMode.MirrorAndFlip)
                {
                    transform.localRotation = Quaternion.Euler(direction.y > 0 ? 90f : 0f, 0, 0);
                }
            }
        }
        public virtual void SetRotation(Transform transform, float rotation)
        {
            if (!transform)
                return;

            if (IsXY)
                transform.rotation = Quaternion.Euler(0, 0, rotation);
            else
                transform.rotation = Quaternion.Euler(0, rotation, 0);
        }
        public virtual float GetRotation(Vector3 direction)
        {
            if (IsXY)
                return Vector3.SignedAngle(direction, Vector3.right, Vector3.forward);
            else
                return Vector3.SignedAngle(direction, Vector3.right, Vector3.down);
        }
        public virtual Vector3 GetDirection(float angle)
        {
            if (IsXY)
                return Quaternion.AngleAxis(-angle, Vector3.forward) * Vector3.right;
            else
                return Quaternion.AngleAxis(-angle, Vector3.down) * Vector3.right;
        }
    }
}
