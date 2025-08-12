using FrameworkDesign;

namespace ShootingEditor2D
{
    public class FullBulletCommand : AbstractCommand
    {
        protected override void OnExecute()
        {
            var gunSystem = this.GetSystem<IGunSystem>();
            var gunConfigModel = this.GetModel<IGunConfigModel>();
            gunSystem.CurGun.BulletCountInGun.Value = gunConfigModel.GetItemByName(gunSystem.CurGun.Name.Value).BulletMaxCount;
            foreach (var gunInfo in gunSystem.GunInfos)
            {
                gunInfo.BulletCountInGun.Value = gunConfigModel.GetItemByName(gunInfo.Name.Value).BulletMaxCount;
            }
        }
    }
}
