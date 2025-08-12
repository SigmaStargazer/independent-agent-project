using FrameworkDesign;

namespace ShootingEditor2D
{
    public class PickGunCommand : AbstractCommand
    {
        private readonly string mName;
        private readonly int mBulletInGun;
        private readonly int mBulletOutGun;

        // readonly的值只能再构造函数赋值，{ get{return xxx};}可以各种赋值
        public PickGunCommand(string mName, int mBulletInGun, int mBulletOutGun)
        {
            this.mName = mName;
            this.mBulletInGun = mBulletInGun;
            this.mBulletOutGun = mBulletOutGun;
        }

        protected override void OnExecute()
        {
            this.GetSystem<IGunSystem>().PickGun(mName, mBulletInGun, mBulletOutGun);
        }
    }
}
