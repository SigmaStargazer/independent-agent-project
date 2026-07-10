# 技术方案 — v0.22.1 调试工具：SceneObj 状态历史记录器

> **状态**：已实现
> **依据需求**：v0.22.1 联调过程中发现 EnemyBase 仍有"莫名其妙转头"现象，需要在运行时记录某个 SceneObj 的完整状态切换历史，以定位异常触发源。
> **最后更新**：2026-07-10

---

## 1. 背景与目标

v0.22.1 引入 Alerted / Investigate / Inspect / Searching 四个新状态后，状态跳转路径变复杂。已修复两处已知问题（`IsInBattle` 归属、`BrokenGlass` 误响应子 Trigger），但仍观测到不明原因的转头。

**根因定位的关键障碍**：当前只能在 `Debug.Log` 控制台看零散日志，难以重建"**谁、在什么时刻、从哪个状态切到哪个状态**"的完整时序，尤其是定位是哪一次 `ChangeState` 把朝向带偏的。

**目标**：提供一个可挂载到场景里的轻量调试组件，把指定 SceneObj 的每次状态切换**追加**到一个文本域，形成时序日志，便于人工复盘。

## 2. 范围与约束

- **纯调试工具**，不进入正式发布路径；最终可通过编译开关或场景里不挂载来禁用。
- **不改任何现有业务代码**（不改 `SceneObjBase`、`EnemyBase`、`BrokenGlass`）。
- 仅 Unity 侧、单文件脚本 + 场景手工挂载。
- 文本域用项目已在用的 TextMeshPro（`TMP_Text`，UI 与 3D 文本都用同一类型）。
- 无 Python / 协议 / 记忆改动。

## 3. 详细设计

### 3.1 文件

新增：`Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Debug/SceneObjStateLogger.cs`

放在 `Debug` 子目录，与业务代码物理隔离，便于未来整体剔除。

### 3.2 组件职责

`SceneObjStateLogger : MonoBehaviour`：

- **Inspector 输入**：
  - `TMP_Text TargetTextField`：状态历史要写入的文本域（UI 或 3D `TMP_Text` 均可，由用户在场景里创建并拖入）。
  - `SceneObjBase Target`：要监听的 SceneObj（通常是 EnemyBase 实例）。
  - `int MaxLines = 30`：文本域最多保留的行数，超出后丢弃最旧行（防止无限增长卡 UI）。
  - `bool IncludeTimestamp = true`：每行是否带 `Time.time`。
  - `bool LogToConsole = false`：是否同时 `Debug.Log`（调试初期可能想双开）。
- **生命周期**：
  - `OnEnable`：订阅 `Target.OnStateChanged += HandleStateChanged`；若 `Target` 已有当前状态，先写入一行 `[init] <当前状态>` 作为基线。
  - `OnDisable`：反订阅 `Target.OnStateChanged -= HandleStateChanged`。
- **`HandleStateChanged(SceneObjBase obj, string oldState, string newState)`**：
  - 构造一行：`[<Time.time:F2>] <obj.Name>: <oldState> -> <newState>`。
  - 追加到 `TargetTextField.text` 末尾（用 `\n` 连接）。
  - 若总行数 > `MaxLines`，截掉最旧的若干行。
  - 若 `LogToConsole`，同步 `Debug.Log` 同样字符串。
- **`Clear()`**：公开方法（可被其他调试按钮调用），清空文本域。
- **`OnDestroy`**：防御性反订阅，避免 `Target` 比本组件存活更久时触发空引用。

### 3.3 关键实现点

**为什么用 `OnStateChanged` 事件而不是反射 / Update 轮询？**

`SceneObjBase.ChangeState` 末尾固定触发 `OnStateChanged?.Invoke(this, oldStateName, stateName)`，这是唯一权威的状态变更出口（`ChangeState` 对相同状态短路、未注册状态报错，都不会误触发事件）。订阅事件能拿到精确的 old→new，且零开销。

**为什么不放在 `EnemyBase` 里写 `Debug.Log`？**

- 业务代码不应承担调试职责。
- `OnStateChanged` 已经是公开事件，外部组件订阅即可，侵入性最小。
- 同一个 logger 可挂给任意 SceneObj（Player、其他 Device），不只服务于 EnemyBase。

**`MaxLines` 截断实现**：

用 `string.Join("\n", lines)` 重构 text。`lines` 内部维护为 `List<string>`，超出 `MaxLines` 时 `RemoveRange(0, overflow)`。避免每次都 split 整个 text。

**`Target` 为空 / 销毁的处理**：

- `OnEnable` 时 `Target == null` → 写一行 `[error] Target 未赋值` 到文本域并禁用自身。
- `HandleStateChanged` 里若 `TargetTextField == null` → 静默 return（不抛异常，调试场景下文本域可能被随手删）。

### 3.4 场景配置步骤（由你手工完成）

1. 在场景里创建一个 UI Canvas（若已有可复用），加一个 `TMP_Text` 子物体作为日志显示区，调整字号 / 锚点 / 自动换行。
   - 或用 3D TextMeshPro（`TextMeshPro - Text`，非 UI），直接挂在世界空间里跟着某个物体——视你方便。
