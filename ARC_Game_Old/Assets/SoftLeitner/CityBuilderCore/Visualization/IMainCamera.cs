using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// interface used to give acccess to various main camera related things through the dependency system<br/>
    /// for example bars that display icons(<see cref="BuildingItemsBar"/>) have to align the icon with the main cam
    /// </summary>
    public interface IMainCamera
    {
        /// <summary>
        /// the main camera of the game
        /// </summary>
        Camera Camera { get; }
        /// <summary>
        /// position the camera point to on the map, not the position of the camera itself 
        /// </summary>
        Vector3 Position { get; set; }
        /// <summary>
        /// current rotation of the main camera(used only to persist camera rotation currently), only applies in 3d
        /// </summary>
        Quaternion Rotation { get; set; }
        /// <summary>
        /// current size of the main camera(used only to persist camera size currently)
        /// </summary>
        float Size { get; set; }

        /// <summary>
        /// sets the culling of the main camera to the passed mask<br/>
        /// the original mask is saved so it can be restored using <see cref="ResetCulling"/>
        /// </summary>
        /// <param name="layerMask">the layer mask to set on the main camera</param>
        void SetCulling(LayerMask layerMask);
        /// <summary>
        /// resets the culling on the main camera to its original layer mask after is has been changed by <see cref="SetCulling(LayerMask)"/>
        /// </summary>
        void ResetCulling();

        /// <summary>
        /// quickly moves the camera to a position on the map<br/>
        /// for example to focus on some happening
        /// </summary>
        /// <param name="position">the absolute position on the map</param>
        /// <param name="rotation">optional rotation the camera should have</param>
        CoroutineToken Jump(Vector3 position, Quaternion? rotation = null);
        /// <summary>
        /// moves the camera to a transform and follows it<br/>
        /// following is canceled when the user moves the camera or the transform becomes inactive
        /// </summary>
        /// <param name="leader">the transform the camera should follow</param>
        CoroutineToken Follow(Transform leader);
    }
}