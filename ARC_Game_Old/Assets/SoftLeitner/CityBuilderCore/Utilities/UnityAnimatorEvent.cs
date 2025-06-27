using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// proxy for setting animator values from unityevents
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_unity_animator_event.html")]
    [RequireComponent(typeof(Animator))]
    public class UnityAnimatorEvent : MonoBehaviour
    {
        [Tooltip("parameter name used by helper methods SetBool/SetInteger/SetFloat")]
        public string Parameter;

        private int _hash;
        private Animator _animator;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _hash = Animator.StringToHash(Parameter);
        }

        public void SetBool(bool value) => _animator.SetBool(_hash, value);
        public void SetInteger(int value) => _animator.SetInteger(_hash, value);
        public void SetFloat(float value) => _animator.SetFloat(_hash, value);
    }
}