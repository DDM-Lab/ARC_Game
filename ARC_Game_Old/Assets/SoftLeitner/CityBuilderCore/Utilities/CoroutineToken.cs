using System.Collections;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// wrapper around coroutines that allows monitoring its activity and stopping it easily
    /// </summary>
    public class CoroutineToken
    {
        /// <summary>
        /// whether the coroutine is still running and has not been finished or stopped
        /// </summary>
        public bool IsActive { get; set; }

        private Coroutine _coroutine;
        private MonoBehaviour _owner;

        private CoroutineToken(IEnumerator routine, MonoBehaviour owner)
        {
            IsActive = true;

            _owner = owner;
            _coroutine = owner.StartCoroutine(run(routine));
        }

        private IEnumerator run(IEnumerator routine)
        {
            yield return routine;
            IsActive = false;
        }

        public void Stop()
        {
            _owner.StopCoroutine(_coroutine);
            IsActive = false;
        }

        public static CoroutineToken Start(IEnumerator routine, MonoBehaviour owner)
        {
            return new CoroutineToken(routine, owner);
        }
    }
}
