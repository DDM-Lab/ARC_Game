using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// container that distributes employees within an <see cref="EmploymentGroup"/> and <see cref="CityBuilderCore.Population"/>
    /// </summary>
    public class EmploymentGroupPopulation
    {
        public bool IsEmply => _employments.Count == 0;

        public Population Population { get; private set; }
        public EmploymentGroup Group { get; private set; }

        public int WorkersAvailable { get; private set; }
        public int WorkersNeeded { get; private set; }

        public int Delta => WorkersAvailable - WorkersNeeded;
        public float EmploymentRate => WorkersAvailable == 0 ? 0f : Mathf.Min(100f, WorkersNeeded / (float)WorkersAvailable);
        public float UnemploymentRate => 100f - EmploymentRate;

        private List<IEmployment> _employments = new List<IEmployment>();

        public EmploymentGroupPopulation(Population population, EmploymentGroup group)
        {
            Population = population;
            Group = group;
        }

        public void Add(IEmployment employment)
        {
            _employments.Add(employment);
        }

        public void Remove(IEmployment employment)
        {
            _employments.Remove(employment);
        }

        public void CalculateNeeded()
        {
            WorkersNeeded = _employments.Sum(e => e.GetNeeded(Group, Population));
        }

        public int Distribute(int quantity)
        {
            WorkersAvailable = Mathf.Min(WorkersNeeded, quantity);

            foreach (var employment in _employments)
            {
                quantity = employment.AssignAvailable(Group, Population, quantity);
            }

            return quantity;
        }
    }
}