namespace CityBuilderCore
{
    /// <summary>
    /// building components that have risks that can be reduced by a <see cref="RiskWalker"/> or are otherwise executed
    /// </summary>
    public interface IRiskRecipient : IBuildingComponent
    {
        /// <summary>
        /// whether the recipient handles a particular risk
        /// </summary>
        /// <param name="risk">the risk to check</param>
        /// <returns>true if the passed risk is present in the recipient</returns>
        bool HasRiskValue(Risk risk);
        /// <summary>
        /// checks how far a risk in the recipient has progressed
        /// </summary>
        /// <param name="risk">the risk for which we want the value</param>
        /// <returns>value in percent of how close the risk is to executing</returns>
        float GetRiskValue(Risk risk);
        /// <summary>
        /// changes a risks value in the recipient<br/>
        /// risks have to be reduced regularly or they will execute
        /// </summary>
        /// <param name="risk">the risk that will have its value changed</param>
        /// <param name="amount">positive value will move the risk closer to executing, negative value prevent it from happening</param>
        void ModifyRisk(Risk risk, float amount);
    }
}