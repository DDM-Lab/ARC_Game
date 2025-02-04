using System;
using UnityEngine;

namespace CityBuilderCore
{
    public interface IObjectPool
    {
        void Release(Component prefab, Component instance);
        T Request<T>(T prefab, Transform parent, Func<T, bool> check = null) where T : Component;
    }
}