using System.Collections.Generic;

namespace CityBuilderCore
{
    /// <summary>
    /// a building component that uses employees
    /// </summary>
    public interface IEmployment : IBuildingComponent
    {
        /// <summary>
        /// all needed employees are assigned
        /// </summary>
        bool IsFullyStaffed { get; }
        /// <summary>
        /// the rate of employees assigned from 0 to 1
        /// </summary>
        float EmploymentRate { get; }

        /// <summary>
        /// returns which employment groups the employer needs
        /// </summary>
        /// <returns>the employment groups used by this component</returns>
        IEnumerable<EmploymentGroup> GetEmploymentGroups();
        /// <summary>
        /// returns which populations this component uses
        /// </summary>
        /// <returns>the populations this component uses</returns>
        IEnumerable<Population> GetPopulations();

        /// <summary>
        /// check how many workers of a population the component needs for a certain employment group
        /// </summary>
        /// <param name="employmentGroup">the group to check</param>
        /// <param name="population">the relevant population</param>
        /// <returns>how many workers are needed in total</returns>
        int GetNeeded(EmploymentGroup employmentGroup, Population population);
        /// <summary>
        /// assigns the quantity needed and returns the rest
        /// </summary>
        /// <param name="employmentGroup"></param>
        /// <param name="population"></param>
        /// <param name="quantity"></param>
        /// <returns>workers count remaining</returns>
        int AssignAvailable(EmploymentGroup employmentGroup, Population population, int quantity);
    }
}