using CityBuilderCore;

namespace CityBuilderTown
{
    /// <summary>
    /// just puts the walker in an animation that shows it is working<br/>
    /// in combination with <see cref="TownWorkTask"/> and <see cref="TownWorkComponent"/> this can drive a buildings efficiency<br/>
    /// (for example in the Woodcutter which has a <see cref="TownProductionComponent"/> that uses that efficiency)
    /// </summary>
    public class TownWorkAction : AnimatedActionBase
    {
        public TownWorkAction() : base() { }
        public TownWorkAction(string parameter) : base(parameter) { }
        public TownWorkAction(int parameter) : base(parameter) { }

        public override void Start(Walker walker)
        {
            walker.OnMessage("WORK");

            base.Start(walker);
        }

        public override void Cancel(Walker walker)
        {
            base.Cancel(walker);

            walker.AdvanceProcess();
        }
    }
}