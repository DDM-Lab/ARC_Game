using System.Collections;
using UnityEngine;

namespace CityBuilderCore
{
    public class RotateAction : WaitAction
    {
        [SerializeField]
        private Quaternion _rotation;

        public RotateAction(Quaternion rotation, float time):base(time)
        {
            _rotation = rotation;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            walker.StartCoroutine(rotate(walker));
        }

        public override void Continue(Walker walker)
        {
            base.Continue(walker);

            walker.StartCoroutine(rotate(walker));
        }

        private IEnumerator rotate(Walker walker)
        {
            var start = walker.Pivot.rotation;

            while (walker.CurrentWaiting != null)
            {
                walker.Pivot.rotation = Quaternion.Lerp(start, _rotation, walker.CurrentWaiting.Progress);
                yield return null;
            }

            walker.Pivot.rotation = _rotation;
        }
    }
}
