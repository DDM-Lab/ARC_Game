
// You can access enums globally by using this Singleton class in any script.
// ex. GlobalEnums.GamePhase currentPhase = GlobalEnums.GamePhase.Start;
public static class GlobalEnums
{
    public enum GamePhase
    {
        Start,
        Construction,
        WorkerAssignment,
        Simulation,
        EmergencyTasks,
        PlayerTurn,
        DisasterEvents,
        End,
        GameComplete
    }

    public enum DisasterType
    {
        Flood
    }

    public enum BuildingType
    {
        Community,
        Motel,
        Shelter,
        CaseworkSite,
        Kitchen
    }

    public enum WeatherType
    {
        Sunny,
        Rainy,
        Stormy
    }
}
