using FrameworkDesign;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class IndependentAgentProject : Architecture<IndependentAgentProject>
    {
        protected override void Init()
        {
            RegisterModel<IGameModel>(new GameModel());
            RegisterModel<IGameSettingsModel>(new GameSettingsModel());
            RegisterModel<IApiConfigModel>(new ApiConfigModel());
        }
    }

}
