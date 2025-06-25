using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// attempts to walk to a building
    /// </summary>
    [Serializable]
    public class WalkBuildingAction : WalkerAction, ISerializationCallbackReceiver
    {
        private BuildingReference _buildingReference;
        [SerializeField]
        private string _buildingId;

        public WalkBuildingAction()
        {

        }
        public WalkBuildingAction(IBuilding building)
        {
            _buildingReference = building.BuildingReference;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            if (!_buildingReference.HasInstance)
                walker.AdvanceProcess();

            if (!walker.Walk(_buildingReference.Instance, walker.AdvanceProcess))
                walker.AdvanceProcess();
        }
        public override void Continue(Walker walker)
        {
            base.Continue(walker);

            walker.ContinueWalk(walker.AdvanceProcess);
        }
        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            walker.CancelWalk();
        }

        public void OnBeforeSerialize()
        {
            _buildingId = _buildingReference.Id.ToString();
        }
        public void OnAfterDeserialize()
        {
            _buildingReference = Dependencies.Get<IBuildingManager>().GetBuildingReference(new Guid(_buildingId));
        }
    }
}
