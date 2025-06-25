namespace CityBuilderCore.Tests
{
    public class DebugRoamer : RoamingWalker
    {
        protected override void Start()
        {
            base.Start();

            Delay(() => Initialize(null, Dependencies.Get<IGridPositions>().GetGridPoint(transform.position)));
        }
    }
}