namespace IndependentAgentProject
{
    public enum SceneType
    {
        Level,
        Training
    }

    public class SceneData
    {
        public string DisplayName;
        public string Description;
        public SceneType SceneType;
    }
}
