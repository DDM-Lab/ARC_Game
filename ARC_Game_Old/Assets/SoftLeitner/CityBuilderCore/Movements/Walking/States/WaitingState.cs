using System;
using System.Collections;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper class for <see cref="Walker"/> that hold the current status when it is just waiting for a set time<br/>
    /// </summary>
    public class WaitingState
    {
        /// <summary>
        /// the total time to wait before waiting ends[s]
        /// </summary>
        public float SetTime { get; private set; }
        /// <summary>
        /// the time already waited[s]
        /// </summary>
        public float WaitTime { get; set; }

        public bool IsFinished => WaitTime >= SetTime;
        public float Progress => WaitTime / SetTime;

        private WaitingState()
        {

        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="setTime">total waiting time in seconds</param>
        public WaitingState(float setTime)
        {
            SetTime = setTime;
        }

        public IEnumerator Wait()
        {
            while (!IsFinished)
            {
                yield return null;
                WaitTime += Time.deltaTime;
            }
        }

        public void Cancel()
        {
            SetTime = 0f;
        }

        #region Saving
        [Serializable]
        public class WaitingData
        {
            public float SetTime;
            public float WaitTime;
        }

        public WaitingData GetData() => new WaitingData()
        {
            SetTime = SetTime,
            WaitTime = WaitTime
        };
        public static WaitingState FromData(WaitingData data)
        {
            if (data == null || data.SetTime == 0f)
                return null;
            return new WaitingState()
            {
                SetTime = data.SetTime,
                WaitTime = data.WaitTime
            };
        }
        #endregion
    }
}