# v0.21.0 训练场扩展方案 — `solution_training_ground.md`

> 状态：**已实现**
> 创建：2026-06-14
> 最后更新：2026-06-17
> 关联：`PRD.md`、`solution.md`（v0.21.0 主方案）、`test_checklist_unity.md`

## 0. 目标与范围

为 ActionSkill 训练场打通三块基础设施：

1. **`AgentExportSkillsRequest`**：UI 触发 → Python 把指定 Agent 的全部技能写到本地 YAML 文件，无需返回客户端（不需要 UI 预览）。
2. **`CheckPoint`（继承 `SceneObjBase`） + `PlayerBase.UpdateCheckPoint` / `ReturnToCheckPoint`**：碰到 CheckPoint 即记录最近重生点（取 CheckPoint 上挂载的 `respawnAnchor` 物体位置，避免卡到地下）；后续被陷阱 / 显式调用时回到该锚点位置并清干净 Action / ActionSequence。
3. **`Trap`（继承 `SceneObjBase`）**：参考现有 `Abyss` 实现，但用 `ReturnToCheckPoint` 取代 `Die()`，让训练循环可重复运行。

附带工程改动：

- **初始技能存放路径迁移**：把 `default_skills.yaml` 从 `Src/PythonServer/action_skill_system/` 移到 `Src/PythonServer/db/default_skills/`，提供两级 fallback：`<group_id>.yaml`（按 Agent 定制）→ `default.yaml`（兜底）。导出文件也写到这个目录下，便于训练后"挑选 → 改名 → 落盘"。

---

## 1. 涉及文件清单

| # | 文件 | 改动类型 |
|---|---|---|
| 1 | `Tools/message.proto` | **新增** `AgentExportSkillsRequest` / `AgentExportSkillsResponse` + 入 `NetMessageRequest/Response` oneof |
| 2 | `Src/Lib/AgentProtocol/...`（生成产物） | 走 `1.genproto.cmd` → `2.copyprotocol.cmd` 流程，**禁手改** |
| 3 | `Src/PythonServer/main.py` | **新增** `handle_agent_export_skills_request`；`load_default_skills` 路径调整为 `db/default_skills/<group_id>.yaml`，找不到时回退到通用 `default.yaml` |
| 4 | `Src/PythonServer/action_skill_system/default_skill_loader.py` | **修改**：支持 `group_id` 参数 + 新路径 + fallback |
| 5 | `Src/PythonServer/action_skill_system/action_skill_manager.py` | 已有 `export_skills_yaml`，本次仅复用 |
| 6 | `Src/PythonServer/db/default_skills/`（新目录） | **新增**：放 `default.yaml`（通用）和后续训练产出的 `<group_id>.yaml` |
| 7 | `Src/IndependentAgentProject/.../Services/AgentService.cs` | **新增** `SendAgentExportSkills(string name)` + `OnAgentExportSkills` 事件 + 订阅/反订阅 |
| 8 | `Src/IndependentAgentProject/.../ViewController/Gameplay/Chara/Core/PlayerBase.cs` | **新增** `ReturnToCheckPoint()` 虚方法 + `LastCheckPoint` 字段 |
| 9 | `Src/IndependentAgentProject/.../ViewController/Gameplay/Chara/AIPlayer.cs` | **覆写** `ReturnToCheckPoint()`：停 ActionSequence + StopMovement + SendFeedback |
| 10 | `Src/IndependentAgentProject/.../ViewController/Gameplay/Device/CheckPoint.cs` | **新增**：`SceneObjBase` 子类；`OnTriggerEnter2D` 调用 `player.UpdateCheckPoint(this)`；带可序列化字段 `respawnAnchor`（Transform） |
| 11 | `Src/IndependentAgentProject/.../ViewController/Gameplay/Device/Trap.cs` | **新增**：`SceneObjBase` 子类，`OnTriggerEnter2D` 调用 `player.ReturnToCheckPoint()` |
| 12 | `Src/IndependentAgentProject/.../MessageDispatch.cs`（生成产物） | **生成** AgentExportSkillsResponse 分发 |
| 13 | `DevDocs/v0.21.0/test_checklist_unity.md` | 追加训练场相关用例 |

> ⚠️ 协议改动**严格**走 `Tools/message.proto → 1.genproto.cmd → MessageDispatch.cs → Rebuild CSharpClient.sln → 2.copyprotocol.cmd`，禁止手改 `message_pb2.py` / `message.cs`。

---

## 2. 功能 1：AgentExportSkills（导出技能）

