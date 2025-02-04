using NUnit.Framework;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore.Tests
{
    public class PositionTesting
    {
        [Test]
        public virtual void CheckSmallRect()
        {
            var points = PositionHelper.GetAdjacentRect(Vector2Int.zero, Vector2Int.one, 1).ToList();

            Debug.Assert(points.Count == 8);

            Debug.Assert(points.Contains(new Vector2Int(-1, -1)));
            Debug.Assert(points.Contains(new Vector2Int(0, -1)));
            Debug.Assert(points.Contains(new Vector2Int(1, -1)));

            Debug.Assert(points.Contains(new Vector2Int(-1, 1)));
            Debug.Assert(points.Contains(new Vector2Int(0, 1)));
            Debug.Assert(points.Contains(new Vector2Int(1, 1)));

            Debug.Assert(points.Contains(new Vector2Int(-1, 0)));
            Debug.Assert(points.Contains(new Vector2Int(1, 0)));
        }
        [Test]
        public virtual void CheckBigRect()
        {
            var points = PositionHelper.GetAdjacentRect(new Vector2Int(10, 10), Vector2Int.one * 2, 2).ToList();

            Debug.Assert(points.Count == 20);

            Debug.Assert(points.Contains(new Vector2Int(8, 8)));
            Debug.Assert(points.Contains(new Vector2Int(9, 8)));
            Debug.Assert(points.Contains(new Vector2Int(10, 8)));
            Debug.Assert(points.Contains(new Vector2Int(11, 8)));
            Debug.Assert(points.Contains(new Vector2Int(12, 8)));
            Debug.Assert(points.Contains(new Vector2Int(13, 8)));

            Debug.Assert(points.Contains(new Vector2Int(8, 13)));
            Debug.Assert(points.Contains(new Vector2Int(9, 13)));
            Debug.Assert(points.Contains(new Vector2Int(10, 13)));
            Debug.Assert(points.Contains(new Vector2Int(11, 13)));
            Debug.Assert(points.Contains(new Vector2Int(12, 13)));
            Debug.Assert(points.Contains(new Vector2Int(13, 13)));

            Debug.Assert(points.Contains(new Vector2Int(8, 12)));
            Debug.Assert(points.Contains(new Vector2Int(8, 11)));
            Debug.Assert(points.Contains(new Vector2Int(8, 10)));
            Debug.Assert(points.Contains(new Vector2Int(8, 9)));

            Debug.Assert(points.Contains(new Vector2Int(13, 12)));
            Debug.Assert(points.Contains(new Vector2Int(13, 11)));
            Debug.Assert(points.Contains(new Vector2Int(13, 10)));
            Debug.Assert(points.Contains(new Vector2Int(13, 9)));
        }

        [Test]
        public virtual void CheckSmallCross()
        {
            var points = PositionHelper.GetAdjacentCross(Vector2Int.zero, Vector2Int.one, 1).ToList();

            Debug.Assert(points.Count == 4);

            Debug.Assert(points.Contains(new Vector2Int(0, -1)));
            Debug.Assert(points.Contains(new Vector2Int(0, 1)));
            Debug.Assert(points.Contains(new Vector2Int(-1, 0)));
            Debug.Assert(points.Contains(new Vector2Int(1, 0)));
        }
        [Test]
        public virtual void CheckBigCross()
        {
            var points = PositionHelper.GetAdjacentCross(new Vector2Int(10, 10), Vector2Int.one * 2, 2).ToList();

            Debug.Assert(points.Count == 8);

            Debug.Assert(points.Contains(new Vector2Int(10, 8)));
            Debug.Assert(points.Contains(new Vector2Int(11, 8)));

            Debug.Assert(points.Contains(new Vector2Int(10, 13)));
            Debug.Assert(points.Contains(new Vector2Int(11, 13)));

            Debug.Assert(points.Contains(new Vector2Int(8, 11)));
            Debug.Assert(points.Contains(new Vector2Int(8, 10)));

            Debug.Assert(points.Contains(new Vector2Int(13, 11)));
            Debug.Assert(points.Contains(new Vector2Int(13, 10)));
        }

        [Test]
        public virtual void CheckSmallHex()
        {
            var points = PositionHelper.GetAdjacentHex(Vector2Int.zero, Vector2Int.one, 1).ToList();

            Debug.Assert(points.Count == 6);

            Debug.Assert(points.Contains(new Vector2Int(1, 0)));
            Debug.Assert(points.Contains(new Vector2Int(0, -1)));
            Debug.Assert(points.Contains(new Vector2Int(-1, -1)));
            Debug.Assert(points.Contains(new Vector2Int(-1, 0)));
            Debug.Assert(points.Contains(new Vector2Int(-1, 1)));
            Debug.Assert(points.Contains(new Vector2Int(0, 1)));
        }
        [Test]
        public virtual void CheckSmallHexOdd()
        {
            var points = PositionHelper.GetAdjacentHex(new Vector2Int(0, 1), Vector2Int.one, 1).ToList();

            Debug.Assert(points.Count == 6);

            Debug.Assert(points.Contains(new Vector2Int(1, 1)));
            Debug.Assert(points.Contains(new Vector2Int(1, 0)));
            Debug.Assert(points.Contains(new Vector2Int(0, 0)));
            Debug.Assert(points.Contains(new Vector2Int(-1, 1)));
            Debug.Assert(points.Contains(new Vector2Int(0, 2)));
            Debug.Assert(points.Contains(new Vector2Int(1, 2)));
        }
        [Test]
        public virtual void CheckBigHex()
        {
            var points = PositionHelper.GetAdjacentHex(new Vector2Int(16,20), Vector2Int.one * 2, 2).ToList();

            Debug.Assert(points.Count == 18);

            Debug.Assert(points.Contains(new Vector2Int(17, 23)));
            Debug.Assert(points.Contains(new Vector2Int(18, 22)));
            Debug.Assert(points.Contains(new Vector2Int(18, 21)));
            Debug.Assert(points.Contains(new Vector2Int(19, 20)));
            Debug.Assert(points.Contains(new Vector2Int(18, 19)));
            Debug.Assert(points.Contains(new Vector2Int(18, 18)));
            Debug.Assert(points.Contains(new Vector2Int(17, 17)));
            Debug.Assert(points.Contains(new Vector2Int(16, 17)));
            Debug.Assert(points.Contains(new Vector2Int(15, 17)));
            Debug.Assert(points.Contains(new Vector2Int(14, 17)));
            Debug.Assert(points.Contains(new Vector2Int(14, 18)));
            Debug.Assert(points.Contains(new Vector2Int(13, 19)));
            Debug.Assert(points.Contains(new Vector2Int(13, 20)));
            Debug.Assert(points.Contains(new Vector2Int(13, 21)));
            Debug.Assert(points.Contains(new Vector2Int(14, 23)));
            Debug.Assert(points.Contains(new Vector2Int(14, 22)));
            Debug.Assert(points.Contains(new Vector2Int(15, 23)));
            Debug.Assert(points.Contains(new Vector2Int(16, 23)));
        }

        [Test]
        public virtual void CheckRectRoadStraight()
        {
            var points = PositionHelper.GetRoadPositionsRect(new Vector2Int(0, 0), new Vector2Int(4, 0)).ToList();

            Debug.Assert(points.Count == 5);

            Debug.Assert(points.Contains(new Vector2Int(0, 0)));
            Debug.Assert(points.Contains(new Vector2Int(1, 0)));
            Debug.Assert(points.Contains(new Vector2Int(2, 0)));
            Debug.Assert(points.Contains(new Vector2Int(3, 0)));
            Debug.Assert(points.Contains(new Vector2Int(4, 0)));
        }
        [Test]
        public virtual void CheckRectRoadL()
        {
            var points = PositionHelper.GetRoadPositionsRect(new Vector2Int(20, 30), new Vector2Int(24, 35)).ToList();

            Debug.Assert(points.Count == 10);

            Debug.Assert(points.Contains(new Vector2Int(20, 30)));
            Debug.Assert(points.Contains(new Vector2Int(20, 31)));
            Debug.Assert(points.Contains(new Vector2Int(20, 32)));
            Debug.Assert(points.Contains(new Vector2Int(20, 33)));
            Debug.Assert(points.Contains(new Vector2Int(20, 34)));
            Debug.Assert(points.Contains(new Vector2Int(20, 35)));
            Debug.Assert(points.Contains(new Vector2Int(21, 35)));
            Debug.Assert(points.Contains(new Vector2Int(22, 35)));
            Debug.Assert(points.Contains(new Vector2Int(23, 35)));
            Debug.Assert(points.Contains(new Vector2Int(24, 35)));
        }

        [Test]
        public virtual void CheckHexRoadStraight()
        {
            var points = PositionHelper.GetRoadPositionsHex(new Vector2Int(0, 0), new Vector2Int(4, 0)).ToList();

            Debug.Assert(points.Count == 5);

            Debug.Assert(points.Contains(new Vector2Int(0, 0)));
            Debug.Assert(points.Contains(new Vector2Int(1, 0)));
            Debug.Assert(points.Contains(new Vector2Int(2, 0)));
            Debug.Assert(points.Contains(new Vector2Int(3, 0)));
            Debug.Assert(points.Contains(new Vector2Int(4, 0)));
        }
        [Test]
        public virtual void CheckHexRoadDiagonal()
        {
            var points = PositionHelper.GetRoadPositionsHex(new Vector2Int(2, 4), new Vector2Int(4, 0)).ToList();

            Debug.Assert(points.Count == 5);

            Debug.Assert(points.Contains(new Vector2Int(2, 4)));
            Debug.Assert(points.Contains(new Vector2Int(2, 3)));
            Debug.Assert(points.Contains(new Vector2Int(3, 2)));
            Debug.Assert(points.Contains(new Vector2Int(3, 1)));
            Debug.Assert(points.Contains(new Vector2Int(4, 0)));
        }
        [Test]
        public virtual void CheckHexRoadL()
        {
            var points = PositionHelper.GetRoadPositionsHex(new Vector2Int(2, 4), new Vector2Int(6, 2)).ToList();

            Debug.Assert(points.Count == 6);

            Debug.Assert(points.Contains(new Vector2Int(2, 4)));
            Debug.Assert(points.Contains(new Vector2Int(3, 4)));
            Debug.Assert(points.Contains(new Vector2Int(4, 4)));
            Debug.Assert(points.Contains(new Vector2Int(5, 4)));
            Debug.Assert(points.Contains(new Vector2Int(5, 3)));
            Debug.Assert(points.Contains(new Vector2Int(6, 2)));
        }
    }
}
