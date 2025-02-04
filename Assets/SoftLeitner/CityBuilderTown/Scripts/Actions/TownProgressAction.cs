using CityBuilderCore;
using System.Collections;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// walker action that progresses a task by calling (like harvesting or building)<br/>
    /// calls <see cref="TownTask.ProgressTask(TownWalker, string)"/> and ends when that returns false<br/>
    /// this is done so the progress value can be stored on the task which makes it possible<br/>
    /// for multiple walkers to work on the same task and for walkers to stop progressing midway
    /// </summary>
    public class TownProgressAction : AnimatedActionBase
    {
        [SerializeField]
        private string _key;

        private Coroutine _coroutine;

        public TownProgressAction() : base()
        {

        }
        public TownProgressAction(string key, string parameter) : base(parameter)
        {
            _key = key;
        }
        public TownProgressAction(string key, int parameter) : base(parameter)
        {
            _key = key;
        }

        public override void Start(Walker walker)
        {
            base.Start(walker);

            _coroutine = walker.StartCoroutine(progress((TownWalker)walker));
        }

        public override void Continue(Walker walker)
        {
            base.Continue(walker);

            _coroutine = walker.StartCoroutine(progress((TownWalker)walker));
        }

        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            walker.StopCoroutine(_coroutine);
            walker.AdvanceProcess();
        }

        private IEnumerator progress(TownWalker walker)
        {
            yield return null;

            while (walker && walker.CurrentTask.ProgressTask(walker, _key))
            {
                yield return null;
            }

            if (!walker)
                yield break;

            _coroutine = null;

            if (walker.CurrentAction == this)
                walker.AdvanceProcess();
        }
    }
}