using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper class for <see cref="Walker"/> that hold the current status when the walker is using a navmesh agent to reach a target
    /// </summary>
    public class WalkingAgentState
    {
        /// <summary>
        /// destination world position for the agent
        /// </summary>
        public Vector3 Destination { get; private set; }
        /// <summary>
        /// how far away from the destination the agent can stop
        /// </summary>
        public float Distance { get; private set; }
        /// <summary>
        /// current velocity of the agent
        /// </summary>
        public Vector3 Velocity { get; set; }
        /// <summary>
        /// whether the walking has been canceled
        /// </summary>
        public bool IsCanceled { get; private set; }

        private WalkingAgentState()
        {

        }
        public WalkingAgentState(Vector3 destination, float distance)
        {
            Destination = destination;
            Distance = distance;
        }

        /// <summary>
        /// requests for the agent walking to be canceled, it might not end immediately but at the next possible point
        /// </summary>
        public void Cancel()
        {
            IsCanceled = true;
        }

        #region Saving
        [Serializable]
        public class WalkingAgentData
        {
            public Vector3 Destination;
            public Vector3 Velocity;
        }

        public WalkingAgentData GetData() => new WalkingAgentData()
        {
            Destination = Destination,
            Velocity = Velocity
        };
        public static WalkingAgentState FromData(WalkingAgentData data)
        {
            if (data == null)
                return null;
            return new WalkingAgentState()
            {
                Destination = data.Destination,
                Velocity = data.Velocity
            };
        }
        #endregion
    }
}