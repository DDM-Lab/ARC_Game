using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore.Tests
{
    [RequireComponent(typeof(Grid))]
    public class DebugMap : MonoBehaviour, IMap, IGridOverlay, IGridPositions, IGridRotations
    {
        public Vector2Int Size;
        public Tilemap Ground;
        public TileBase[] WalkingBlockingTiles;
        public TileBase[] BuildingBlockingTiles;
        public MeshRenderer GridVisual;

        public bool IsHex => Grid.cellLayout == GridLayout.CellLayout.Hexagon;
        public bool IsXY => Grid.cellSwizzle == GridLayout.CellSwizzle.XYZ;
        public Vector3 CellOffset => Vector3.one;
        public Vector3 WorldCenter => new Vector3(Size.x / 2f, 0, Size.y / 2f);
        public Vector3 WorldSize => new Vector3(Size.x, 1, Size.y);

        Vector2Int IMap.Size => Size;

        public Grid Grid => _grid ? _grid : GetComponent<Grid>();

        private Grid _grid;

        protected virtual void Awake()
        {
            _grid = Grid;

            Dependencies.Register<IMap>(this);
            Dependencies.Register<IGridOverlay>(this);
            Dependencies.Register<IGridPositions>(this);
            Dependencies.Register<IGridRotations>(this);
        }

        private void Start()
        {
            if (GridVisual)
            {
                GridVisual.transform.position = WorldCenter;
                GridVisual.transform.localScale = new Vector3(Size.x / 10f, 0f, Size.y / 10f);
                GridVisual.material.mainTextureScale = Size;
                GridVisual.gameObject.SetActive(false);
            }
        }

        private void OnDrawGizmos()
        {
            if (Grid)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireCube(WorldCenter, WorldSize);
            }
        }

        public bool IsInside(Vector2Int point)
        {
            if (point.x < 0 || point.y < 0)
                return false;
            if (point.x >= Size.x || point.y >= Size.y)
                return false;
            return true;
        }

        public bool IsBuildable(Vector2Int point, int mask, object tag = null)
        {
            return !BuildingBlockingTiles.Contains(Ground.GetTile((Vector3Int)point));
        }

        public bool IsWalkable(Vector2Int point)
        {
            return !WalkingBlockingTiles.Contains(Ground.GetTile((Vector3Int)point));
        }

        public bool CheckGround(Vector2Int point, Object[] options)
        {
            return options.Contains(Ground.GetTile((Vector3Int)point));
        }

        public void Show() => setGridVisibility(true);
        public void Hide() => setGridVisibility(false);
        private void setGridVisibility(bool visible)
        {
            if (GridVisual)
                GridVisual.gameObject.SetActive(visible);
        }

        public Vector3 ClampPosition(Vector3 position)
        {
            return new Vector3(Mathf.Clamp(position.x, 0, Size.x), position.y, Mathf.Clamp(position.z, 0, Size.y));
        }

        public Vector2Int ClampPoint(Vector2Int point)
        {
            return new Vector2Int(Mathf.Clamp(point.x, 0, Size.x - 1), Mathf.Clamp(point.y, 0, Size.y - 1));
        }

        public Vector2Int GetGridPoint(Vector3 position)
        {
            return (Vector2Int)Grid.WorldToCell(position);
        }

        public Vector3 GetWorldPosition(Vector2Int point)
        {
            return Grid.CellToWorld((Vector3Int)point);
        }

        public Vector3 GetCenterFromPosition(Vector3 position)
        {
            return position + new Vector3(Grid.cellSize.x / 2f, 0f, Grid.cellSize.y / 2f);
        }

        public Vector3 GetPositionFromCenter(Vector3 center)
        {
            return center - new Vector3(Grid.cellSize.x / 2f, 0f, Grid.cellSize.y / 2f);
        }

        public Vector3 GetVariance()
        {
            return Vector3.zero;
        }

        public void SetRotation(Transform transform, Vector3 direction)
        {
            if (transform)
                transform.localRotation = Quaternion.LookRotation(direction);
        }
        public void SetRotation(Transform transform, float rotation) { }
        public float GetRotation(Vector3 _) => 0;
        public Vector3 GetDirection(float _) => Vector3.zero;
    }
}