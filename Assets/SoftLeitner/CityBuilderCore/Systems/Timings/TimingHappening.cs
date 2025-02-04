using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for timed events
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_timing_happening.html")]
    public abstract class TimingHappening : ScriptableObject
    {
        [Tooltip("name of the happening for UI purposes")]
        public string Name;
        [Tooltip("optional text that causes a dialog to show when the happening starts")]
        public string StartTitle;
        [Tooltip("additional text to display when a dialog is shown at the start of the happening")]
        [TextArea]
        public string StartDescription;
        [Tooltip("optional text that causes a dialog to show when the happening ends")]
        public string EndTitle;
        [Tooltip("additional text to display when a dialog is shown at the end of the happening")]
        [TextArea]
        public string EndDescription;

        //use start/end for one time events, activate/deactivate for ongoing effects

        /// <summary>
        /// called when the happening first starts, meaning when its condition switches from being false to true<br/>
        /// use for thing that persist on their own like adding items to buildings or changing risk values<br/>
        /// the difference to <see cref="Activate"/> is that start will not be called again when the game is loaded
        /// </summary>
        public virtual void Start() { }
        /// <summary>
        /// called when the happening first end, meaning when its condition switches from bein true to being false
        /// the difference to <see cref="Deactivate"/> is that end will not be called again when the game is unloaded
        /// </summary>
        public virtual void End() { }

        /// <summary>
        /// called whenever the happening becomes active, this may be when the happening starts or when a game is loaded at a time when the happening is active
        /// used for ongoing stuff like modifers to layers and services or for visuals like rain particles
        /// </summary>
        public virtual void Activate() { }
        /// <summary>
        /// called whenever the happening becomes inactive, this may be when the happening ends or when a game is unloaded<br/>
        /// used to reset ongoing stuff from <see cref="Activate"/>
        /// </summary>
        public virtual void Deactivate() { }
    }
}