using FrameworkDesign;
using System;
using UnityEngine;

namespace ShootingEditor2D
{
    public class UIController : ShootingEditor2DController
    {
        private IStatSystem mStatSystem;
        private IGunSystem mGunSystem;
        private IPlayerModel mPlayerModel;
        //private IGunConfigModel mGunConfigModel;

        private int mMaxBulletCount;

        private void Awake()
        {
            mStatSystem = this.GetSystem<IStatSystem>();
            mPlayerModel = this.GetModel<IPlayerModel>();
            mGunSystem = this.GetSystem<IGunSystem>();
            //mGunConfigModel = this.GetModel<IGunConfigModel>();

            // 查询代码
            //var gunConfigItem = mGunConfigModel.GetItemByName(mGunSystem.CurGun.Name.Value);
            //mMaxBulletCount = gunConfigItem.BulletMaxCount;
            //mMaxBulletCount = new MaxBulletCountQuery(mGunSystem.CurGun.Name.Value).Do();

            this.RegisterEvent<OnCurGunChangedEvent>(e =>
            {
                mMaxBulletCount = this.SendQuery(new MaxBulletCountQuery(e.Name));
            }).UnRegisterWhenGameObjectDestroyed(gameObject);
            mMaxBulletCount = this.SendQuery(new MaxBulletCountQuery(mGunSystem.CurGun.Name.Value));
        }

        /// <summary>
        /// 自定义字体大小
        /// </summary>
        /// Lazy 委托，意思是懒加载，mLabelStyle.Value 第一次调用的时候才会执行后边笔者传进去的委托。
        private readonly Lazy<GUIStyle> mLabelStyle = new Lazy<GUIStyle>(() => new GUIStyle(GUI.skin.label)
        {
            fontSize = 40
        });

        private void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 300, 100), $"生命:{mPlayerModel.HP.Value}/3", mLabelStyle.Value);
            GUI.Label(new Rect(Screen.width - 10 - 300, 10, 300, 100), $"击杀数量:{mStatSystem.KillCount.Value}", mLabelStyle.Value);
            GUI.Label(new Rect(10, 60, 300, 100), $"子弹:{mGunSystem.CurGun.BulletCountInGun.Value}/{mGunSystem.CurGun.BulletCountOutGun.Value}", mLabelStyle.Value); 
            GUI.Label(new Rect(10, 110, 300, 100), $"枪械名字:{mGunSystem.CurGun.Name.Value}", mLabelStyle.Value); 
            GUI.Label(new Rect(10, 160, 300, 100), $"枪械状态:{mGunSystem.CurGun.GunState.Value}", mLabelStyle.Value); 
        }
        private void OnDestroy()
        {
            mStatSystem = null;
            mPlayerModel = null;
            mGunSystem = null;
        }
    }
}

