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
    /// <para>
    /// v0.22.17 扩展：转换边动画（fromState->toState 命中过渡入口）+ 物理参数透传（velY/grounded）。
    /// 单 FSM 状态内的动画内部流转（如起跑->走动循环、跳跃各阶段）由 Animator 自管，
    /// 组件只负责「进入入口」这一步，之后靠 Animator 内部 Transition + 参数条件驱动。
    /// </para>
    /// <para>
    /// v0.22.18 扩展：PlayOneShot 驱动上层 Action Layer 播放一次性动作动画；
    /// 状态切换时全清上层（ClearActionLayer），保证抢占正确。
    /// </para>
    /// </summary>
    public class SceneObjAnimator : MonoBehaviour
    {
        // === 基础配置 ===
        [SerializeField] private SceneObjBase _target;
        [SerializeField] private Animator _animator;
        [SerializeField] private float _crossFadeDuration = 0.1f;

        [Tooltip("未在映射表逐项配置时的默认过渡方式。逐帧 Sprite 动画建议 false（瞬切，避免重影）。")]
        [SerializeField] private bool _crossFadeByDefault = false;

        [Tooltip("Action Layer 索引（Base Layer = 0）。美术在 Animator 里调整层级后，这里同步改。")]
        [SerializeField] private int _actionLayerIndex = 1;

        // === 参数透传（每帧向 Animator 写参数） ===
        [Tooltip("朝向参数名（Float），每帧写入角色面朝方向。留空则不写；Device 无需配置。")]
        [SerializeField] private string _facingParam = "dirX";

        [Tooltip("垂直速度参数名（Float），每帧写 Rigidbody2D.velocity.y。留空则不写；用于跳跃阶段切换。")]
        [SerializeField] private string _velYParam = "";

        [Tooltip("触地参数名（Bool），每帧写 GroundCheck 是否触地。留空则不写；用于跳跃落地检测。")]
        [SerializeField] private string _groundedParam = "";

        [Tooltip("拖入 GroundCheck 子物体的 Collider2D（建议 CircleCollider2D + IsTrigger）。每帧用 IsTouchingLayers 判定触地。")]
        [SerializeField] private Collider2D _groundCollider;

        [Tooltip("地面 Layer 掩码。GroundCheck Collider 触及这些 Layer 时 grounded=true。留 0 则不写 grounded。")]
        [SerializeField] private LayerMask _groundLayerMask = 0;

        // === 状态映射 ===
        [Tooltip("FSM 状态名 -> 动画状态名 / 过渡方式 / 跳过 等映射。留空则状态名=动画状态名。")]
        [SerializeField] private List<StateMapping> _mappings = new();

        // === 转换边动画（v0.22.17） ===
        [Tooltip("FSM 状态转换边 -> 过渡动画入口映射。命中则播过渡动画（如起跑/刹车），未命中走状态映射。")]
        [SerializeField] private List<TransitionMapping> _transitions = new();

        private readonly Dictionary<string, StateMapping> _map = new();
        private readonly Dictionary<string, TransitionMapping> _transitionMap = new();

        // 组件自缓存 Rigidbody2D，避免访问 CharaBase 的 protected 成员
        private Rigidbody2D _rigidbody2D;

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
        /// FSM 状态转换边 -> 过渡动画入口的映射（v0.22.17）。
        /// 命中时播过渡动画（如起跑/刹车），之后由 Animator 内部 HasExitTime Transition 接管转到目标循环。
        /// </summary>
        [Serializable]
        public class TransitionMapping
        {
            [Tooltip("旧 FSM 状态名，如 \"Idle\"")]
            public string fromState;

            [Tooltip("新 FSM 状态名，如 \"Move\"")]
            public string toState;

            [Tooltip("过渡动画入口名，如 \"Move_Start\"")]
            public string animState;

            [Tooltip("是否用 CrossFade 进入过渡动画（默认 true）")]
            public bool crossFade = true;
        }

        /// <summary>
        /// 订阅目标 SceneObjBase 的状态变更事件。
        /// </summary>
        protected virtual void OnEnable()
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

        protected virtual void Start()
        {
            // 缓存 Rigidbody2D（组件自洽，不改 CharaBase）
            // 只对挂了 Rigidbody2D 的对象有效；Device 无此组件则为 null
            if (_target is CharaBase)
                _rigidbody2D = GetComponent<Rigidbody2D>();

            // 挂载后同步当前状态，避免「挂了组件但不动」。
            // 若组件在对象已处于某状态（如 Idle）后挂载，OnEnable 订阅时该状态已过去，
            // 需要这里主动播一次。
            if (_target != null)
                PlayState(_target.StateName);
        }

        private void Update()
        {
            if (_animator == null) return;

            // 朝向（v0.22.16 已有）：每帧写一次（Blend Tree 需要连续值）。
            // 只对 CharaBase 写；Device 不是 CharaBase，自动跳过。
            if (!string.IsNullOrEmpty(_facingParam) && _target is CharaBase chara)
                _animator.SetFloat(_facingParam, chara.IsRight ? 1f : -1f);

            // 物理参数透传（v0.22.17）：只对有 Rigidbody2D 的对象写
            if (_rigidbody2D != null && !string.IsNullOrEmpty(_velYParam))
            {
                float velY = _rigidbody2D.velocity.y;
                _animator.SetFloat(_velYParam, velY);
            }

            // grounded：用 GroundCheck 子物体的 Collider 判定触地
            if (!string.IsNullOrEmpty(_groundedParam) && _groundCollider != null)
            {
                bool grounded = _groundCollider.IsTouchingLayers(_groundLayerMask);
                _animator.SetBool(_groundedParam, grounded);
            }
        }

        private void HandleStateChanged(SceneObjBase obj, string oldState, string newState)
        {
            // v0.22.18：状态切换时全清 Action Layer（上层一次性动作动画）
            ClearActionLayer();

            // 1. 先查转换映射 (oldState, newState) -- v0.22.17
            //    命中则播过渡动画入口（如 Move_Start），之后由 Animator 内部 HasExitTime Transition 接管
            if (!string.IsNullOrEmpty(oldState) && _transitionMap.Count > 0)
            {
                string key = oldState + "->" + newState;
                if (_transitionMap.TryGetValue(key, out var t))
                {
                    PlayAnim(t.animState, t.crossFade);
                    return;
                }
            }

            // 2. 未命中：v0.22.16 行为不变，直接播 newState 对应动画
            PlayState(newState);
        }

        private void BuildMap()
        {
            _map.Clear();
            if (_mappings != null)
            {
                foreach (var m in _mappings)
                {
                    if (m != null && !string.IsNullOrEmpty(m.fsmState))
                        _map[m.fsmState] = m;
                }
            }

            _transitionMap.Clear();
            if (_transitions != null)
            {
                foreach (var t in _transitions)
                {
                    if (t != null && !string.IsNullOrEmpty(t.fromState) && !string.IsNullOrEmpty(t.toState))
                        _transitionMap[t.fromState + "->" + t.toState] = t;
                }
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

            PlayAnim(animState, crossFade);
        }

        /// <summary>
        /// 按动画状态名 + 过渡方式直接驱动 Animator（v0.22.17 抽出，供转换映射复用）。
        /// </summary>
        private void PlayAnim(string animState, bool crossFade)
        {
            if (_animator == null || string.IsNullOrEmpty(animState))
                return;

            if (crossFade)
                _animator.CrossFade(animState, _crossFadeDuration);
            else
                _animator.Play(animState);
        }

        // === v0.22.18: 上层 Action Layer 一次性动作动画 ===

        /// <summary>
        /// 运行时自检 Action Layer 配置（v0.22.18）。
        /// 配置错误时打 LogWarning 提示，不影响运行。供需要动作动画的子类（如 PlayerAnimator）在 Start 调用。
        /// </summary>
        protected void CheckActionLayerConfig()
        {
            if (_animator == null)
            {
                Debug.LogWarning($"[SceneObjAnimator] {name}: _animator 未配置，跳过 Action Layer 检查。", this);
                return;
            }

            // 1. Layer 索引越界检查
            if (_actionLayerIndex < 0 || _actionLayerIndex >= _animator.layerCount)
            {
                Debug.LogWarning(
                    $"[SceneObjAnimator] {name}: Action Layer 索引 {_actionLayerIndex} 超出 Animator 层数（共 {_animator.layerCount} 层）。" +
                    "动作动画将无法播放。请在 Inspector 里把 _actionLayerIndex 改成 Action Layer 的实际索引。", this);
                return;
            }

            // 2. Action Layer 权重为 0 检查（最隐蔽，动画会被完全抹掉）
            float weight = _animator.GetLayerWeight(_actionLayerIndex);
            if (weight <= 0f)
            {
                Debug.LogWarning(
                    $"[SceneObjAnimator] {name}: Action Layer（索引 {_actionLayerIndex}）权重为 {weight}。" +
                    "权重为 0 时该层动画完全不显示。请在 Animator 窗口 Layers 面板把该层 Weight 改成 1。", this);
            }

            // 3. Action Layer 默认状态检查（应为 Empty，否则进场景时上层就停在某个动作状态）
            //    仅编辑器可读取默认状态；运行时跳过，避免误报
#if UNITY_EDITOR
            string defaultStateName = GetLayerDefaultStateName(_actionLayerIndex);
            if (!string.IsNullOrEmpty(defaultStateName) && defaultStateName != "Empty")
            {
                Debug.LogWarning(
                    $"[SceneObjAnimator] {name}: Action Layer（索引 {_actionLayerIndex}）的默认状态是 \"{defaultStateName}\"，应为 \"Empty\"。" +
                    "否则进场景时上层就停在动作状态，交互时重播同名状态无效。请在 Animator 里右键 Empty → Set as Layer Default State。", this);
            }
#endif
        }

        /// <summary>
        /// 读取指定 Layer 的默认状态名（AnimatorController 层默认状态）。
        /// 仅编辑器内调用。
        /// </summary>
        private string GetLayerDefaultStateName(int layerIndex)
        {
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                var controller = _animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                if (controller != null && layerIndex < controller.layers.Length)
                {
                    var sm = controller.layers[layerIndex].stateMachine;
                    if (sm != null && sm.defaultState != null)
                        return sm.defaultState.name;
                }
            }
            return null;
        }

        /// <summary>
        /// 在上层 Action Layer 播放一次性动作动画（v0.22.18）。
        /// 供 PlayerAnimator 等子类调用，也可直接调用。
        /// </summary>
        public void PlayOneShot(string animState, bool crossFade = true)
        {
            if (_animator == null || string.IsNullOrEmpty(animState))
                return;

            if (crossFade)
                _animator.CrossFade(animState, _crossFadeDuration, _actionLayerIndex);
            else
                _animator.Play(animState, _actionLayerIndex);
        }

        /// <summary>
        /// 清空上层 Action Layer（回到空状态，透出底层）。
        /// 状态切换时调用，保证 FSM 抢占正确。
        /// </summary>
        protected void ClearActionLayer()
        {
            if (_animator == null)
                return;

            // 用空状态名 "Empty" 跳转；Animator Action Layer 需有一个名为 "Empty" 的默认空状态
            _animator.Play("Empty", _actionLayerIndex);
        }
    }
}
