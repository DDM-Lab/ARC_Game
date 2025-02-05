using System.Collections.Generic;
using System.Linq;

namespace CityBuilderCore
{
    /// <summary>
    /// container that distributes employees within a <see cref="CityBuilderCore.Population"/>
    /// </summary>
    public class EmploymentPopulation
    {
        public bool IsEmpty => _groups.Count == 0;

        public Population Population { get; private set; }

        public int WorkersAvailable { get; private set; }
        public int WorkersNeeded { get; private set; }

        public int Delta => WorkersAvailable - WorkersNeeded;
        public float WorkerRate => WorkersNeeded == 0 ? 0f : (float)WorkersAvailable / WorkersNeeded;
        public float EmploymentRate => WorkersAvailable == 0 ? 0f : (float)WorkersNeeded/ WorkersAvailable;

        private Dictionary<EmploymentGroup, EmploymentGroupPopulation> _groups = new Dictionary<EmploymentGroup, EmploymentGroupPopulation>();

        public EmploymentPopulation(Population population)
        {
            Population = population;
        }

        public void Add(IEmployment employment)
        {
            foreach (var group in employment.GetEmploymentGroups())
            {
                if (!_groups.ContainsKey(group))
                    _groups.Add(group, new EmploymentGroupPopulation(Population, group));
                _groups[group].Add(employment);
            }
        }

        public void Remove(IEmployment employment)
        {
            foreach (var group in employment.GetEmploymentGroups())
            {
                if (!_groups.ContainsKey(group))
                    continue;
                _groups[group].Remove(employment);
                if (_groups[group].IsEmply)
                    _groups.Remove(group);
            }
        }

        public void CalculateNeeded()
        {
            _groups.Values.ForEach(g => g.CalculateNeeded());

            WorkersNeeded = _groups.Values.Sum(g => g.WorkersNeeded);
        }

        public int Distribute(int quantity, Dictionary<string, int> priorities)
        {
            WorkersAvailable = quantity;

            foreach (var group in _groups.Keys.OrderByDescending(g => priorities[g.Key]))
            {
                quantity = _groups[group].Distribute(quantity);
            }

            return quantity;
        }

        public int GetNeeded(EmploymentGroup group)
        {
            if (!_groups.ContainsKey(group))
                return 0;
            return _groups[group].WorkersNeeded;
        }
        public int GetAvailable(EmploymentGroup group)
        {
            if (!_groups.ContainsKey(group))
                return 0;
            return _groups[group].WorkersAvailable;
        }
    }
}