### 2.1 协议定义

`Tools/message.proto` 追加：

```proto
// ===== Agent 技能导出（v0.21.0 训练场） =====
message AgentExportSkillsRequest {
    string name = 1;            // 要导出的 Agent 名（对应 group_id = name.utf8.hex）
}

message AgentExportSkillsResponse {
    bool success = 1;
    string errormsg = 2;
    int32 skill_count = 3;      // 导出技能数量；success=false 时为 0
}
```

`NetMessageRequest` / `NetMessageResponse` 各新增一个 oneof 槽位（编号顺延，例如 31）。

> Python 端直接把 yaml 写到 `db/default_skills/exports/<name>_<timestamp>.yaml`；客户端只需要知道导出成功与数量即可，文件路径不返回（开发者去本地目录找文件）。

### 2.2 Python 服务端

`main.py` 新增 handler：

```python
@server.on_message(message_pb2.AgentExportSkillsRequest)
async def handle_agent_export_skills_request(msg, context):
    name = msg.name
    response = message_pb2.AgentExportSkillsResponse()
    try:
        group_id = name.encode("utf-8").hex()
        yaml_text = await ActionSkillManager().export_skills_yaml(group_id)
        skills = await ActionSkillManager().get_all_skills(group_id)

        # 写到 db/default_skills/exports/<safeName>_<timestamp>.yaml
        export_dir = os.path.join(
            os.path.dirname(__file__), "db", "default_skills", "exports"
        )
        os.makedirs(export_dir, exist_ok=True)
        ts = datetime.now().strftime("%Y%m%d_%H%M%S")
        safe_name = re.sub(r"[^\w\u4e00-\u9fa5\-]", "_", name)
        file_path = os.path.join(export_dir, f"{safe_name}_{ts}.yaml")
        with open(file_path, "w", encoding="utf-8") as f:
            f.write(yaml_text)
        print(f"[main] 已导出 Agent '{name}' 的 {len(skills)} 个技能到 {file_path}")

        response.success = True
        response.skill_count = len(skills)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
    await context['server'].send_message(response, context)
```

> 复用 `ActionSkillManager.export_skills_yaml(group_id)`（保留所有 source 类型）。
> 文件名带时间戳避免覆盖；训练完到 `db/default_skills/exports/` 挑文件 → 改名为 `<group_id>.yaml` 或 `default.yaml` 移到上一级目录即可生效为初始技能。

### 2.3 Unity AgentService

`AgentService.cs`：

```csharp
public event UnityAction<bool, string, int> OnAgentExportSkills;
// 参数：success, errormsg, skillCount

// 构造函数中订阅
MessageDistributer.Instance.Subscribe<AgentExportSkillsResponse>(OnAgentExportSkills_);

// Dispose 中反订阅
MessageDistributer.Instance.Unsubscribe<AgentExportSkillsResponse>(OnAgentExportSkills_);

public void SendAgentExportSkills(string name)
{
    NetMessage message = new NetMessage();
    message.Request = new NetMessageRequest();
    message.Request.agentExportSkillsRequest = new AgentExportSkillsRequest();
    message.Request.agentExportSkillsRequest.Name = name;
    if (this.connected && AgentClient.Instance.Connected)
        AgentClient.Instance.SendMessage(message);
    else { pendingMessages.Enqueue(message); if (!connected && !connecting) ConnectToServer(); }
}

void OnAgentExportSkills_(object sender, AgentExportSkillsResponse response)
{
    OnAgentExportSkills?.Invoke(response.Success, response.Errormsg, response.SkillCount);
}
```

UI 层（用户后续自行实现）订阅事件 → 弹窗显示「导出 N 个技能成功，请到 `Src/PythonServer/db/default_skills/exports/` 查看」即可。

### 2.4 初始技能目录迁移

| 旧 | 新 |
|---|---|
| `Src/PythonServer/action_skill_system/default_skills.yaml`（通用一份） | `Src/PythonServer/db/default_skills/default.yaml`（兜底）<br>`Src/PythonServer/db/default_skills/<group_id>.yaml`（按 Agent 命名定制） |

`default_skill_loader.py` 调整：

```python
DEFAULT_SKILLS_DIR = os.path.join(
    os.path.dirname(__file__), "..", "db", "default_skills"
)

def load_default_skills(group_id: str | None = None, path: str | None = None) -> List[dict]:
    if path:
        target = path
    elif group_id:
        per_agent = os.path.join(DEFAULT_SKILLS_DIR, f"{group_id}.yaml")
        fallback = os.path.join(DEFAULT_SKILLS_DIR, "default.yaml")
        target = per_agent if os.path.exists(per_agent) else fallback
    else:
        target = os.path.join(DEFAULT_SKILLS_DIR, "default.yaml")
    if not os.path.exists(target):
        return []
    # ... yaml load 逻辑不变
```

