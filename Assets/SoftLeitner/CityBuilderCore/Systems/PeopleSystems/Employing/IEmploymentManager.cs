namespace CityBuilderCore
{
    /// <summary>
    /// manages the employment system
    /// <para>
    /// distributes the different populations to their appropriate workplaces<br/>
    /// when employees are missing, employees are assigned by employment group priority<br/>
    /// this priority can be changed at runtime
    /// </para>
    /// </summary>
    public interface IEmploymentManager
    {
        /// <summary>
        /// registers an employment with the manager so that it can now be considered when distributing available employees<br/>
        /// typically called in Start
        /// </summary>
        /// <param name="employment">the employment to add to the manager</param>
        void AddEmployment(IEmployment employment);
        /// <summary>
        /// removes an employment that was previously added to the manager, the employment will no longer have any employees assigned<br/>
        /// typically called in OnDestroy
        /// </summary>
        /// <param name="employment">the employment that will no longer be managed here</param>
        void RemoveEmployment(IEmployment employment);

        /// <summary>
        /// forces immediate redistribution of employees<br/>
        /// ususally the manager does this itself, this is called after loading so there is no wait time
        /// </summary>
        void CheckEmployment();

        /// <summary>
        /// checks how many employees in a population are available for work
        /// </summary>
        /// <param name="population">the populatio to check</param>
        /// <param name="group">the group, null for all groups</param>
        /// <returns>employee count available for work</returns>
        int GetAvailable(Population population, EmploymentGroup group = null);
        /// <summary>
        /// checks how many employees in a population are currently employed
        /// </summary>
        /// <param name="population">the populatio to check</param>
        /// <param name="group">the group, null for all groups</param>
        /// <returns>employee count currently employed</returns>
        int GetEmployed(Population population, EmploymentGroup group = null);
        /// <summary>
        /// checks how many employees in a population are currently needed
        /// </summary>
        /// <param name="population">the populatio to check</param>
        /// <param name="group">the group, null for all groups</param>
        /// <returns>employee count currently needed</returns>
        int GetNeeded(Population population, EmploymentGroup group = null);
        /// <summary>
        /// calculates employment rate for a population(needed/available)
        /// 100 jobs for 200 employees > 0.5<br/>
        /// 300 jobs for 100 employees > 3.0
        /// </summary>
        /// <param name="population">the populatio to check</param>
        /// <returns>employment rate(below 1 when more people than jobs)</returns>
        float GetEmploymentRate(Population population);
        /// <summary>
        /// calculates worker rate for population(available/needed)
        /// 100 jobs for 200 employees > 2.0<br/>
        /// 300 jobs for 150 employees > 0.5
        /// </summary>
        /// <param name="population">the populatio to check</param>
        /// <returns>worker rate(below 1 when more jobs than people)</returns>
        float GetWorkerRate(Population population);

        /// <summary>
        /// checks the current priority of an employment group<br/>
        /// the priority determines in which order employees are assigned
        /// </summary>
        /// <param name="group">the group to check</param>
        /// <returns>current priority</returns>
        int GetPriority(EmploymentGroup group);
        /// <summary>
        /// changes the priority of an employment group<br/>
        /// this changes which employment groups employees are distributed to first
        /// </summary>
        /// <param name="group"></param>
        /// <param name="priority"></param>
        void SetPriority(EmploymentGroup group, int priority);

        string SaveData();
        void LoadData(string json);
    }
}