using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// requirement used in <see cref="EvolutionStage"/> to specify that a building needs access to a certain number of services in a category to evolve<br/>
    /// for example a building may need access to 3 different temples and 2 type of entertainment to evolve
    /// </summary>
    [Serializable]
    public class ServiceCategoryRequirement
    {
        [Tooltip("how many different services of the category are needed")]
        public int Quantity;
        [Tooltip("the category of services that are counted")]
        public ServiceCategory ServiceCategory;

        public bool IsFulfilled(Service[] services)
        {
            return services.Where(s => ServiceCategory.Contains(s)).Count() >= Quantity;
        }
    }
}