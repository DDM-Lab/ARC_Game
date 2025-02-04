using System;
using System.Linq;
using UnityEngine.Events;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// serializable utility class that combines a message with an event<br/>
    /// useful for defining actions for particular messages directly in the inspector<br/>
    /// for example playing a sound or showing some particles when a weapon is swung
    /// </summary>
    [Serializable]
    public class MessageEvent
    {
        [Tooltip("the name of the message to look for(ignores case)")]
        public string Message;
        [Tooltip("the event that is fired when the message occurs")]
        public UnityEvent Event;

        public static void Send(MessageEvent[] messageEvents, string message)
        {
            if (messageEvents == null)
                return;

            foreach (var messageEvent in messageEvents.Where(m => m.Message.Equals(message, StringComparison.OrdinalIgnoreCase)))
            {
                messageEvent.Event.Invoke();
            }
        }
    }
}
