using NUnit.Framework;
using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class RotationTesting
    {
        [Test]
        public void RotateBuildingPoint()
        {
            var rot = new BuildingRotationRectangle();

            Assert.AreEqual(new Vector2Int(5, 5), rot.RotateBuildingPoint(new Vector2Int(2, 2), new Vector2Int(3, 3), new Vector2Int(4, 4)));
            rot.TurnClockwise();
            Assert.AreEqual(new Vector2Int(5, 2), rot.RotateBuildingPoint(new Vector2Int(2, 2), new Vector2Int(3, 3), new Vector2Int(4, 4)));
            rot.TurnClockwise();
            Assert.AreEqual(new Vector2Int(2, 2), rot.RotateBuildingPoint(new Vector2Int(2, 2), new Vector2Int(3, 3), new Vector2Int(4, 4)));
            rot.TurnClockwise();
            Assert.AreEqual(new Vector2Int(2, 5), rot.RotateBuildingPoint(new Vector2Int(2, 2), new Vector2Int(3, 3), new Vector2Int(4, 4)));
            rot.TurnClockwise();
            Assert.AreEqual(new Vector2Int(5, 5), rot.RotateBuildingPoint(new Vector2Int(2, 2), new Vector2Int(3, 3), new Vector2Int(4, 4)));
        }
    }
}
