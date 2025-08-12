using FrameworkDesign;

namespace ShootingEditor2D
{
    public class ShootCommand : AbstractCommand
    {
        public static readonly ShootCommand Instance = new ShootCommand();
        protected override void OnExecute()
        {
            var gunSystem = this.GetSystem<IGunSystem>();
            var gunConfigItem = this.GetModel<IGunConfigModel>()
                .GetItemByName(gunSystem.CurGun.Name.Value);

            var timeSystem = this.GetSystem<ITimeSystem>();

            gunSystem.CurGun.BulletCountInGun.Value--;
            gunSystem.CurGun.GunState.Value = GunState.Shooting;
            
            timeSystem.AddDelayTask(1 / gunConfigItem.Frequency,
                () =>
                {
                    gunSystem.CurGun.GunState.Value = GunState.Idle;

                    if (gunSystem.CurGun.BulletCountInGun.Value == 0 &&
                        gunSystem.CurGun.BulletCountOutGun.Value > 0)
                    {
                        this.SendCommand<ReloadCommand>();
                    }
                });
        }
    }
}