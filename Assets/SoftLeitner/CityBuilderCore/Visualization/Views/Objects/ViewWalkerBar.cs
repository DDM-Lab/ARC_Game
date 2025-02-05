using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for views that visualize a <see cref="IWalkerValue"/> with a bar
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class ViewWalkerBar<T> : ViewWalkerBarBase where T : IWalkerValue
    {
        [Tooltip("the walker value displayed by this view")]
        public T Value;

        public override IWalkerValue WalkerValue => Value;
    }
}