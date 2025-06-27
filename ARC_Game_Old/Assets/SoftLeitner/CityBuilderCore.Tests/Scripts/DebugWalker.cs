using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class DebugWalker : Walker
    {
        public Transform Target;
        public bool ShouldArrive;

        public bool HasFinished { get; private set; }
        public bool HasArrived => Dependencies.Get<IGridPositions>().GetGridPoint(transform.position) == Dependencies.Get<IGridPositions>().GetGridPoint(Target.position);

        protected override void Start()
        {
            base.Start();

            Initialize(null, Dependencies.Get<IGridPositions>().GetGridPoint(transform.position));
        }

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            if (Target)
                this.Delay(1, () => tryWalk(Dependencies.Get<IGridPositions>().GetGridPoint(Target.position), finished: () => HasFinished = true));
        }

        protected override void onProcessFinished(ProcessState process)
        {

        }
    }
}