`main.py.handle_agent_create_request` 调用：`load_default_skills(group_id=group_id)`；导入时仍把 `source` 全改为 `"default"`（PRD 已确认）。

> 训练流程：训练完用 `AgentExportSkills` 导出 yaml → 开发者删减后另存为 `db/default_skills/default.yaml` 或 `<group_id>.yaml` → 下次 NewGame 自动注入。

---

## 3. 功能 2：CheckPoint + ReturnToCheckPoint

### 3.1 PlayerBase 新增

`PlayerBase.cs`：

```csharp
public abstract class PlayerBase : CharaBase
{
    /// <summary>最近接触到的 CheckPoint，下次 ReturnToCheckPoint 用</summary>
    public CheckPoint LastCheckPoint { get; private set; }

    /// <summary>由 CheckPoint.OnTriggerEnter2D 调用，更新当前重生点</summary>
    public virtual void UpdateCheckPoint(CheckPoint cp)
    {
        LastCheckPoint = cp;
    }

    /// <summary>
    /// 返回最近 CheckPoint 的重生锚点：
    /// 1. 取 LastCheckPoint.GetRespawnPosition()（默认是挂载的 respawnAnchor，未设置则用自身 transform）
    /// 2. 速度归零（线速度 + 角速度，避免重生后继续滑行）
    /// 3. 子类可覆写：AIPlayer 需要停 ActionSequence 并发反馈
    /// 4. 若 LastCheckPoint 为 null（从未碰过），保持原地不动并打印警告
    /// </summary>
    public virtual void ReturnToCheckPoint()
    {
        if (LastCheckPoint == null)
        {
            Debug.LogWarning($"[{Name}] ReturnToCheckPoint called but LastCheckPoint is null");
            return;
        }
        transform.position = LastCheckPoint.GetRespawnPosition();
        if (mRigidbody2D != null)
        {
            mRigidbody2D.velocity = Vector2.zero;
            mRigidbody2D.angularVelocity = 0f;
        }
        ChangeState("Idle");
    }
}
```

> 注：`PlayerBase` 当前还有 `OnDeadEnter → KillPlayerCommand`，**保留不动**——本版本只是新增 CheckPoint 训练循环，老的死亡链路（结束游戏）继续可用。

### 3.2 AIPlayer 覆写

`AIPlayer.cs`：

```csharp
public override void ReturnToCheckPoint()
{
    // 1. 停掉当前 Action / ActionSequence
    //    - StopMovement(true) 会把 mCurActionRuntime 置 Aborted、清空，并停掉 ActionSequence
    StopMovement(stopActionSequence: true);

    // 2. 走 PlayerBase 通用逻辑（取 anchor 位置 + 速度归零 + Idle）
    base.ReturnToCheckPoint();

    // 3. 给 Agent 发反馈，让 LLM 感知到自己被传送
    //    feedback 自带打断语义（Q3 用户确认：feedback ≈ force_interrupt=true），下一轮 LLM 会立即重新决策
    SendFeedbackToAgent($"[训练场反馈]你触碰到陷阱，已被传送回最近的检查点 '{LastCheckPoint?.Name ?? "未命名检查点"}'。");
}
```

> `StopMovement(true)` 已实现的语义（见 `AIPlayer.cs:492`）：把 `mCurActionRuntime` 置 `Aborted` 并清空；`stopActionSequence=true` 时同步停 ActionSequence。这就是用户提到的"调用 StopMovement 即可"。

### 3.3 CheckPoint.cs（新增 SceneObjBase 子类）

```csharp
public class CheckPoint : SceneObjBase
{
    public override string Name => string.IsNullOrEmpty(customName) ? "检查点" : customName;
    public override string Desc => "训练场的安全点。触碰即记录为重生位置。";

    [SerializeField] private string customName = "";

    /// <summary>
    /// 实际重生位置：在 Inspector 里挂一个略高出地面的子物体，避免重生后卡进地里。
    /// 未设置时退回到 CheckPoint 自身 transform。
    /// </summary>
    [SerializeField, Tooltip("挂一个略高出地面的子物体作为重生锚点；未设置则用 CheckPoint 自身位置")]
    private Transform respawnAnchor;

    public Vector3 GetRespawnPosition()
    {
        return respawnAnchor != null ? respawnAnchor.position : transform.position;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        var player = collision.GetComponent<PlayerBase>();
        if (player != null)
        {
            player.UpdateCheckPoint(this);
        }
    }
}
```

