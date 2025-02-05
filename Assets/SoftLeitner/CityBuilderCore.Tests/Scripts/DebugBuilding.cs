using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class DebugBuilding : Building
    {
        public bool Walkable = true;

        public override Vector2Int Size => Info == null ? Vector2Int.one : base.Size;
        public override Vector2Int Point => Info == null ? Dependencies.Get<IGridPositions>().GetGridPoint(transform.position) : base.Point;

        public override bool IsWalkable => Walkable;

        protected override void Awake()
        {
            Components.ForEach(c => c.Building = this);
        }

        protected override void Start()
        {
            if (StructureReference == null)
                Initialize();
        }

        public override void Initialize()
        {
            StructureReference = new StructureReference(this);
            BuildingReference = new BuildingReference(this);

            Dependencies.GetOptional<IStructureManager>()?.RegisterStructure(this);
            Dependencies.GetOptional<IBuildingManager>()?.RegisterBuilding(this);

            var height = Dependencies.GetOptional<IGridHeights>();
            if (height != null && Pivot)
                height.ApplyHeight(Pivot);

            Components.ForEach(c => c.InitializeComponent());
        }
        public override void Setup()
        {
            Components.ForEach(c => c.SetupComponent());
        }

        public override void Terminate()
        {
            Components.ForEach(c => c.TerminateComponent());

            Dependencies.Get<IStructureManager>().DeregisterStructure(this);
            Dependencies.Get<IBuildingManager>().DeregisterBuilding(this);

            Destroy(gameObject);
        }

        public override IEnumerable<Vector2Int> GetAccessPoints(PathType type, object tag = null) => Info == null ? new Vector2Int[] { Point } : base.GetAccessPoints(type, tag);
        public override Vector2Int? GetAccessPoint(PathType type, object tag = null) => Info == null ? Point : base.GetAccessPoint(type, tag);
    }
}