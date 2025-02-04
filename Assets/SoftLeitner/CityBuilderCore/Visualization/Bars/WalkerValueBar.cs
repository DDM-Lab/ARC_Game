using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for in game visuals of walker values
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_value_bar.html")]
    public abstract class WalkerValueBar : MonoBehaviour
    {
        public Walker Walker => _walker;
        public virtual bool IsGlobal => false;

        protected Walker _walker;
        protected IWalkerValue _value;

        public virtual void Initialize(Walker walker, IWalkerValue value)
        {
            _walker = walker;
            _value = value;
        }

        public bool HasValue() => _value.HasValue(_walker);
        public float GetMaximum() => _value.GetMaximum(_walker);
        public float GetValue() => _value.GetValue(_walker);
        public Vector3 GetPosition() => _value.GetPosition(_walker);
        public float GetRatio()
        {
            var max = GetMaximum();
            if (max == 0)
                return 1f;
            else
                return GetValue() / max;
        }
    }
}