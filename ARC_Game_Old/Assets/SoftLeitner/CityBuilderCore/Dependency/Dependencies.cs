using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.SceneManagement;

namespace CityBuilderCore
{
    /// <summary>
    /// very rudementary dependency management used to decouple implementations<br/>
    /// dependencies get cleared whenever the scene changes<br/>
    /// in this implementaton all registered types are quasi singletons<br/>
    /// could be replaced with something more sophisticated like a tagged or scoped dependencies if necessary<br/>
    /// for the purpose of this framework it has been left as simple as possible for performance
    /// </summary>
    public static class Dependencies
    {
        private static Dictionary<Type, object> _dependencies = new Dictionary<Type, object>();

        static Dependencies()
        {
            SceneManager.sceneUnloaded += (s) => Clear();
        }

        public static void Register<T>(T instance)
        {
            if (_dependencies.ContainsKey(typeof(T)))
                throw new Exception($"Duplicate Dependency {typeof(T).Name}! ({instance})");

            _dependencies.Add(typeof(T), instance);
        }

        public static bool Contains<T>()
        {
            return _dependencies.ContainsKey(typeof(T));
        }

        /// <summary>
        /// returns the dependency of the type, throws if the dependency is not found
        /// </summary>
        /// <typeparam name="T">type of the desired dependency</typeparam>
        /// <returns>the registered dependency</returns>
        [DebuggerStepThrough]
        public static T Get<T>()
        {
            if (_dependencies.TryGetValue(typeof(T), out object value))
                return (T)value;
            else
                throw new Exception($"Missing Dependency {typeof(T).Name}!");
        }

        /// <summary>
        /// retrieves a dependency if it was registered
        /// </summary>
        /// <typeparam name="T">type of the dependency to look for</typeparam>
        /// <returns>the dependency or the default value of the type if no dependency is found</returns>
        [DebuggerStepThrough]
        public static T GetOptional<T>()
        {
            if (_dependencies.TryGetValue(typeof(T), out object value))
                return (T)value;
            else
                return default;
        }

        /// <summary>
        /// tries to retrieve a registered dependency
        /// </summary>
        /// <typeparam name="T">type of the depency to look for</typeparam>
        /// <param name="instance">the registered dependency</param>
        /// <returns>true if the dependency was found</returns>
        [DebuggerStepThrough]
        public static bool TryGet<T>(out T instance)
        {
            if (_dependencies.TryGetValue(typeof(T), out object value))
            {
                instance = (T)value;
                return true;
            }
            else
            {
                instance = default;
                return false;
            }
        }

        public static void Clear()
        {
            _dependencies.Clear();
        }
    }
}