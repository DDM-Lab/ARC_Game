using NUnit.Framework;

namespace CityBuilderCore.Tests
{
    public class LayerChecker : CheckerBase
    {
        public Layer Layer;
        public int ExpectedValue;
        public int ActualValue => Dependencies.Get<ILayerManager>().GetValue(Dependencies.Get<IGridPositions>().GetGridPoint(transform.position), Layer);

        public override void Check()
        {
            Assert.AreEqual(ExpectedValue, ActualValue, transform.parent.name + " " + transform.name);
        }
    }
}