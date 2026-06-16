using UnityEngine;

namespace IndependentAgentProject
{
    public class SceneConfig : MonoBehaviour
    {
        [Header("场景名称")]
        public string SceneDisplayName;
        [Header("场景描述")]
        [TextArea]
        public string Description;
        [Header("场景类型")]
        public SceneType SceneType = SceneType.Level;


        private void Awake()
        {
            SceneInfo.SetCurrent(new SceneData()
            {
                DisplayName = SceneDisplayName,
                Description = Description,
                SceneType = SceneType
            });
        }
    }
}
