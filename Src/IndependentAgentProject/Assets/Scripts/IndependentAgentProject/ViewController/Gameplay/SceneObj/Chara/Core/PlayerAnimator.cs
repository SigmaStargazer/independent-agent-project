using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// Player 专属动画组件，继承自 <see cref="SceneObjAnimator"/>。
    /// 在基类（FSM 状态动画驱动）基础上，增加"交互动画标签 -> 上层 Animator 状态名"映射，
    /// 供 Player 侧根据 <see cref="InteractAnimTag"/> 播放一次性动作动画。
    /// <para>
    /// Device 挂 <see cref="SceneObjAnimator"/>（基类），不需要本组件。
    /// </para>
    /// </summary>
    public class PlayerAnimator : SceneObjAnimator
    {
        [Tooltip("交互动画标签 -> Animator 状态名映射。上层 Action Layer 播放对应的一次性动画。")]
        [SerializeField] private List<ActionAnimMapping> _actionMappings = new();

        private readonly Dictionary<InteractAnimTag, ActionAnimMapping> _actionMap = new();

        [Serializable]
        public class ActionAnimMapping
        {
            public InteractAnimTag tag;
            [Tooltip("上层 Animator 状态名")]
            public string animState;
            public bool crossFade = true;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _actionMap.Clear();
            if (_actionMappings != null)
            {
                foreach (var m in _actionMappings)
                    if (m != null && m.tag != InteractAnimTag.None)
                        _actionMap[m.tag] = m;
            }
        }

        protected override void Start()
        {
            base.Start();

            // v0.22.18：Action Layer 配置自检（权重/默认状态/索引越界），配错时打警告
            if (_actionMappings != null && _actionMappings.Count > 0)
                CheckActionLayerConfig();
        }

        /// <summary>
        /// 按交互标签播放上层一次性动画。
        /// 标签为 <see cref="InteractAnimTag.None"/> 或未配置映射则不播。
        /// 播不播完全由 tag 决定，与交互成功与否无关。
        /// </summary>
        public void PlayOneShotByTag(InteractAnimTag tag)
        {
            if (tag == InteractAnimTag.None) return;
            if (!_actionMap.TryGetValue(tag, out var m)) return;
            PlayOneShot(m.animState, m.crossFade);
        }
    }
}
