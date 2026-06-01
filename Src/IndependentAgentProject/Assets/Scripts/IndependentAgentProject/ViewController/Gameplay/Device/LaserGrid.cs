using FrameworkDesign;
using IndependentAgentProject;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static IndependentAgentProject.CharaBase;

namespace IndependentAgentProject
{
    public class LaserGrid : DeviceBase, ITriggerable
    {
        public override string Name => "激光网";
        public override string Desc => "接触后会直接死亡";
        public override bool IsInteractable => false;

        [Header("激光配置")]
        [SerializeField]
        private GameObject mLaser;
        [SerializeField]
        [Tooltip("游戏开始时是否激活")]
        private bool mStartActive = true;
        public bool IsActive { get; private set; }
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
            if (mLaser == null)
                return;
            mLaser.SetActive(true);
        }
        public void OnActiveUpdate() { }
        public void OnActiveFixedUpdate() { }
        public void OnActiveExit() { }

        public void OnInactiveEnter() 
        {
            IsActive = false;
            if (mLaser == null)
                return;
            mLaser.SetActive(false);
        }
        public void OnInactiveUpdate() { }
        public void OnInactiveFixedUpdate() { }
        public void OnInactiveExit() { }
        #endregion

        #region FSM State
        public class ActiveState : FSMStateBase
        {
            public override string Name => "Active";

            public override void OnEnter(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGrid laserGrid)
                    laserGrid.OnActiveEnter();
            }
            public override void OnUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGrid laserGrid)
                    laserGrid.OnActiveUpdate();
            }
            public override void OnFixedUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGrid laserGrid)
                    laserGrid.OnActiveFixedUpdate();
            }

            public override void OnExit(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGrid laserGrid)
                    laserGrid.OnActiveExit();
            }
        }

        public class InactiveState : FSMStateBase
        {
            public override string Name => "Inactive";

            public override void OnEnter(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGrid laserGrid)
                    laserGrid.OnInactiveEnter();
            }
            public override void OnUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGrid laserGrid)
                    laserGrid.OnInactiveUpdate();
            }
            public override void OnFixedUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGrid laserGrid)
                    laserGrid.OnInactiveFixedUpdate();
            }

            public override void OnExit(SceneObjBase sceneObj)
            {
                if (sceneObj is LaserGrid laserGrid)
                    laserGrid.OnInactiveExit();
            }
        }
        #endregion

        public bool CanTrigger()
        {
            return true;
        }

        public void Trigger()
        {
            ChangeState(!IsActive ? "Active" : "Inactive");
        }
    }
}

