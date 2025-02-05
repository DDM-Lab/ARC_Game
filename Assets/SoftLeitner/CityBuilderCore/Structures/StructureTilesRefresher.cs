using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// behaviour that will refresh tiles under and around an attached building<br/>
    /// if no building is found the point under the transform and the ones around it are refreshed<br/>
    /// used in the urban demo so that tiles attach to buildings, for example power lines to houses
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/structures">https://citybuilder.softleitner.com/manual/structures</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_structure_tiles_refresher.html")]
    public class StructureTilesRefresher : MonoBehaviour
    {
        [Tooltip("the key of the structure tiles")]
        public string Key;

        private StructureTiles _tiles;
        private IBuilding _building;
        private Vector2Int _p1, _p2;
        private Vector2Int _size;

        private void Start()
        {
            _tiles = Dependencies.Get<IStructureManager>().GetStructure(Key) as StructureTiles;
            if (_tiles == null)
                throw new System.Exception("Missing StructureTiles with Key:" + Key);

            _building = GetComponent<Building>();
            SetPoints();
            Refresh();
        }

        private void _buildingPointsChanged(PointsChanged<IStructure> obj)
        {
            Refresh();
            SetPoints();
            Refresh();
        }

        private void OnDestroy()
        {
            if (gameObject.scene.isLoaded)
                Refresh();
        }

        public void SetPoints()
        {
            if (_building == null)
            {
                _p1 = Dependencies.Get<IGridPositions>().GetGridPoint(transform.position);
                _p2 = Vector2Int.zero;
            }
            else
            {
                _p1 = _building.Point;
                _p2 = _building.Size;

                _building.PointsChanged += _buildingPointsChanged;
            }
        }

        public void Refresh()
        {
            foreach (var point in PositionHelper.GetBoxPositions(_p1 - Vector2Int.one, _p1 + _p2 + Vector2Int.one))
            {
                _tiles.RefreshTile(point);
            }
        }
    }
}
