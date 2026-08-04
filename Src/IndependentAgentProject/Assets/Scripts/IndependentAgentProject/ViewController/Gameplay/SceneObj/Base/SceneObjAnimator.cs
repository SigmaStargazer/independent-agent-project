using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 订阅 <see cref="SceneObjBase.OnStateChanged"/>，把 FSM 状态切换翻译成 Animator 动画播放。
    /// 只读状态、只驱动 Animator，不参与任何 FSM 逻辑。
    /// Chara 与 Device 共用，因为都走 <see cref="SceneObjBase.ChangeState"/>。
    /// <para>
    /// Animator Controller 约定：只建孤立 State（不画 Transition、不建切换参数），
    /// 组件用 <see cref="Animator.CrossFade"/> / <see cref="Animator.Play"/> 按状态名直接跳转。
    /// </para>
    /// </summary>
    public class SceneObjAnimator : MonoBehaviour
    {
        [SerializeField] private SceneObjBase _target;
        [SerializeField] private Animator _animator;
        [SerializeField] private float _crossFadeDuration = 0.1f;

        [Tooltip("未在映射表逐项配置时的默认过渡方式。逐帧 Sprite 动画建议 false（瞬切，避免重影）。")]
        [SerializeField] private bool _crossFadeByDefault = false;

        [Tooltip("朝向参数名（Float），每帧写入角色面朝方向。留空则不写；Device 无需配置。")]
        [SerializeField] private string _facingParam = "dirX";

        [Tooltip("FSM 状态名 -> 动画状态名 / 过渡方式 / 跳过 等映射。留空则状态名=动画状态名。")]
        [SerializeField] private List<StateMapping> _mappings = new();

        private readonly Dictionary<string, StateMapping> _map = new();

        /// <summary>
        /// 单个 FSM 状态的动画映射配置。
        /// </summary>
        [Serializable]
        public class StateMapping
        {
            [Tooltip("FSM 状态名，如 \"Idle\"")]
            public string fsmState;

            [Tooltip("Animator 状态名，留空则与 fsmState 同名")]
            public string animState;

            [Tooltip("true=一次性(Dead/Stunned/Open), false=循环。仅记录，实际循环由 Animator Clip 设置决定")]
            public bool isOneShot;

            [Tooltip("是否用 CrossFade 过渡；以组件 _crossFadeByDefault 为初值，这里逐项覆盖")]
            public bool crossFade;

            [Tooltip("true=该状态不驱动 Animator（如 Hidden/Follow）")]
            public bool skipAnimation;
        }

        /// <summary>
        /// 订阅目标 SceneObjBase 的状态变更事件。
        /// </summary>
        private void OnEnable()
        {
            BuildMap();
            if (_target != null)
                _target.OnStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (_target != null)
                _target.OnStateChanged -= HandleStateChanged;
        }

        private void Start()
        {
            // 挂载后同步当前状态，避免「挂了组件但不动」。
            // 若组件在对象已处于某状态（如 Idle）后挂载，OnEnable 订阅时该状态已过去，
            // 需要这里主动播一次。
            if (_target != null)
                PlayState(_target.StateName);
        }

        private void Update()
        {
            // 朝向参数：每帧写一次（Blend Tree 需要连续值）。
            // 只对 CharaBase 写；Device 不是 CharaBase，自动跳过。
            if (string.IsNullOrEmpty(_facingParam) || _animator == null)
                return;
            if (_target is CharaBase chara)
                _animator.SetFloat(_facingParam, chara.IsRight ? 1f : -1f);
        }

        private void HandleStateChanged(SceneObjBase obj, string oldState, string newState)
        {
            PlayState(newState);
        }

        private void BuildMap()
        {
            _map.Clear();
            if (_mappings == null) return;
            foreach (var m in _mappings)
            {
                if (m != null && !string.IsNullOrEmpty(m.fsmState))
                    _map[m.fsmState] = m;
            }
        }

        /// <summary>
        /// 按 FSM 状态名驱动 Animator。
        /// 映射命中且 skipAnimation=true 时直接返回；未命中则用 fsmState 本身作为动画状态名。
        /// </summary>
        private void PlayState(string fsmState)
        {
            if (_animator == null || string.IsNullOrEmpty(fsmState))
                return;

            string animState = fsmState;
            bool crossFade = _crossFadeByDefault;

            if (_map.TryGetValue(fsmState, out var m))
            {
                if (m.skipAnimation)
                    return;
                animState = string.IsNullOrEmpty(m.animState) ? fsmState : m.animState;
                crossFade = m.crossFade;
            }

            if (crossFade)
                _animator.CrossFade(animState, _crossFadeDuration);
            else
                _animator.Play(animState);
        }
    }
}
