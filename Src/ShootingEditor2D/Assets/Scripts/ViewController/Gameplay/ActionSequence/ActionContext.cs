using System;

namespace ShootingEditor2D
{
    public class ActionContext
    {
        public string ActionName;
        public Func<bool> EndCondition;

        // 通用计时
        public float ActionTime;
    }

}