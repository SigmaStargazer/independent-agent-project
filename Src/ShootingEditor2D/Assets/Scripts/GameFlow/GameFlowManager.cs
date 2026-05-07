using Common;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class GameFlowManager : Singleton<GameFlowManager>
    {
        public GameFlowManager() { }

        public void StartNewGame(string firstLevelName, string agentName, string agentDesc) 
        {
            var flow = new NewGameFlow(firstLevelName, agentName, agentDesc);
            flow.Start();
        }

        public void ContinueGame()
        {
            var flow = new ContinueGameFlow();
            flow.Start();
        }
    }
}
