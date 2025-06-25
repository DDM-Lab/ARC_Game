using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace CityBuilderCore.Tests
{
    public class MapBaseTesting
    {
        [Test]
        public virtual void CheckRotationXY()
        {
            var map = getMap(cellSwizzle: GridLayout.CellSwizzle.XYZ);

            Assert.AreEqual(0, map.GetRotation(Vector3.right));
            Assert.AreEqual(90, map.GetRotation(Vector3.down));
            Assert.AreEqual(180, map.GetRotation(Vector3.left));
            Assert.AreEqual(-90, map.GetRotation(Vector3.up));
        }

        [Test]
        public virtual void CheckRotationXZ()
        {
            var map = getMap(cellSwizzle: GridLayout.CellSwizzle.XZY);

            Assert.AreEqual(0, map.GetRotation(Vector3.right));
            Assert.AreEqual(90, map.GetRotation(Vector3.back));
            Assert.AreEqual(180, map.GetRotation(Vector3.left));
            Assert.AreEqual(-90, map.GetRotation(Vector3.forward));
        }

        [Test]
        public virtual void CheckDirectionXY()
        {
            var map = getMap(cellSwizzle: GridLayout.CellSwizzle.XYZ);

            var comparer = new Vector3EqualityComparer(10e-6f);

            Assert.That(map.GetDirection(0), Is.EqualTo(Vector3.right).Using(comparer));
            Assert.That(map.GetDirection(90), Is.EqualTo(Vector3.down).Using(comparer));
            Assert.That(map.GetDirection(180), Is.EqualTo(Vector3.left).Using(comparer));
            Assert.That(map.GetDirection(-90), Is.EqualTo(Vector3.up).Using(comparer));
        }

        [Test]
        public virtual void CheckDirectionXZ()
        {
            var map = getMap(cellSwizzle: GridLayout.CellSwizzle.XZY);

            var comparer = new Vector3EqualityComparer(10e-6f);

            Assert.That(map.GetDirection(0), Is.EqualTo(Vector3.right).Using(comparer));
            Assert.That(map.GetDirection(90), Is.EqualTo(Vector3.back).Using(comparer));
            Assert.That(map.GetDirection(180), Is.EqualTo(Vector3.left).Using(comparer));
            Assert.That(map.GetDirection(-90), Is.EqualTo(Vector3.forward).Using(comparer));
        }

        [TearDown]
        public void ClearDependencies() => Dependencies.Clear();

        private MapBase getMap(Vector2Int? size = null, GridLayout.CellSwizzle cellSwizzle = GridLayout.CellSwizzle.XYZ)
        {
            GameObject gameObject = new GameObject();

            var grid = gameObject.AddComponent<Grid>();

            grid.cellSwizzle = cellSwizzle;

            var map = gameObject.AddComponent<DefaultMap>();

            map.Size = size ?? new Vector2Int(100, 100);

            return map;
        }
    }
}
