using System;

namespace CityBuilderCore
{
    /// <summary>
    /// helper class for <see cref="Walker"/> that hold the current status when the walker is following a <see cref="WalkingPath"/><br/>
    /// </summary>
    public class WalkingState
    {
        /// <summary>
        /// the walking path the walker is currently following
        /// </summary>
        public WalkingPath WalkingPath { get; private set; }
        /// <summary>
        /// how far the walker has moved within the current step, used to calculate the actual position of the walker by interpolating between the last and next point
        /// </summary>
        public float Moved { get; set; }
        /// <summary>
        /// index of the point in the path the walker has last visited
        /// </summary>
        public int Index { get; set; }
        /// <summary>
        /// whether the walking has been canceled
        /// </summary>
        public bool IsCanceled { get; private set; }

        private WalkingState()
        {

        }
        public WalkingState(WalkingPath walkingPath)
        {
            WalkingPath = walkingPath;
        }

        /// <summary>
        /// replaces the current path with another one and resets the index so it is followed from its start<br/>
        /// happens in the defense demo when new obstacles are placed that may interfere with the walker
        /// </summary>
        /// <param name="walkingPath">the new path that will be followed instead of the current one</param>
        public void Recalculated(WalkingPath walkingPath)
        {
            WalkingPath = walkingPath;
            Index = 0;
        }

        /// <summary>
        /// requests for the walking to be canceled, it might not end immediately but at the next possible point
        /// </summary>
        public void Cancel()
        {
            IsCanceled = true;
        }

        #region Saving
        [Serializable]
        public class WalkingData
        {
            public WalkingPath.WalkingPathData WalkingPathData;
            public float Moved;
            public int Index;
        }

        public WalkingData GetData() => new WalkingData()
        {
            WalkingPathData = WalkingPath.GetData(),
            Moved = Moved,
            Index = Index
        };
        public static WalkingState FromData(WalkingData data)
        {
            if (data == null)
                return null;
            return new WalkingState()
            {
                WalkingPath = WalkingPath.FromData(data.WalkingPathData),
                Moved = data.Moved,
                Index = data.Index
            };
        }
        #endregion
    }
}