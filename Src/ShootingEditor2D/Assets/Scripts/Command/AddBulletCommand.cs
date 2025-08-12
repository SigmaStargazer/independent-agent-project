using FrameworkDesign;

namespace ShootingEditor2D
{
    public class AddBulletCommand : AbstractCommand
    {
        protected override void OnExecute()
        {
            var gunSystem = this.GetSystem<IGunSystem>();
            AddBullet(gunSystem.CurGun);
            foreach(var gunInfo in gunSystem.GunInfos)
            {
                AddBullet(gunInfo);
            }
        }

        void AddBullet(GunInfo gunInfo)
        {
            var gunConfigItem = this.GetModel<IGunConfigModel>().GetItemByName(gunInfo.Name.Value);
            var maxBullet = this.SendQuery(new MaxBulletCountQuery(gunInfo.Name.Value));
            if(!gunConfigItem.NeedBullet)// 不需要子弹就是手枪
            {
                gunInfo.BulletCountInGun.Value = maxBullet;
            }
            else
            {
                gunInfo.BulletCountOutGun.Value += maxBullet;
            }
        }
    }
}
