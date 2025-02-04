namespace CityBuilderCore
{
    public interface IMissionManager
    {
        /// <summary>
        /// whether the mission has been finished in this playthrough<br/>
        /// if the mission has been finished ever can be checked in <see cref="Mission.GetFinished(Difficulty)"/>
        /// </summary>
        public bool IsFinished { get; }

        /// <summary>
        /// gets mission, difficulty, new/continue of the active mission
        /// </summary>
        MissionParameters MissionParameters { get; }

        /// <summary>
        /// sets mission, difficulty, new/continue<br/>
        /// triggers mission initialization like loading for continue
        /// </summary>
        /// <param name="missionParameters"></param>
        void SetMissionParameters(MissionParameters missionParameters);
    }
}