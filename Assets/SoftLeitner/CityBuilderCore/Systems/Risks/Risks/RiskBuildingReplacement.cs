using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// a risk that when executed replaces its building with something else<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/risks">https://citybuilder.softleitner.com/manual/risks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_risk_building_replacement.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Risks/" + nameof(RiskBuildingReplacement))]
    public class RiskBuildingReplacement : Risk
    {
        [Tooltip("when the risk triggers the afflicted building will be replaced with this one")]
        public Building Replacement;

        public override void Execute(IRiskRecipient risker)
        {
            base.Execute(risker);

            risker.Building.Replace(Replacement.Info.GetPrefab(risker.Building.Index));
        }
    }
}