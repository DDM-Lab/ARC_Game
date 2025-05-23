﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// changes materials of renderers on the building while active<br/>
    /// for example for outline or highlight materials
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_addon_material.html")]
    public class BuildingAddonMaterial : BuildingAddon
    {
        [Tooltip("whether the original materials should be removed before adding the addon ones")]
        public bool Replace;
        [Tooltip("paths to the transforms that contains the renderers starting from Pivot, empty for all")]
        public string[] Targets;
        [Tooltip("materials that are added to the renderers while the addon is active")]
        public Material[] Materials;

        private Renderer[] _renderers;
        private List<Material[]> _originalMaterials;

        public override void InitializeAddon()
        {
            base.InitializeAddon();

            _renderers = getRenderers();

            if (Replace)
            {
                _originalMaterials = new List<Material[]>();
                foreach (var renderer in _renderers)
                {
                    if (renderer is ParticleSystemRenderer)
                        continue;
                    _originalMaterials.Add(renderer.sharedMaterials);
                    renderer.sharedMaterials = Materials;
                }
            }
            else
            {
                foreach (var renderer in _renderers)
                {
                    if (renderer is ParticleSystemRenderer)
                        continue;
                    renderer.sharedMaterials = renderer.sharedMaterials.Concat(Materials).ToArray();
                }
            }
        }

        public override void TerminateAddon()
        {
            base.TerminateAddon();

            if (Replace)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] == null || _renderers[i] is ParticleSystemRenderer)
                        continue;
                    _renderers[i].sharedMaterials = _originalMaterials[i];
                }
            }
            else
            {
                foreach (var renderer in _renderers)
                {
                    if (renderer == null || renderer is ParticleSystemRenderer)
                        continue;
                    renderer.sharedMaterials = renderer.sharedMaterials.SkipLast(Materials.Length).ToArray();
                }
            }
        }

        private Renderer[] getRenderers()
        {
            if (Targets != null && Targets.Length > 0)
            {
                return Targets.Select(t => Building.Pivot.Find(t).GetComponent<Renderer>()).ToArray();
            }
            else
            {
                return Building.Pivot.GetComponentsInChildren<Renderer>();
            }
        }
    }
}