> CheckPoint 继承 `SceneObjBase`（Q1/Q8 用户确认），会自动注册进 SceneObjManager；LLM `observe` 时能看到「检查点」存在，配合训练 prompt 能引导"先去检查点再尝试陷阱旁的技能"。
> "最近"语义采用**最后一次触发的**（Q2 用户确认）。
> Inspector 用法：在 CheckPoint Prefab 下创建一个空子物体（命名如 `RespawnAnchor`），位置略高于地面，把它拖进 `respawnAnchor` 字段。

### 3.4 Trap.cs（新增陷阱，参考 Abyss）

```csharp
public class Trap : SceneObjBase   // Q1 用户确认：与 Abyss 一致继承 SceneObjBase
{
    public override string Name => string.IsNullOrEmpty(customName) ? "陷阱" : customName;
    public override string Desc => string.IsNullOrEmpty(customDesc)
        ? "训练场的陷阱。触碰会被传送回最近的检查点。"
        : customDesc;

    [SerializeField] private string customName = "";
    [SerializeField, TextArea] private string customDesc = "";

    private void OnTriggerEnter2D(Collider2D collision)
    {
        var player = collision.GetComponent<PlayerBase>();
        if (player != null)
        {
            player.ReturnToCheckPoint();
        }
    }
}
```

---

## 4. 决策点确认结果（用户已答复 2026-06-14）

| # | 问题 | 最终决定 |
|---|---|---|
| Q1 | `Trap` / `CheckPoint` 继承基类 | **`SceneObjBase`**（两者都是；和 Abyss 一致） |
| Q2 | "最近的 CheckPoint" 语义 | **最后一次触发的** |
| Q3 | `ReturnToCheckPoint` 是否打断 Agent | **走 SendFeedback**（feedback 已等价于 force_interrupt=true） |
| Q4 | 没碰过 CheckPoint 就掉 Trap | **不动 + 警告** |
| Q5 | 导出 yaml 怎么返回 | **不返回客户端**：Python 直接写 `db/default_skills/exports/<name>_<ts>.yaml`，仅返回 `success` + `skill_count` + `errormsg` |
| Q6 | `db/default_skills/` 目录冲突 | **不冲突**（备份只动 `graphiti.kuzu*` 与 `db/backups/`） |
| Q7 | 旧 `default_skills.yaml` 处理 | **手动迁移** |
| Q8 | CheckPoint 是否注册 SceneObjManager | **是**（`SceneObjBase` 自动注册） |

额外要求：
- `PlayerBase` 上方法名为 **`UpdateCheckPoint`**（不是 `RegisterCheckPoint`）。
- `CheckPoint` 上挂一个 `respawnAnchor` Transform 作为实际重生点，避免角色卡进地里。

---

## 5. 实施顺序（开发期）

1. **proto 改动 + 重新生成**（Tools/message.proto → 1.genproto.cmd → 2.copyprotocol.cmd）；
2. **Python**：`default_skill_loader.py` 路径迁移 + 创建 `db/default_skills/`、把旧 `default_skills.yaml` 移到 `default.yaml`；`main.py` 加 export handler；
3. **Unity Service**：`AgentService.cs` 新增 `SendAgentExportSkills` + 事件；
4. **Unity 战斗层**：`PlayerBase` → `AIPlayer` → `CheckPoint.cs` → `Trap.cs`；
5. **Unity 场景**：在训练用关卡里摆 1 个 CheckPoint Prefab + 若干 Trap Prefab + 用 UI 按钮调 `AgentService.Instance.SendAgentExportSkills(name)`（UI 部分由用户自行接 / 后续单独提）；
6. **更新测试清单 `test_checklist_unity.md`**，追加训练场 G 类用例（见 §6）。

---

## 6. 测试清单（追加到 `test_checklist_unity.md`）

### G. 训练场基础设施

