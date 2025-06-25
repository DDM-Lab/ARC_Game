using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// applies the heightmap and various other settings of a terrain to the per renderer property of a material<br/>
    /// in combination with the HeightMapped shader this can be used to draw tilemaps over terrains
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_height_mapped_terrain_renderer.html")]
    [ExecuteAlways]
    public class HeightMappedTerrainRenderer : MonoBehaviour
    {
        [Tooltip("the heightmap of this terrain is used to change the renderer")]
        public Terrain Terrain;
        [Tooltip("the heigtmap is set as a per renderer property in this renderers material")]
        public Renderer Renderer;
        [Tooltip("property name of the height map in the renderers material")]
        public string Name = "_HeightMap";
        [Tooltip("optional, when set the terrain size is assigned into this material property, _Size scales the heightmap in relation to the rendered position")]
        public string SizeName = "_Size";
        [Tooltip("optional, when set the terrain size is assigned into this material property, _Height is multiplied with the heightmap value")]
        public string HeightName = "_Height";
        [Tooltip("optional, when set the terrain size is assigned into this material property, _HeightOffset is added to the y position")]
        public string HeightOffsetName = "_HeightOffset";
        [Tooltip("subtracted from height offset, makes road hover above the ground which may be needed to avoid z fighting")]
        public float HeightOffsetRaise = 0.01f;
        [Tooltip("optional, when set the OffsetValue is assigned into this material property, sets render depth offset(only in built in)")]
        public string OffsetName = "_Offset";
        [Tooltip("value assigned to the offset property, sets render depth which can be used to draw over other models without moving its position")]
        public float OffsetValue = -1;

        void OnEnable()
        {
            Assign();
        }

        public void Assign()
        {
            if (Renderer && Terrain)
            {
                var propertyBlock = new MaterialPropertyBlock();

                if (!string.IsNullOrWhiteSpace(Name))
                    propertyBlock.SetTexture(Name, Terrain.terrainData.heightmapTexture);

                if (!string.IsNullOrWhiteSpace(SizeName))
                    propertyBlock.SetFloat(SizeName, Terrain.terrainData.size.x);

                if (!string.IsNullOrWhiteSpace(HeightName))
                    propertyBlock.SetFloat(HeightName, Terrain.terrainData.size.y * 2f);

                if (!string.IsNullOrWhiteSpace(HeightOffsetName))
                    propertyBlock.SetFloat(HeightOffsetName, Terrain.transform.position.y - Renderer.transform.position.y + HeightOffsetRaise);

                if (!string.IsNullOrWhiteSpace(OffsetName))
                    propertyBlock.SetFloat(OffsetName, OffsetValue);

                Renderer.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
