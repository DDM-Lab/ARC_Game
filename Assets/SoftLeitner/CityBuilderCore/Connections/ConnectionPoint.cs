namespace CityBuilderCore
{
    /// <summary>
    /// helper used by <see cref="ConnectionGrid"/> to hold the value and passer of a point in the connection grid
    /// </summary>
    public class ConnectionPoint
    {
        public IConnectionPasser Passer { get; }
        public int Value { get; set; }

        public ConnectionPoint(IConnectionPasser passer)
        {
            Passer = passer;
            Value = -1;
        }
    }
}
