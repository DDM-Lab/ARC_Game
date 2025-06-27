using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace CityBuilderCore
{
    [Serializable]
    public class EvolutionStage
    {
        [Tooltip("the building that will replace the current one when it evolves to this stage")]
        public BuildingInfo BuildingInfo;

        [Tooltip("evolution requires services(water, education, ...)")]
        public Service[] Services;
        [Tooltip("evolution requires a number of a type of service(2 types of religion for example)")]
        [FormerlySerializedAs("ServieCategoryRequirements")]
        public ServiceCategoryRequirement[] ServiceCategoryRequirements;
        [Tooltip("evolution requires specific items(pottery, ...)")]
        public Item[] Items;
        [Tooltip("evolution requires a number of a type of item(2 types of food for example)")]
        public ItemCategoryRequirement[] ItemCategoryRequirements;
        [Tooltip("evolution requires some layer value(desirability between 5 and 100)")]
        public LayerRequirement[] LayerRequirements;

        /// <summary>
        /// checks if all requirements for this stage are met
        /// </summary>
        /// <param name="point">used to determine layer values</param>
        /// <param name="services">services accessible to the building</param>
        /// <param name="items">items available in the building</param>
        /// <returns></returns>
        public bool Check(Vector2Int point, Service[] services, Item[] items)
        {
            if (Services != null && !Services.All(s => services.Contains(s)))
                return false;
            if (ServiceCategoryRequirements != null && !ServiceCategoryRequirements.All(s => s.IsFulfilled(services)))
                return false;
            if (Items != null && !Items.All(s => items.Contains(s)))
                return false;
            if (Items != null && !ItemCategoryRequirements.All(s => s.IsFulfilled(items)))
                return false;
            if (LayerRequirements != null && !LayerRequirements.All(r => r.IsFulfilled(point)))
                return false;
            return true;
        }
    }
}