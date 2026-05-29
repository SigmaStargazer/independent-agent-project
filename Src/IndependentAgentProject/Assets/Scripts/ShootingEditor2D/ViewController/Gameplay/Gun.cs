using FrameworkDesign;
using UnityEngine;

namespace ShootingEditor2D
{
    public class Gun : ShootingEditor2DController
    {
        private Bullet mBullet;

        private GunInfo mGunInfo;

        private int mMaxBulletCount;

        private void Awake()
        {
            mBullet = transform.Find("Bullet").GetComponent<Bullet>();

            this.RegisterEvent<OnCurGunChangedEvent>(e =>
            {
                mMaxBulletCount = this.SendQuery(new MaxBulletCountQuery(e.Name));
                mGunInfo = this.GetSystem<IGunSystem>().CurGun;
            }).UnRegisterWhenGameObjectDestroyed(gameObject);

            mGunInfo = this.GetSystem<IGunSystem>().CurGun;
            mMaxBulletCount = this.SendQuery(new MaxBulletCountQuery(mGunInfo.Name.Value)); 
        }

        public void Shoot()
        {
            if (mGunInfo.BulletCountInGun.Value > 0 && mGunInfo.GunState.Value == GunState.Idle)
            {
                var bullet = Instantiate(mBullet.transform, mBullet.transform.position, mBullet.transform.rotation);
                // 统一缩放值
                bullet.transform.localScale = mBullet.transform.lossyScale;
                bullet.gameObject.SetActive(true);

                this.SendCommand(ShootCommand.Instance);
            }
        }

        public void Reload()
        {
            if (mGunInfo.BulletCountInGun.Value < mMaxBulletCount &&
                mGunInfo.BulletCountOutGun.Value > 0 &&
                mGunInfo.GunState.Value == GunState.Idle)
            {
                this.SendCommand<ReloadCommand>();
            }
        }

        private void OnDestroy()
        {
            mGunInfo = null;
        }

    }
}