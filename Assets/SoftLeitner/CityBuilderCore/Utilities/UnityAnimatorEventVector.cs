using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// proxy for setting animator values from unityevents
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_unity_animator_event_vector.html")]
    [RequireComponent(typeof(Animator))]
    public class UnityAnimatorEventVector : MonoBehaviour
    {
        [Tooltip("parameter name that gets components of the vector set by the helper methods(with associated postfixes)")]
        public string Parameter;
        [Tooltip("when true the X component of the passed vector gets set to <Parameter>X")]
        public bool SetX;
        [Tooltip("when true the Y component of the passed vector gets set to <Parameter>Y")]
        public bool SetY;
        [Tooltip("when true the Z component of the passed vector gets set to <Parameter>Z")]
        public bool SetZ;

        private int _hashX, _hashY, _hashZ;
        private Animator _animator;

        private void Awake()
        {
            _animator = GetComponent<Animator>();

            if (SetX)
                _hashX = Animator.StringToHash(Parameter + "X");
            if (SetY)
                _hashY = Animator.StringToHash(Parameter + "Y");
            if (SetX)
                _hashZ = Animator.StringToHash(Parameter + "Z");
        }

        public void SetVector3(Vector3 vector)
        {
            if (SetX)
                _animator.SetFloat(_hashX, vector.x);
            if (SetY)
                _animator.SetFloat(_hashY, vector.y);
            if (SetZ)
                _animator.SetFloat(_hashZ, vector.z);
        }
    }
}