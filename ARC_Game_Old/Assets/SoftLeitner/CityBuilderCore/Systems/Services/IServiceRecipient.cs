namespace CityBuilderCore
{
    /// <summary>
    /// building components that have services that can be filled by a <see cref="RiskWalker"/>
    /// </summary>
    public interface IServiceRecipient : IBuildingComponent
    {
        /// <summary>
        /// whether the recipient handles a particular serviec<br/>
        /// for example a bar visualization should not be created on a building that does not even have the service that is visualized
        /// </summary>
        /// <param name="service">the service to check</param>
        /// <returns>true if the service is present in the recipient</returns>
        bool HasServiceValue(Service service);
        /// <summary>
        /// check how much service value is left in the recipient
        /// </summary>
        /// <param name="service">the service for which we want the value</param>
        /// <returns>value in percent of much is left before service access is lost</returns>
        float GetServiceValue(Service service);
        /// <summary>
        /// changes a services value in the recipient<br/>
        /// services have to be increased regularly or access will be lost
        /// </summary>
        /// <param name="service">the service that will have its value changed</param>
        /// <param name="amount">positive to refill the service, negative to bring down closer to losing access</param>
        void ModifyService(Service service, float amount);
    }
}