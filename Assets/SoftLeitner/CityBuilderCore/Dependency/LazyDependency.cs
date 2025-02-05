using System.Diagnostics;

namespace CityBuilderCore
{
    public class LazyDependency<T>
    {
        private T _value;
        public T Value
        {
            [DebuggerStepThrough]
            get
            {
                if (_value == null)
                    _value = Dependencies.Get<T>();
                return _value;
            }
        }
    }
}
