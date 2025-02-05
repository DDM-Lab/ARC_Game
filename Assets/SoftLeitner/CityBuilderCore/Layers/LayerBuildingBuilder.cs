using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// special building builder that also highlights points around the building<br/>
    /// these points follow the same rules as layers(range, falloff) so they can be used as a preview for layer affectors
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/layers">https://citybuilder.softleitner.com/manual/layers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_layer_building_builder.html")]
    public class LayerBuildingBuilder : BuildingBuilder
    {
        [Tooltip("value inside the affector")]
        public int Value;
        [Tooltip("range of points outside the affector")]
        public int Range;
        [Tooltip("value subtracted for every step outside the affector")]
        public int Falloff;
        [Tooltip("gradient that determines the color for any layer value that will be displayed as an overlay")]
        public Gradient Gradient;
        [Tooltip("layer value for which the lowest gradient value will be used")]
        public int Minimum;
        [Tooltip("layer value at which the highest gradient value will be used")]
        public int Maximum;
        [Header("Tiles")]
        [Tooltip("can be set to visualize the layer preview on a tilemap instead of highlighting")]
        public Tilemap Tilemap;
        [Tooltip("tile used and recolored when using a tilemap to show the preview")]
        public Tile Tile;

        public override void DeactivateTool()
        {
            base.DeactivateTool();

            if (Tilemap)
                Tilemap.ClearAllTiles();
        }

        protected override void updatePreview(List<Vector2Int> buildPoints, Vector2Int size, Func<Vector2Int, bool> validityChecker)
        {
            base.updatePreview(buildPoints, size, validityChecker);

            if (Tilemap)
                Tilemap.ClearAllTiles();

            if (buildPoints != null && buildPoints.Count > 0 && validityChecker(buildPoints[0]))
            {
                var buildingPoints = PositionHelper.GetStructurePositions(buildPoints[0], size).ToList();
                var values = new Dictionary<Vector2Int, int>();

                foreach (var buildingPoint in buildingPoints)
                {
                    var value = Value;

                    for (int i = 0; i <= Range; i++)
                    {
                        foreach (var point in PositionHelper.GetAdjacent(buildingPoint, Vector2Int.one, true, i - 1))
                        {
                            if (values.ContainsKey(point))
                            {
                                if (Mathf.Abs(values[point]) < Mathf.Abs(value))
                                    values[point] = value;
                            }
                            else
                            {
                                values.Add(point, value);
                            }
                        }

                        value -= Falloff;
                    }
                }

                var range = Maximum - Minimum;
                var bottom = -Minimum;

                if (Tilemap)
                {
                    foreach (var value in values)
                    {
                        setTile((Vector3Int)value.Key, Gradient.Evaluate((float)(value.Value + bottom) / range));
                    }
                }
                else
                {
                    foreach (var value in values)
                    {
                        if (buildingPoints.Contains(value.Key))
                            continue;

                        _highlighting.Highlight(value.Key, Gradient.Evaluate((float)(value.Value + bottom) / range));
                    }
                }
            }
        }

        private void setTile(Vector3Int point, Color color)
        {
            Tilemap.SetTile(point, Tile);
            Tilemap.SetTileFlags(point, TileFlags.None);
            Tilemap.SetColor(point, color);
        }
    }
}
