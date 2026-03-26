using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    /// <summary>
    /// 是否能点击接口。需要被 MouseClickInteractor2D 触发
    /// 注意：被点击的物体必须有碰撞体，根据IsTrigger的状态决定是否能产生碰撞事件
    /// </summary>
    public interface IClickable
    {
        bool IsClickable { get; }   // 是否允许点击
        void OnClick();          // 点击行为
    }
}
