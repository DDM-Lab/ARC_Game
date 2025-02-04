using CityBuilderCore;
using System.Collections;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// walker action that is triggered when a walker runs out of energy from working<br/>
    /// the action leaves the walker standing still and calls <see cref="TownWalker.Recover"/> until the walker returns false when it has full energy again
    /// </summary>
    public class TownRecoveryAction : WalkerAction
    {
        private Coroutine _coroutine;

        public override void Start(Walker walker)
        {
            base.Start(walker);

            _coroutine = walker.StartCoroutine(recover((TownWalker)walker));
        }

        public override void Continue(Walker walker)
        {
            base.Continue(walker);

            _coroutine = walker.StartCoroutine(recover((TownWalker)walker));
        }

        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            walker.StopCoroutine(_coroutine);
            walker.AdvanceProcess();
        }

        private IEnumerator recover(TownWalker walker)
        {
            while (walker.Recover())
            {
                yield return null;
            }

            walker.AdvanceProcess();
        }
    }
}
