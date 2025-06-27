using NUnit.Framework;
using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class StructureChecker : CheckerBase
    {
        public StructureCollection StructureCollection;
        public Transform Position;
        public bool ExpectedExistence;
        public bool ActualExistence => StructureCollection.HasPoint(Dependencies.Get<IGridPositions>().GetGridPoint(Position.position));

        public override void Check()
        {
            Assert.AreEqual(ExpectedExistence, ActualExistence, name);
        }
    }
}