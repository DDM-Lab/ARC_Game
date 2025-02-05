using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that uses employees to influence the buildings efficiency<br/>
    /// for example in THREE most buildings employ workers<br/>
    /// how fast mines and farms create items and whether storage works is bound to employment
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_employment_component.html")]
    public class EmploymentComponent : BuildingComponent, IEmployment, IEfficiencyFactor
    {
        public override string Key => "EMP";

        [Tooltip("determines which populations and employment groups this component needs and how many of them")]
        public PopulationEmployment[] PopulationEmployments;

        public BoolEvent IsFullyStaffedChanged;

        public bool IsFullyStaffed => Dependencies.Get<IGameSettings>().HasEmployment ? PopulationEmployments.All(p => p.IsFullyStaffed) : true;
        public float EmploymentRate => Dependencies.Get<IGameSettings>().HasEmployment ? PopulationEmployments.Select(p => p.Rate).Aggregate((a, b) => a * b) : 1f;

        public bool IsWorking => IsFullyStaffed;
        public float Factor => EmploymentRate;

        private void Start()
        {
            Dependencies.Get<IEmploymentManager>().AddEmployment(this);
        }
        private void OnDestroy()
        {
            Dependencies.Get<IEmploymentManager>().RemoveEmployment(this);
        }

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            IsFullyStaffedChanged?.Invoke(IsFullyStaffed);
        }

        public int GetNeeded(EmploymentGroup employmentGroup, Population population)
        {
            if(Building.IsSuspended) 
                return 0;
            return PopulationEmployments.Where(e => e.Group == employmentGroup && e.Population == population).Sum(e => e.Needed);
        }
        public virtual int AssignAvailable(EmploymentGroup employmentGroup, Population population, int quantity)
        {
            var fullyStaffed = IsFullyStaffed;
            var rest = quantity;
            if (Building.IsSuspended)
                PopulationEmployments.First(e => e.Group == employmentGroup && e.Population == population).AssignAvailable(0);
            else
                rest = PopulationEmployments.First(e => e.Group == employmentGroup && e.Population == population).AssignAvailable(quantity);
            if (fullyStaffed != IsFullyStaffed)
                IsFullyStaffedChanged?.Invoke(!fullyStaffed);
            return rest;
        }

        public IEnumerable<EmploymentGroup> GetEmploymentGroups() => PopulationEmployments.Select(p => p.Group);
        public IEnumerable<Population> GetPopulations() => PopulationEmployments.Select(p => p.Population);

        public override string GetDebugText() => $"EMP: {PopulationEmployments.Sum(p => p.Available)}/{PopulationEmployments.Sum(p => p.Needed)}";
        public override string GetDescription()
        {
            StringBuilder sb = new StringBuilder();

            foreach (var population in PopulationEmployments)
            {
                sb.AppendLine($"{population.Population.Name}: {population.Available}/{population.Needed}");
            }

            return sb.ToString();
        }
    }
}