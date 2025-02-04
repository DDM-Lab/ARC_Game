using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// request sent to the <see cref="INotificationManager"/> to request that a notification gets displayed
    /// </summary>
    public class NotificationRequest
    {
        /// <summary>
        /// text of the notification
        /// </summary>
        public string Text { get; set; }
        /// <summary>
        /// transform relevant to the notification, in the default implementation when a player clicks the notification the camera will start following it
        /// </summary>
        public Transform Leader { get; set; }
        /// <summary>
        /// position relevant to the notification, in the default implementation when a player clicks the notification the camera will jump to it
        /// </summary>
        public Vector3? Position { get; set; }

        public NotificationRequest()
        {

        }
        public NotificationRequest(string text)
        {
            Text = text;
        }
        public NotificationRequest(string text, Vector3 position)
        {
            Text = text;
            Position = position;
        }
        public NotificationRequest(string text, Transform leader)
        {
            Text = text;
            Leader = leader;
        }
    }
}