2. 创建一个空 GameObject，挂 `SceneObjStateLogger` 组件。
3. Inspector 里：
   - `Target`：拖入要观察的 EnemyBase 实例。
   - `TargetTextField`：拖入第 1 步创建的 `TMP_Text`。
   - `MaxLines`：默认 30；若想看长时序可调到 100+。
4. Play。每次该 EnemyBase 状态切换，文本域会追加一行。

### 3.5 代码草案

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace IndependentAgentProject.DebugTools
{
    /// <summary>
    /// 调试组件：订阅指定 SceneObjBase 的 OnStateChanged 事件，
    /// 把每次状态切换追加到 TMP_Text，形成时序日志。
    /// 纯调试用途，不依赖任何业务逻辑；不挂载即不生效。
    /// </summary>
    public class SceneObjStateLogger : MonoBehaviour
    {
        [SerializeField] private TMP_Text targetTextField;
        [SerializeField] private SceneObjBase target;
        [SerializeField] private int maxLines = 30;
        [SerializeField] private bool includeTimestamp = true;
        [SerializeField] private bool logToConsole = false;

        private readonly List<string> lines = new List<string>();

        private void OnEnable()
        {
            if (target == null)
            {
                AppendLine("[error] Target 未赋值");
                enabled = false;
                return;
            }
            target.OnStateChanged += HandleStateChanged;
            AppendLine($"[init] {target.GetStateName()}");
        }

        private void OnDisable()
        {
            if (target != null)
                target.OnStateChanged -= HandleStateChanged;
        }

        private void OnDestroy()
        {
            if (target != null)
                target.OnStateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(SceneObjBase obj, string oldState, string newState)
        {
            string ts = includeTimestamp ? $"[{Time.time:F2}] " : "";
            AppendLine($"{ts}{obj.Name}: {oldState} -> {newState}");
        }

        private void AppendLine(string line)
        {
            if (targetTextField == null) return;
            lines.Add(line);
            int overflow = lines.Count - maxLines;
            if (overflow > 0) lines.RemoveRange(0, overflow);
            targetTextField.text = string.Join("\n", lines);
            if (logToConsole) Debug.Log(line);
        }

        public void Clear()
        {
            lines.Clear();
            if (targetTextField != null) targetTextField.text = "";
        }
    }
}
```

**命名空间**：用 `IndependentAgentProject.DebugTools` 与业务代码隔离。若项目里 `Debug` 子命名空间有冲突可改为 `IndependentAgentProject.DevTools`。

## 4. 使用方式与预期输出

挂到观察 EnemyBase A 上，Play 后文本域大致长这样：

```
[init] Idle
[12.34] 敌人: Idle -> Alerted
[13.34] 敌人: Alerted -> Idle
[15.50] 敌人: Idle -> Chase
[16.20] 敌人: Chase -> Searching
[17.10] 敌人: Searching -> Inspect
[22.10] 敌人: Inspect -> Idle
```

定位"莫名其妙转头"时，重点看：
- 是否有非预期的 `Idle -> Alerted`（说明被异常事件触发了）。
- `Alerted -> Idle` 之后朝向是否恢复（验证 `mArrivedFromPatrol` 修复是否生效）。
- 是否有 `Idle -> Move` 又很快 `Move -> Idle`（巡逻点设置问题）。
- 是否有从未预期的状态名出现（说明状态机被外部代码强切）。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| 忘记反订阅导致空引用 | `OnDisable` + `OnDestroy` 双保险反订阅 |
| 文本域被删后 `AppendLine` 报错 | `targetTextField == null` 时静默 return |
| `MaxLines` 设太大导致 UI 卡 | 默认 30；用户可自行调小 |
| 调试代码混入发布 | 放 `Debug/` 子目录 + 独立命名空间；发布前 `git rm` 或场景不挂载即可 |
| `Target` 在 Play 中被销毁 | `OnStateChanged` 是 C# event，Target 销毁时其 GameObject 上的 logger 会被一起销毁（若 logger 也挂在 Target 上）；若 logger 挂在别处，`OnDisable` 反订阅兜底 |

回退方案：删除 `SceneObjStateLogger.cs` 文件 + 移除场景里的挂载，零副作用。

## 6. 测试建议

- **自测**（Unity 运行时）：
  1. 场景放一个 EnemyBase + 两个巡逻点 + 一个 BrokenGlass；挂 logger 到 EnemyBase。
  2. Play 后不操作，观察是否只有 `[init] Idle`（正常）或是否有意外状态切换（异常）。
  3. 让 Player 踩玻璃，观察日志是否依次出现 `Idle -> Alerted -> Investigate -> Inspect -> Idle`。
  4. 让 EnemyBase 被视野发现，观察 `Idle -> Chase -> Searching -> Inspect -> Idle`。
  5. 验证 `MaxLines` 截断：把 `MaxLines` 设为 3，连续触发 5 次切换，确认只保留最后 3 行。
  6. 验证反订阅：Play 中移除 logger GameObject，确认控制台无 `MissingReferenceException`。

---

*本文档由 Cursor Agent 生成；**你确认后** Agent 方可按本方案创建脚本。*
