using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// provides unity events invoked by the most common unity messages like Start or OnEnable<br/>
    /// also contains a couple helper functions that may be useful to be called from the events
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_behaviour_event_trigger.html")]
    public class BehaviourEventTrigger : MonoBehaviour
    {
        [Tooltip("fires when Awake is called on this behaviour")]
        public UnityEvent Awakened;
        [Tooltip("fires when OnEnable is called on this behaviour")]
        public UnityEvent Enabled;
        [Tooltip("fires when Start is called on this behaviour")]
        public UnityEvent Started;
        [Tooltip("fires when OnDisable is called on this behaviour")]
        public UnityEvent Disabled;
        [Tooltip("fires when OnDestroy is called on this behaviour")]
        public UnityEvent Destroyed;
        [Tooltip("fires when OnDestroy is called on this behaviour and the scene is still loaded(not called when the scene is switched)")]
        public UnityEvent DestroyedLoaded;

        private void Awake()
        {
            Awakened?.Invoke();
        }

        private void Start()
        {
            Started?.Invoke();
        }

        private void OnEnable()
        {
            Enabled?.Invoke();
        }

        private void OnDisable()
        {
            Disabled?.Invoke();
        }

        private void OnDestroy()
        {
            Destroyed?.Invoke();
            if (gameObject.scene.isLoaded)
                DestroyedLoaded?.Invoke();
        }

        public void ResetPosition(bool global)
        {
            if (global)
                transform.position = Vector3.zero;
            else
                transform.localPosition = Vector3.zero;
        }
        public void ResetRotation(bool global)
        {
            if (global)
                transform.rotation = Quaternion.identity;
            else
                transform.localRotation = Quaternion.identity;
        }
    }
}
