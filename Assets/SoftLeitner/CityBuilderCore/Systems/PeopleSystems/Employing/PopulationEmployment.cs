using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper that <see cref="EmploymentComponent"/> uses to define which populations and employment groups it used and how many
    /// </summary>
    [Serializable]
    public class PopulationEmployment
    {
        [Tooltip("used to group employments together and prioritize which ones get workers first")]
        public EmploymentGroup Group;
        [Tooltip("the population used to fill occupancies(plebs, academics, ...)")]
        public Population Population;
        [Tooltip("how many workers are needed by the employment to reach full productivity")]
        public int Needed;
        public int Available { get; private set; }

        public float Rate => Needed == 0 ? 1f : Available / (float)Needed;
        public bool IsFullyStaffed => Available >= Needed;

        /// <summary>
        /// assigns the quantity needed and returns the rest
        /// </summary>
        /// <param name="quantity"></param>
        /// <returns></returns>
        public int AssignAvailable(int quantity)
        {
            int assigned = Mathf.Min(quantity, Needed);
            Available = assigned;
            return quantity - assigned;
        }
    }
}