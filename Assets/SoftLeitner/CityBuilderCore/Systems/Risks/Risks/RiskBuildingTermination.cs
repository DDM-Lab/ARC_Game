using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// a risk that when executed terminates the building
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/risks">https://citybuilder.softleitner.com/manual/risks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_risk_building_termination.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Risks/" + nameof(RiskBuildingTermination))]
    public class RiskBuildingTermination : Risk
    {
        public override void Execute(IRiskRecipient risker)
        {
            base.Execute(risker);

            risker.Building.Terminate();
        }
    }
}