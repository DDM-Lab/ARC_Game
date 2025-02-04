namespace CityBuilderCore
{
    /// <summary>
    /// shows notification texts to players
    /// </summary>
    public interface INotificationManager
    {
        /// <summary>
        /// requests that a notification gets displayed to the player
        /// </summary>
        /// <param name="request">the request to display to the player</param>
        void Notify(NotificationRequest request);
    }
}
