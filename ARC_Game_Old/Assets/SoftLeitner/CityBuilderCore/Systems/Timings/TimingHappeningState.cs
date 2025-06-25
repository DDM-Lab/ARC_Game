using System;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// helper class that keeps track of the state of happenings and correctly calls their lifetime methods
    /// </summary>
    public class TimingHappeningState
    {
        public TimingHappeningOccurence HappeningOccurence { get; private set; }
        public bool IsActive { get; private set; }

        public bool HasText => !(string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Description));
        public string Title => IsActive ? HappeningOccurence.Happening.StartTitle : HappeningOccurence.Happening.EndTitle;
        public string Description => IsActive ? HappeningOccurence.Happening.StartDescription : HappeningOccurence.Happening.EndDescription;

        /// <summary>
        /// debug constructor for custom editor, do not use in game
        /// </summary>
        /// <param name="happening"></param>
        /// <param name="isActive"></param>
        public TimingHappeningState(TimingHappening happening, bool isActive)
        {
            HappeningOccurence = new TimingHappeningOccurence() { Happening = happening };
            IsActive = isActive;
        }
        public TimingHappeningState(TimingHappeningOccurence happeningOccurence)
        {
            HappeningOccurence = happeningOccurence;
        }

        public void Start(float playtime, int seed)
        {
            if (HappeningOccurence.GetIsOccuring(playtime, seed))
            {
                IsActive = true;
                HappeningOccurence.Happening.Activate();
            }
        }

        public bool Check(float playtime, int seed)
        {
            var isOccuring = HappeningOccurence.GetIsOccuring(playtime, seed);

            if (IsActive == isOccuring)
                return false;

            IsActive = isOccuring;

            if (IsActive)
            {
                HappeningOccurence.Happening.Start();
                HappeningOccurence.Happening.Activate();
            }
            else
            {
                HappeningOccurence.Happening.Deactivate();
                HappeningOccurence.Happening.End();
            }

            return true;
        }

        public void End()
        {
            if (IsActive)
            {
                IsActive = false;
                HappeningOccurence.Happening.Deactivate();
            }
        }
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class TimingHappeningStateEvent : UnityEvent<TimingHappeningState> { }
}