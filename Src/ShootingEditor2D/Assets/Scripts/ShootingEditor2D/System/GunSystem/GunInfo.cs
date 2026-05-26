using FrameworkDesign;

namespace ShootingEditor2D
{
    public enum GunState
    {
        Idle,
        Shooting,
        Reload,
        EmptyBullet,
        CoolDown
    }
    public class GunInfo
    {
        //[Obsolete("请使用BulletCountInGame", true)]
        ////Obsolete特性：表示某个东西被弃用，在编译时提示
        ////false为提示warning。true为提示error
        //public BindableProperty<int> BulletCount
        //{
        //    get => BulletCountInGun;
        //    set => BulletCountInGun = value;
        //}
        public BindableProperty<string> Name;
        public BindableProperty<GunState> GunState;
        public BindableProperty<int> BulletCountInGun;
        public BindableProperty<int> BulletCountOutGun;
    }
}
