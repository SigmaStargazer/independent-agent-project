using FrameworkDesign;

namespace IndependentAgentProject
{
    public enum GameStateEnum
    {
        Playing,
        Paused,
        GameOver,
        Victory
    }
    public interface IGameModel : IModel
    {
        BindableProperty<GameStateEnum> GameState { get; }
    }

    public class GameModel : AbstractModel, IGameModel
    {
        public BindableProperty<GameStateEnum> GameState { get; } = new BindableProperty<GameStateEnum>()
        {
            Value = GameStateEnum.Playing
        };

        protected override void OnInit()
        {

        }
    }
}