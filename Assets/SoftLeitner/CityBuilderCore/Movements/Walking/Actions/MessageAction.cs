using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// action that sends a message to the walker, can be intercepted on the walkers message events for example to play sounds<br/>
    /// ends immediately so only really makes sense as part of a larger process
    /// </summary>
    [Serializable]
    public class MessageAction : WalkerAction
    {
        [SerializeField]
        private string _message;

        public MessageAction()
        {

        }
        public MessageAction(string message)
        {
            _message = message;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            walker.OnMessage(_message);
            walker.AdvanceProcess();
        }
    }
}
