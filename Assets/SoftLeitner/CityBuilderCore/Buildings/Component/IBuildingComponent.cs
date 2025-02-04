using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// permanent components making up a buildings behaviour<br/>
    /// as such they have to be added to the building transform in the building prefabs<br/>
    /// when a building is replaced its components are destroyed with their building<br/>
    /// onReplacing is called beforehand so the component can transfer its items and such to an equivalent component in the new building
    /// </summary>
    public interface IBuildingComponent : ISaveData
    {
        /// <summary>
        /// unique key used to identify the component in save/load
        /// </summary>
        string Key { get; }

        /// <summary>
        /// the building the component is attached to<br/>
        /// is set by the building in awake
        /// </summary>
        IBuilding Building { get; set; }

        /// <summary>
        /// only called when the building is originally placed, before Initialize
        /// </summary>
        void SetupComponent();
        /// <summary>
        /// initialization is performed when the building is placed or loaded<br/>
        /// use to create references, register traits, ...
        /// </summary>
        void InitializeComponent();
        /// <summary>
        /// termination is performed when the building is destroyed<br/>
        /// use to deregister traits, remove references from other systems
        /// </summary>
        void TerminateComponent();
        /// <summary>
        /// called when a component gets replaced<br/>
        /// use to transfer resources, replace references
        /// </summary>
        /// <param name="replacement"></param>
        void OnReplacing(IBuilding replacement);

        /// <summary>
        /// called when the building is about to be moved<br/>
        /// can be used to remove/deregister stuff from the old position
        /// </summary>
        void OnMoving();
        /// <summary>
        /// called after a building has been moved<br/>
        /// can be used to register things at the new position
        /// </summary>
        /// <param name="oldPoint">point of the building before moving</param>
        /// <param name="oldRotation">rotation of the building before moving</param>
        void OnMoved(Vector2Int oldPoint, BuildingRotation oldRotation);

        /// <summary>
        /// temporarily stops the component from working
        /// </summary>
        void SuspendComponent();
        /// <summary>
        /// resumes work in the component after <see cref="SuspendComponent"/> has been called
        /// </summary>
        void ResumeComponent();

        /// <summary>
        /// text displayed in scene editor
        /// </summary>
        /// <returns></returns>
        string GetDebugText();
        /// <summary>
        /// text that may be displayed in dialogs
        /// </summary>
        /// <returns></returns>
        string GetDescription();
    }
}