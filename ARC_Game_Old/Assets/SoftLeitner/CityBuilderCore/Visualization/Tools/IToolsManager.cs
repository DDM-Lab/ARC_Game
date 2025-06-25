namespace CityBuilderCore
{
    /// <summary>
    /// manages the tool activation and tool cost
    /// </summary>
    public interface IToolsManager
    {
        /// <summary>
        /// the currently active tool or null when none
        /// </summary>
        BaseTool ActiveTool { get; }

        /// <summary>
        /// deactivate the currently active tool and activate the new one
        /// </summary>
        /// <param name="tool">the tool that will become active</param>
        void ActivateTool(BaseTool tool);
        /// <summary>
        /// deactivate the currently active tool if it is the one passed
        /// </summary>
        /// <param name="tool">the tool to deactivate</param>
        void DeactivateTool(BaseTool tool);
        /// <summary>
        /// checks the active tool for any item costs that should be shown as a previous<br/>
        /// for example building costs while the building tool is active
        /// </summary>
        /// <param name="item">the item for which to check cost</param>
        /// <returns>how many items are needed to perform the action of the current tool</returns>
        int GetCost(Item item);
    }
}