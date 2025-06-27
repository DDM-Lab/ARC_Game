using NUnit.Framework;
using System.Linq;

namespace CityBuilderCore.Tests
{
    public class StructurePointChecker : CheckerBase
    {
        public string ExpectedStructureKey;

        public override void Check()
        {
            Assert.AreEqual(ExpectedStructureKey, Dependencies.Get<IStructureManager>().GetStructures(Dependencies.Get<IGridPositions>().GetGridPoint(transform.position)).FirstOrDefault()?.Key, name);
        }
    }
}
