using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// makes the walker walk a set path
    /// </summary>
    [Serializable]
    public class WalkPathAction : WalkerAction, ISerializationCallbackReceiver
    {
        private WalkingPath _walkingPath;
        [SerializeField]
        private WalkingPath.WalkingPathData _walkingPathData;

        public WalkPathAction()
        {

        }
        public WalkPathAction(WalkingPath walkingPath)
        {
            _walkingPath = walkingPath;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            walker.Walk(_walkingPath, walker.AdvanceProcess);
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
            _walkingPathData = _walkingPath.GetData();
        }
        public void OnAfterDeserialize()
        {
            _walkingPath = _walkingPathData.GetPath();
        }
    }
}
