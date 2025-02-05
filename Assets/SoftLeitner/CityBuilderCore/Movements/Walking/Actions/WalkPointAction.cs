using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// attempts to walk to a point
    /// </summary>
    [Serializable]
    public class WalkPointAction : WalkerAction
    {
        [SerializeField]
        public Vector2Int _point;

        public WalkPointAction()
        {

        }
        public WalkPointAction(Vector2Int point)
        {
            _point = point;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            if (!walker.Walk(_point, walker.AdvanceProcess))
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
    }
}
