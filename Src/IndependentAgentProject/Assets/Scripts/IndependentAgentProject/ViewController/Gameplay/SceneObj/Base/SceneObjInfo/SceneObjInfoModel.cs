namespace IndependentAgentProject
{
    public class SceneObjInfoModel
    {
        public string Name;
        public string Desc;
        public string State;

        public string FaceDirection;

        // 普通模式
        public string Direction;
        public float Distance;

        // 范围模式
        public bool IsRangeDirection;

        public string RangeLeftDirection;
        public float RangeLeftDistance;

        public string RangeRightDirection;
        public float RangeRightDistance;

        public float SpeedX;
        public string SpeedDirX;

        public float SpeedY;
        public string SpeedDirY;
    }
}
