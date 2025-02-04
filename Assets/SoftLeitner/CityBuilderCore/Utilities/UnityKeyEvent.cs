using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// fires unity events when a key is pressed or released
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_unity_key_event.html")]
    public class UnityKeyEvent : MonoBehaviour
    {
        [Tooltip("the key that fires the events on this behaviour on down and up")]
        public KeyCode Key;

        [Tooltip("gets fired when the defined key is pressed down")]
        public UnityEvent KeyDown;
        [Tooltip("gets fired when the defined key is released")]
        public UnityEvent KeyUp;

        private void Update()
        {
            if (Input.GetKeyDown(Key))
                KeyDown?.Invoke();
            if (Input.GetKeyUp(Key))
                KeyUp?.Invoke();
        }
    }
}