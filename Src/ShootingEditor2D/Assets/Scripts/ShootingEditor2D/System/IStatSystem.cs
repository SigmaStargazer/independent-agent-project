using FrameworkDesign;

namespace ShootingEditor2D
{
    /// <summary>
    /// 统计系统
    /// </summary>
    public interface IStatSystem : ISystem
    {
        BindableProperty<int> KillCount { get; }
    }

    public class StatSystem : AbstractSystem, IStatSystem
    {
        protected override void OnInit()
        {

        }
        public BindableProperty<int> KillCount { get; } = new BindableProperty<int>()
        {
            Value = 0,
        };
    }
}

