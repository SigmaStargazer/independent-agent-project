namespace IndependentAgentProject
{
    public static class SceneInfo
    {
        public static SceneData Current { get; private set; }
        public static bool IsTraining => Current?.SceneType == SceneType.Training;
        public static bool IsLevel => Current?.SceneType == SceneType.Level;
        public static void SetCurrent(SceneData data)
        {
            Current = data;
        }
    }
}
