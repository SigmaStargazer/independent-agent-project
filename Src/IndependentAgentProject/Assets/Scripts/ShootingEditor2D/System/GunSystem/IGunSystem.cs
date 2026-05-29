using FrameworkDesign;
using System.Collections.Generic;
using System.Linq;

namespace ShootingEditor2D
{
    public interface IGunSystem : ISystem
    {
        GunInfo CurGun { get; }
        Queue<GunInfo> GunInfos { get; }

        void PickGun(string name, int bulletCountInGun, int bulletCountOutGun);
        void ShiftGun();
    }

    public class OnCurGunChangedEvent
    {
        public string Name { get; set; }
    }

    public class GunSystem : AbstractSystem, IGunSystem
    {
        protected override void OnInit()
        {
            
        }

        private Queue<GunInfo> mGunInfos = new Queue<GunInfo>();
        public Queue<GunInfo> GunInfos { get { return mGunInfos; } }

        public GunInfo CurGun { get; } = new GunInfo()
        {
            // 初始值
            Name = new BindableProperty<string>()
            {
                Value = "手枪"
            },
            GunState = new BindableProperty<GunState>()
            {
                Value = GunState.Idle
            },
            BulletCountInGun = new BindableProperty<int>()
            {
                Value = 3
            },
            BulletCountOutGun = new BindableProperty<int>()
            {
                Value = 7
            }
        };

        // 当前枪入队,并换新枪
        void EnqueueCurGun(string nextGunName, int nextBulletInGun, int nextBulletOutGun)
        {
            // 复制当前的枪械信息
            var curGunInfo = new GunInfo
            {
                Name = new BindableProperty<string>
                {
                    Value = CurGun.Name.Value
                },
                GunState = new BindableProperty<GunState>
                {
                    Value = CurGun.GunState.Value
                },
                BulletCountInGun = new BindableProperty<int>
                {
                    Value = CurGun.BulletCountInGun.Value
                },
                BulletCountOutGun = new BindableProperty<int>
                {
                    Value = CurGun.BulletCountOutGun.Value
                }
            };
            // 入队
            mGunInfos.Enqueue(curGunInfo);

            // 新枪设置为当前枪
            CurGun.Name.Value = nextGunName;
            CurGun.GunState.Value = GunState.Idle;
            CurGun.BulletCountInGun.Value = nextBulletInGun;
            CurGun.BulletCountOutGun.Value = nextBulletOutGun;

            // 发送换枪事件
            this.SendEvent(new OnCurGunChangedEvent()
            {
                Name = nextGunName
            });
        }

        public void PickGun(string name, int bulletCountInGun, int bulletCountOutGun)
        {
            // 当前枪是同类型
            if (CurGun.Name.Value == name)
            {
                CurGun.BulletCountInGun.Value += bulletCountInGun;
                CurGun.BulletCountOutGun.Value += bulletCountOutGun;
            }
            // 已经拥有这把枪了
            else if (mGunInfos.Any(info => info.Name.Value == name))
            {
                var gunInfo = mGunInfos.First(info => info.Name.Value == name);
                gunInfo.BulletCountInGun.Value += bulletCountInGun;
                gunInfo.BulletCountOutGun.Value += bulletCountOutGun;
            }
            else
            {
                EnqueueCurGun(name, bulletCountInGun, bulletCountOutGun);
            }
        }
        public void ShiftGun()
        {
            if (mGunInfos.Count > 0)
            {
                var nextGunInfo = mGunInfos.Dequeue();
                EnqueueCurGun(nextGunInfo.Name.Value, nextGunInfo.BulletCountInGun.Value, nextGunInfo.BulletCountOutGun.Value);
            }

        }
    }
}
