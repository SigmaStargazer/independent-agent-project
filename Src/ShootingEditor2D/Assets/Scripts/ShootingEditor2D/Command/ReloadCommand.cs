using FrameworkDesign;

namespace ShootingEditor2D
{
    public class ReloadCommand : AbstractCommand
    {
        protected override void OnExecute()
        {
            var curGun = this.GetSystem<IGunSystem>().CurGun;
            var gunConfigItem = this.GetModel<IGunConfigModel>().GetItemByName(curGun.Name.Value);
            // 需要的子弹数量
            var needBulletCount = gunConfigItem.BulletMaxCount - curGun.BulletCountInGun.Value;

            if (needBulletCount > 0)
            {
                if(curGun.BulletCountOutGun.Value > 0)
                {
                    // 状态切装弹
                    curGun.GunState.Value = GunState.Reload;
                    // 状态切待机
                    this.GetSystem<ITimeSystem>().AddDelayTask(gunConfigItem.ReloadSeconds, () =>
                    {
                        // 如果枪外子弹很充足
                        if (curGun.BulletCountOutGun.Value > needBulletCount)
                        {
                            curGun.BulletCountOutGun.Value -= needBulletCount;
                            curGun.BulletCountInGun.Value += needBulletCount;
                        }
                        // 如果不充足，就全部填弹
                        else
                        {
                            curGun.BulletCountInGun.Value += curGun.BulletCountOutGun.Value; ;
                            curGun.BulletCountOutGun.Value = 0;
                        }
                        curGun.GunState.Value = GunState.Idle;
                    });
                }
            }
        }
    }
}