using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for walker actions that play an animation while they are active<br/>
    /// sets a bool parameter in the <see cref="Walker.Animator"/> to true when started and resets it when is ends
    /// </summary>
    public abstract class AnimatedActionBase : WalkerAction
    {
        [SerializeField]
        private int _parameter;

        public AnimatedActionBase() : base()
        {

        }
        public AnimatedActionBase(string parameter)
        {
            _parameter = Animator.StringToHash(parameter);
        }
        public AnimatedActionBase(int parameter)
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
