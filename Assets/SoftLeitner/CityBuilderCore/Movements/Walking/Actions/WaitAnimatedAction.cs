using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// action that simply waits for a set time and plays an animation
    /// </summary>
    [Serializable]
    public class WaitAnimatedAction : WaitAction
    {
        [SerializeField]
        private int _parameter;

        public WaitAnimatedAction() : base()
        {

        }
        public WaitAnimatedAction(float time, string parameter) : base(time)
        {
            _parameter = Animator.StringToHash(parameter);
        }
        public WaitAnimatedAction(float time, int parameter) : base(time)
        {
            _parameter = parameter;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            walker.Animator.SetBool(_parameter, true);
        }
        public override void Continue(Walker walker)
        {
            base.Continue(walker);

            walker.Animator.SetBool(_parameter, true);
        }
        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            walker.Animator.SetBool(_parameter, false);
        }
        public override void End(Walker walker)
        {
            base.End(walker);

            walker.Animator.SetBool(_parameter, false);
        }
    }
}
