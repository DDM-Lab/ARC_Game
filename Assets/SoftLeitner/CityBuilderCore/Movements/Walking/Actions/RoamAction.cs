using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// makes the walker roam
    /// </summary>
    [Serializable]
    public class RoamAction : WalkerAction
    {
        [SerializeField]
        private int _memoryLength;
        [SerializeField]
        private int _range;

        public RoamAction()
        {

        }
        public RoamAction(int memoryLength, int range)
        {
            _memoryLength = memoryLength;
            _range = range;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            walker.Roam(_memoryLength, _range, walker.AdvanceProcess);
        }
        public override void Continue(Walker walker)
        {
            base.Continue(walker);

            walker.ContinueRoam(_memoryLength, _range, walker.AdvanceProcess);
        }
        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            walker.CancelRoam();
        }
    }
}
