using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// scriptable object that stores an array of other objects<br/>
    /// used for things like <see cref="BuildingInfoSet"/> to store a known collection of buildings
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_object_set_base.html")]
    public abstract class ObjectSetBase : ScriptableObject
    {
        public abstract Type GetObjectType();
        public abstract void SetObjects(object[] objects);
    }
}