using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for building components implementing <see cref="IBuildingComponent"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_component.html")]
    [RequireComponent(typeof(IBuilding))]
    public abstract class BuildingComponent : MonoBehaviour, IBuildingComponent
    {
        public abstract string Key { get; }

        private IBuilding _building;
        public IBuilding Building { get => _building ?? GetComponent<IBuilding>(); set => _building = value; }

        public virtual void SetupComponent() { }
        public virtual void InitializeComponent() { }
        public virtual void OnReplacing(IBuilding replacement) { }
        public virtual void TerminateComponent() { }

        public virtual void OnMoving() { }
        public virtual void OnMoved(Vector2Int oldPoint, BuildingRotation oldRotation) { }

        public virtual void SuspendComponent() { }
        public virtual void ResumeComponent() { }

        /// <summary>
        /// gets the first building component of the specified type on the same building
        /// </summary>
        /// <typeparam name="T">type of the building component to return</typeparam>
        /// <returns>a building component if one is found</returns>
        protected T getOther<T>() where T : class, IBuildingComponent => Building?.GetBuildingComponent<T>();
        /// <summary>
        /// gets all building components of the specified type on the same building
        /// </summary>
        /// <typeparam name="T">type of the building components to return</typeparam>
        /// <returns>any building components on the same building that match the type</returns>
        protected IEnumerable<T> getOthers<T>() where T : class, IBuildingComponent => Building?.GetBuildingComponents<T>();

        /// <summary>
        /// registers the building component as a trait so it is globally accessible<br/>
        /// usually done in <see cref="InitializeComponent"/>
        /// </summary>
        /// <typeparam name="T">trait type to register, the building component or an interface it implements</typeparam>
        /// <param name="trait">object that will be registered, almost always this building component</param>
        /// <returns></returns>
        protected BuildingComponentReference<T> registerTrait<T>(T trait) where T : IBuildingTrait<T>
        {
            return Dependencies.Get<IBuildingManager>().RegisterBuildingTrait(trait);
        }
        /// <summary>
        /// replaces the reference a trait points to in the global manager<br/>
        /// done when a building is replaced(<see cref="OnReplacing(IBuilding)"/>) so the trait now points to the new component instead of the one that has been removed
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="trait"></param>
        /// <param name="replacement"></param>
        protected void replaceTrait<T>(T trait, T replacement) where T : IBuildingTrait<T>
        {
            Dependencies.Get<IBuildingManager>().ReplaceBuildingTrait(trait, replacement);
        }
        /// <summary>
        /// removes a previously registered trait<br/>
        /// usually when a building is removed which calls <see cref="TerminateComponent"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="trait"></param>
        protected void deregisterTrait<T>(T trait) where T : IBuildingTrait<T>
        {
            Dependencies.Get<IBuildingManager>().DeregisterBuildingTrait(trait);
        }

        /// <summary>
        /// returns text that gets displayed for debugging in the scene editor
        /// </summary>
        /// <returns>a debugging string or null</returns>
        public virtual string GetDebugText() => null;
        /// <summary>
        /// gets displayed in dialogs to show the component status to players
        /// </summary>
        /// <returns>a descriptive string or an empty one</returns>
        public virtual string GetDescription() => string.Empty;

        #region Saving
        public virtual string SaveData() => string.Empty;
        public virtual void LoadData(string json) { }
        #endregion
    }
}