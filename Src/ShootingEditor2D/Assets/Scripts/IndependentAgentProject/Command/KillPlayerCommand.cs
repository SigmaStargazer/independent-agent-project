using FrameworkDesign;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndependentAgentProject
{
    internal class KillPlayerCommand : AbstractCommand
    {
        protected override void OnExecute()
        {
            var gameModel = this.GetModel<IGameModel>();
            gameModel.GameState.Value = GameStateEnum.GameOver;
            this.SendEvent<GameOverEvent>();
        }
    }
}
