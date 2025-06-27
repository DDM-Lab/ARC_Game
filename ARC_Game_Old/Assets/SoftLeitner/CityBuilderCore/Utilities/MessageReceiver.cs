using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// behaviour that can receive and pass simple string messages<br/>
    /// events allow attaching actions to certain messages in the inspector
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_message_receiver.html")]
    public class MessageReceiver : MonoBehaviour
    {
        [Header("Messages")]
        [Tooltip("when set all messages are forwarded to this messanger")]
        public MessageReceiver ForwardReceiver;
        [Tooltip("fires when the character receives a message, messages may be sent from animators, timelines or even the character itself")]
        public UnityEvent<string> MessageReceived;
        [Tooltip("define callbacks for particular messages")]
        public MessageEvent[] MessageEvents;

        /// <summary>
        /// use when a single string has to be split into several messages<br/>
        /// by default the parameter is split by spaces
        /// </summary>
        /// <param name="e"></param>
        public virtual void OnMessages(string e)
        {
            foreach (var parameter in e.Split(' '))
            {
                OnMessage(parameter);
            }
        }
        /// <summary>
        /// puts a message into the characters messaging pipeline
        /// </summary>
        /// <param name="e"></param>
        public virtual void OnMessage(string e)
        {
            MessageReceived?.Invoke(e);
            MessageEvent.Send(MessageEvents, e);

            if (ForwardReceiver)
                ForwardReceiver.OnMessage(e);
        }
    }
}
