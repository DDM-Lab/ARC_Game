namespace CityBuilderCore.Tests
{
    public class DebugAttacker : AttackWalker
    {
        protected override void Start()
        {
            base.Start();

            this.Delay(1, () => Initialize(null, Dependencies.Get<IGridPositions>().GetGridPoint(transform.position)));
        }
    }
}