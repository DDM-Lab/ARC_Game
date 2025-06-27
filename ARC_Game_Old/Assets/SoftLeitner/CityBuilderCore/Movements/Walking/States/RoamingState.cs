using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper class for <see cref="Walker"/> that hold the current status of its roaming<br/>
    /// roaming is a special kind of randomly walking around which memorizes and avoids the points it has already been to
    /// </summary>
    public class RoamingState
    {
        /// <summary>
        /// how many steps the walker has taken while roaming, roaming ends when the steps exceed the range(<see cref="RoamingWalker.Range"/>)
        /// </summary>
        public int Steps { get; set; }
        /// <summary>
        /// how far the walker has moved within the current step, used to calculate the actual position of the walker by interpolating between the last and next point
        /// </summary>
        public float Moved { get; set; }
        /// <summary>
        /// last point the walker has reached
        /// </summary>
        public Vector2Int Current { get; set; }
        /// <summary>
        /// point the walker is currently going to
        /// </summary>
        public Vector2Int Next { get; set; }
        /// <summary>
        /// this is where already visited points are stored so they can be avoided in the future, the walker dictates how long that memory is(for example <see cref="RoamingWalker.Memory"/>)
        /// </summary>
        public List<Vector2Int> Memory { get; set; }
        /// <summary>
        /// whether the roaming has been canceled
        /// </summary>
        public bool IsCanceled { get; private set; }

        public RoamingState()
        {
            Memory = new List<Vector2Int>();
        }

        /// <summary>
        /// requests for the roaming to be canceled, it might not end immediately but at the next possible point
        /// </summary>
        public void Cancel()
        {
            IsCanceled = true;
        }

        #region Saving
        [Serializable]
        public class RoamingData
        {
            public int Steps;
            public float Moved;
            public Vector2Int Current;
            public Vector2Int Next;
            public List<Vector2Int> Memory;
            public bool Canceled;
        }

        public RoamingData GetData() => new RoamingData()
        {
            Steps = Steps,
            Moved = Moved,
            Current = Current,
            Next = Next,
            Memory = Memory,
            Canceled = IsCanceled,
        };
        public static RoamingState FromData(RoamingData data)
        {
            if (data == null)
                return null;
            return new RoamingState()
            {
                Steps = data.Steps,
                Moved = data.Moved,
                Current = data.Current,
                Next = data.Next,
                Memory = data.Memory,
                IsCanceled = data.Canceled,
            };
        }
        #endregion
    }
}