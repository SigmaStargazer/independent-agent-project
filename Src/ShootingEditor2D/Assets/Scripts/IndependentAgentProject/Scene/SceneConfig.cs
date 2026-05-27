using UnityEngine;

namespace IndependentAgentProject
{
    public class SceneConfig : MonoBehaviour
    {
        [Header("³¡¾°Ãû³Æ")]
        public string SceneDisplayName;
        [TextArea]
        public string Description;

        private void Awake()
        {
            SceneInfo.SetCurrent(new SceneData()
            {
                DisplayName = SceneDisplayName,
                Description = Description,
            });
        }
    }
}