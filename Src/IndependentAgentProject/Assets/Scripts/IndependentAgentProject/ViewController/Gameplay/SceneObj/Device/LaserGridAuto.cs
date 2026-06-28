using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 自动开关的激光网。
    /// 与 LaserGrid 类似（激活时碰到玩家直接死亡），但不需要被 Lever 等外部装置触发，
    /// 而是按可配置的「激活时长」「关闭时长」自动循环开关。
    /// 实现方式参考 MovingPlatformAuto：使用 FSM 的 OnXxxUpdate 钩子计时，
    /// 不使用协程；状态切换通过 ChangeState 完成。
    /// </summary>
    public class LaserGridAuto : DeviceBase
    {
        public override string Name => "自动开关的激光网";
        public override string Desc => "似乎会按某种节奏自行开启与关闭，接触激光会直接死亡。";
        public override bool IsInteractable => false;

        [Header("激光配置")]
        [SerializeField]
        private GameObject mLaser;
        [SerializeField]
        [Tooltip("游戏开始时是否激活")]
        private bool mStartActive = true;

        [Header("自动切换配置")]
        [SerializeField]
        [Tooltip("激光保持激活的时长（秒）")]
        private float mActiveDuration = 2f;
        [SerializeField]
        [Tooltip("激光保持关闭的时长（秒）")]
        private float mInactiveDuration = 2f;

        public bool IsActive { get; private set; }

        private float mTimer = 0f;

        protected override void Awake()
        {
            base.Awake();
            RegisterState(new ActiveState());
            RegisterState(new InactiveState());
            if (mLaser != null)
                mLaser.SetActive(mStartActive);
        }
        protected override void Start()
        {
            base.Start();
            ChangeState(mStartActive ? "Active" : "Inactive");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying && mLaser != null)
            {
                mLaser.SetActive(mStartActive);
            }
        }
#endif

        #region FSM Hook
        public void OnActiveEnter()
        {
            IsActive = true;
            mTimer = 0f;
            if (mLaser == null)
                return;
            mLaser.SetActive(true);
        }
        public void OnActiveUpdate()
        {
            mTimer += Time.deltaTime;
            if (mActiveDuration > 0f && mTimer >= mActiveDuration)
            {
                ChangeState("Inactive");
            }
        }
        public void OnActiveFixedUpdate() { }
        public void OnActiveExit() { }

        public void OnInactiveEnter()
        {
            IsActive = false;
            mTimer = 0f;
            if (mLaser == null)
                return;
            mLaser.SetActive(false);
        }
        public void OnInactiveUpdate()
        {
            mTimer += Time.deltaTime;
            if (mInactiveDuration > 0f && mTimer >= mInactiveDuration)
            {
                ChangeState("Active");
            }
        }
        public void OnInactiveFixedUpdate() { }
        public void OnInactiveExit() { }
        #endregion

        #region FSM State
        public class ActiveState : FSMStateBase
        {
            public override string Name => "Active";

            public override void OnEnter(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGridAuto laserGrid)
                    laserGrid.OnActiveEnter();
            }
            public override void OnUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGridAuto laserGrid)
                    laserGrid.OnActiveUpdate();
            }
            public override void OnFixedUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGridAuto laserGrid)
                    laserGrid.OnActiveFixedUpdate();
            }

            public override void OnExit(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGridAuto laserGrid)
                    laserGrid.OnActiveExit();
            }
        }

        public class InactiveState : FSMStateBase
        {
            public override string Name => "Inactive";

            public override void OnEnter(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGridAuto laserGrid)
                    laserGrid.OnInactiveEnter();
            }
            public override void OnUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGridAuto laserGrid)
                    laserGrid.OnInactiveUpdate();
            }
            public override void OnFixedUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGridAuto laserGrid)
                    laserGrid.OnInactiveFixedUpdate();
            }

            public override void OnExit(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGridAuto laserGrid)
                    laserGrid.OnInactiveExit();
            }
        }
        #endregion
    }
}
