using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// default implementation of <see cref="INotificationManager"/><br/>
    /// instantiates a <see cref="NotificationPanel"/> for every notification that is requested
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_notification_manager.html")]
    public class DefaultNotificationManager : MonoBehaviour, INotificationManager
    {
        [Tooltip("gets instantiated for every notification")]
        public NotificationPanel Prefab;
        [Tooltip("the parent for the panels")]
        public Transform Parent;
        [Tooltip("maximum amount of panels at once")]
        public int Maximum = 10;

        private List<NotificationPanel> _panels = new List<NotificationPanel>();

        private void Awake()
        {
            Dependencies.Register<INotificationManager>(this);
        }

        public void Notify(NotificationRequest request)
        {
            var panel = Instantiate(Prefab, Parent);

            panel.transform.SetAsFirstSibling();
            panel.Request = request;
            panel.gameObject.SetActive(true);

            _panels.Add(panel);
            _panels.Cleanup();

            while(_panels.Count>Maximum)
            {
                Destroy(_panels[0].gameObject);
                _panels.RemoveAt(0);
            }
        }
    }
}
