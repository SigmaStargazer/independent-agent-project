using Common;
using Cysharp.Threading.Tasks;
using GameFlow;

namespace ShootingEditor2D
{
    public class GameFlowManager : Singleton<GameFlowManager>
    {
        public async UniTask StartNewGame(string firstLevelName, string agentName, string agentDesc)
        {
            var flow = new NewGameFlow(firstLevelName, agentName, agentDesc);
            await FlowExecutor.Instance.Execute(flow);
        }

        public async UniTask ContinueGame()
        {
            var flow = new ContinueGameFlow();
            await FlowExecutor.Instance.Execute(flow);
        }

        public async UniTask ReturnToTitle(string titleSceneName)
        {
            var flow = new ReturnToTitleFlow(titleSceneName);
            await FlowExecutor.Instance.Execute(flow);
        }

        public async UniTask NextMap(string nextLevelName)
        {
            var flow = new NextMapFlow(nextLevelName);
            await FlowExecutor.Instance.Execute(flow);
        }
    }
}