| ID | 步骤 | 期望 | 通过 |
|---|---|---|---|
| G1 | UI 按钮调 `SendAgentExportSkills(name)`，Agent 已有 ≥1 个技能 | 收到 `OnAgentExportSkills(success=true, errormsg="", skillCount==实际数)`；`db/default_skills/exports/` 下出现对应 yaml 文件，文件名形如 `<name>_<timestamp>.yaml` | ☐ |
| G2 | 打开 G1 写出的 yaml 文件 | 能被 `yaml.safe_load` 解析；`source` 保留训练原值（learned/refined/default 任一） | ☐ |
| G3 | 把 G1 文件改名为 `<group_id>.yaml` 移到上一级 `db/default_skills/` → 删档 → NewGame | 新 Agent 创建后 `list_action_skills` 列出全部导出技能；source 全为 `"default"` | ☐ |
| G4 | 删除 `<group_id>.yaml`，仅留 `default.yaml` → NewGame 一个**新 group_id** 的 Agent | 新 Agent 注入 `default.yaml` 内容；老 group_id（仍有 `<group_id>.yaml`）的 NewGame 走自己定制版 | ☐ |
| G5 | AIPlayer 走到 CheckPoint 上 | `LastCheckPoint` 被记录；无 Feedback 发送（CheckPoint 只是隐式记录，不打扰 LLM） | ☐ |
| G6 | AIPlayer 触碰 Trap | 立即位移到 `LastCheckPoint.GetRespawnPosition()`（即 respawnAnchor 位置）；Rigidbody 速度归零；ActionSequence/Action 全停（`mCurActionRuntime == null`） | ☐ |
| G7 | G6 之后 Agent 收到的 Feedback 包含"已被传送回最近的检查点"字样 | LLM 下一轮推理基于该反馈决策（feedback 自带打断语义） | ☐ |
| G8 | AIPlayer 从未碰过 CheckPoint 就掉进 Trap | 控制台打印警告、不传送、保持原位；`StopMovement(true)` 仍执行（mCurActionRuntime 被清） | ☐ |
| G9 | Trap 触发时 LLM 正在执行 ActionSequence | ActionSequence 被打断；feedback 队列中可见中断提示 + Trap 反馈 | ☐ |
| G10 | CheckPoint 的 `respawnAnchor` 字段未设置 | 重生位置 = CheckPoint 自身 transform.position；不报空引用 | ☐ |
| G11 | 多个 CheckPoint 依次触碰 A → B → C，再触发 Trap | 重生到 C 的 anchor；不是几何最近的那个 | ☐ |

---

## 7. 风险与回滚

- **proto 编号冲突**：本方案 oneof 槽位编号 31 是基于现有 `message.proto` 最大编号 30 顺延；生成前再确认一次。
- **CheckPoint 触发抖动**：玩家在 CheckPoint 上摆 ActionSequence 长时停留时，`OnTriggerEnter2D` 只在进入瞬间触发一次，不会反复刷新。如需更精细可加 `OnTriggerExit2D` 但本期不必。
- **respawnAnchor 引用 Prefab 外物体被销毁**：anchor 是同 Prefab 内的子物体，与 CheckPoint 同生共死，不存在跨场景引用悬空问题。
- **Trap 与移动 ErrorCondition 冲突**：当前 `Move` Action 的 `ErrorConditionFunc` 检测"撞击非允许物体"。Trap 触碰会先触发 Trap 的 `OnTriggerEnter2D` 还是 Action 的碰撞检查存在时序竞争——`StopMovement(true)` 在 Trap 中调用是幂等的，即使 Action 已经因 ErrorCondition 被中断，再调一次也无副作用。
- **导出文件目录可写性**：`db/default_skills/exports/` 由 `os.makedirs(exist_ok=True)` 创建；首次运行无目录时不会失败。如果文件系统只读（极少），handler 会捕获异常并返回 `success=false`。
- **回滚**：如果训练场失败，回滚步骤反向走 §5 即可；`db/default_skills/` 目录留着不删，避免历史导出丢失。

---

## 8. 不在本方案内的事项

- 训练场关卡的具体地图设计（哪儿放陷阱、出口在哪）—— 由用户在 Unity 编辑器内完成。
- 一键"开始训练→记录→导出"的端到端按钮 UI —— UI 层由用户自行实现，本方案只暴露 `SendAgentExportSkills`。
- 自动判断"哪些 source=learned 的技能值得保留"—— PRD 已确认开发者手动筛选。
- 训练场专用 Agent 配置（不同 LLM、不同 prompt）—— 走现有 `AgentCreateRequest.desc` 即可，无新增。

---

*用户已确认 Q1–Q8 + UpdateCheckPoint 命名 + respawnAnchor 设计（2026-06-14）。说一声「可以开发」即开工。*

---

## 9. 实现记录

| 日期 | 说明 |
|------|------|
| 2026-06-17 | Unity 端联调验收通过，本方案标记为「已实现」。 |


