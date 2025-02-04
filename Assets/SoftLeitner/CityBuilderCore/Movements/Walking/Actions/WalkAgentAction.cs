using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// walks a walker to a destination using its navmesh agent
    /// </summary>
    [Serializable]
    public class WalkAgentAction : WalkerAction
    {
        [SerializeField]
        public Vector3 _position;
        [SerializeField]
        public float? _distance;

        public WalkAgentAction()
        {

        }
        public WalkAgentAction(Vector3 position, float? distance = null)
        {
            _position = position;
            _distance = distance;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            walker.WalkAgent(_position, walker.AdvanceProcess, _distance);
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
