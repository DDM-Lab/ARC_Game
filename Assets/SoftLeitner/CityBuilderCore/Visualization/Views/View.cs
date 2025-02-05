using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for views<br/>
    /// views are ways to show additional information in game that can be activated<br/>
    /// for example bars over buildings, different camera cullings, overlays on tilemaps, ...
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/views">https://citybuilder.softleitner.com/manual/views</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_view.html")]
    public abstract class View : ScriptableObject
    {
        [Tooltip("name of the view that may be used in UI")]
        public string Name;

        public abstract void Activate();
        public abstract void Deactivate();
    }
}