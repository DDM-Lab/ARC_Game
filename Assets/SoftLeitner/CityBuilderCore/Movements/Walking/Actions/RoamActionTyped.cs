using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// makes the walker roam on a specified path type
    /// </summary>
    [Serializable]
    public class RoamActionTyped : WalkerAction
    {
        [SerializeField]
        private int _memoryLength;
        [SerializeField]
        private int _range;
        [SerializeField]
        private PathType _pathType;

        public RoamActionTyped()
        {

        }
        public RoamActionTyped(int memoryLength, int range, PathType pathType)
        {
            _memoryLength = memoryLength;
            _range = range;
            _pathType = pathType;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            walker.Roam(_memoryLength, _range, _pathType, null, walker.AdvanceProcess);
        }
        public override void Continue(Walker walker)
        {
            base.Continue(walker);

            walker.ContinueRoam(_memoryLength, _range, _pathType, null, walker.AdvanceProcess);
        }
        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            walker.CancelRoam();
        }
    }
}
