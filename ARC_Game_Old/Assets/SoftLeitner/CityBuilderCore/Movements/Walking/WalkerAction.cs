using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// encapsulated action a walker can perform, started as part of a <see cref="ProcessState"/> in <see cref="Walker.StartProcess(WalkerAction[], string)"/><br/>
    /// this enables easier higher level walker control, the walker no longer has to directly hold all the state of the different actions it can perform<br/>
    /// instead it can just start a process that does multiple things in series(walk somewhere>pick up item>walk back>deposit item)<br/>
    /// these are directly serialized, if they contain state that is not directly serializable use <see cref="ISerializationCallbackReceiver"/>
    /// </summary>
    [Serializable]
    public abstract class WalkerAction
    {
        /// <summary>
        /// called by the walker when the action is first started, either by being first in a process or when the previous action has ended
        /// </summary>
        /// <param name="walker">the walker performing this action</param>
        public virtual void Start(Walker walker) { }
        /// <summary>
        /// called by the walker after the game has been loaded
        /// </summary>
        /// <param name="walker">the walker performing this action</param>
        public virtual void Continue(Walker walker) { }
        /// <summary>
        /// called by the walker if the action was active when its process was canceled
        /// </summary>
        /// <param name="walker">the walker performing this action</param>
        public virtual void Cancel(Walker walker) { }
        /// <summary>
        /// called by the walker when this is the active action and the process advances<br/>
        /// do not call this directly, to end an action call <see cref="Walker.AdvanceProcess"/>
        /// </summary>
        /// <param name="walker"></param>
        public virtual void End(Walker walker) { }
    }
}
