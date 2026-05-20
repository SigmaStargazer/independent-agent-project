namespace ShootingEditor2D
{
    public static class SceneInfo
    {
        public static SceneData Current { get; private set; }
        public static void SetCurrent(SceneData data)
        {
            Current = data;
        }
    }
}