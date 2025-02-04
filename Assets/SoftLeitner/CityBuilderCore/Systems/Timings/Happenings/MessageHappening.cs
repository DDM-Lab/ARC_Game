using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that sends messages to a named object, leave messages empty to not send
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_message_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(MessageHappening))]
    public class MessageHappening : TimingHappening
    {
        [Tooltip("name of the gameobject that will get a message sent")]
        public string ObjectName;

        [Tooltip("message that may be sent when the happening first starts")]
        public string StartMessage;
        [Tooltip("message that may be sent when the happening ends")]
        public string EndMessage;

        [Tooltip("message that may be sent when the happening becomes active")]
        public string ActivateMessage;
        [Tooltip("message that may be sent when the happening becomes inactive")]
        public string DeactivateMessage;

        public override void Start()
        {
            base.Start();

            if (!string.IsNullOrWhiteSpace(StartMessage))
                GameObject.Find(ObjectName).SendMessage(StartMessage);
        }
        public override void End()
        {
            base.End();

            if (!string.IsNullOrWhiteSpace(EndMessage))
                GameObject.Find(ObjectName).SendMessage(EndMessage);
        }

        public override void Activate()
        {
            base.Activate();

            if (!string.IsNullOrWhiteSpace(ActivateMessage))
                GameObject.Find(ObjectName).SendMessage(ActivateMessage);
        }
        public override void Deactivate()
        {
            base.Deactivate();

            if (!string.IsNullOrWhiteSpace(DeactivateMessage))
                GameObject.Find(ObjectName).SendMessage(DeactivateMessage);
        }
    }
}
