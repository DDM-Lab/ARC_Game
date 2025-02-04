using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for walkers that roam and perform actions when passing buildings
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_walker.html")]
    public abstract class BuildingWalker : RoamingWalker
    {
        [Tooltip("how many points away from the walker buildings can be to be effected by the walker<br/>when 0 the walker has to stand on a point inside the building")]
        public int Area = 1;
        [Tooltip("when true only points up/down left/right count as walker area, otherwise the diagonals also count")]
        public bool IsCross;

        private readonly List<BuildingReference> _buildings = new List<BuildingReference>();
        private LazyDependency<IBuildingManager> _buildingManager = new LazyDependency<IBuildingManager>();

        protected virtual void Update()
        {
            foreach (var building in _buildings)
            {
                if (building.HasInstance)
                    onRemaining(building.Instance);
            }
        }

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            _buildings.Clear();
        }

        public override void Spawned()
        {
            base.Spawned();

            checkBuildings();
        }

        protected override void onMoved(Vector2Int point)
        {
            base.onMoved(point);

            checkBuildings();
        }

        /// <summary>
        /// called when the building first enters the walkers area
        /// </summary>
        /// <param name="building">the building inside the walkers area</param>
        protected virtual void onEntered(IBuilding building)
        {

        }
        /// <summary>
        /// called on every frame the walkers area contains the building
        /// </summary>
        /// <param name="building">the building inside the walkers area</param>
        protected virtual void onRemaining(IBuilding building)
        {

        }

        private void checkBuildings()
        {
            var exited = _buildings.ToList();

            foreach (var point in PositionHelper.GetAdjacent(CurrentPoint, Vector2Int.one, !IsCross, range: Area))
            {
                foreach (var building in _buildingManager.Value.GetBuilding(point))
                {
                    if (_buildings.Contains(building.BuildingReference))
                    {
                        exited.Remove(building.BuildingReference);
                    }
                    else
                    {
                        _buildings.Add(building.BuildingReference);
                        onEntered(building);
                    }
                }
            }

            foreach (var structure in exited)
            {
                _buildings.Remove(structure);
            }
        }
    }
}