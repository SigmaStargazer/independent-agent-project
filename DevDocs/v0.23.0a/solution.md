# 技术方案 — v0.23.0 Title API 配置 UI 与注入（提前实现）

> **状态**：已确认
> **依据 PRD**：`PRD.md`
> **引用方案**：`DevDocs/feature-design/打包方案.md`（§4.2、§4.3、§4.6、§10 v0.23.4）
> **最后更新**：2026-08-20

---

## 1. 方案概述

实现「Title 场景 API 配置 UI + 玩家自备 Key」闭环：

- **Unity 侧**：新增 `ApiConfigStore` 负责 `Data/Config/api_config.json`（本期明文）的读写与完整性校验；`UITitle` 接入四个配置面板的文本框双向绑定、退出变更检测、保存弹窗与入口拦截。`api_config.json` 加入 `.gitignore` 防止 Key 入库。
- **Python 侧**：新增统一配置读取层 `config/api_config_loader.py`，按 `api_config.json`（存在且字段非空）> `.env` 的优先级提供 12 项配置；`agent_interuptible.py`、`memory_manager.py`、`embedder_service.py` 改为从该层读取。**并做延迟初始化改造**：Python 无 Key 即可启动监听端口，收到 Unity init 信号后再读 `api_config.json` 注入 `os.environ` 并执行 `MemoryManager.initialize()`。
- **架构预留**：API 配置与未来的游戏设置（分辨率/语言等）**分离存储**，但抽公共 JSON 读写工具复用（见 §3.5）。

不引入新的协议消息（无需改 `message.proto`）。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Unity | `Assets/Scripts/IndependentAgentProject/Services/ApiConfigStore.cs`（新增） | 新增 |
| Unity | `Assets/Scripts/IndependentAgentProject/Services/JsonConfigIO.cs`（新增，公共 JSON 读写） | 新增 |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UITitle.cs` | 修改 |
| Unity | `Title.unity` / `Resources/UI/Msgbox.prefab` | 修改（场景绑定） |
| Python | `config/api_config_loader.py`（新增） | 新增 |
| Python | `agent_framwork/agents/agent_interuptible.py` | 修改（配置源 + llm 延迟构造） |
| Python | `memory_system/memory_manager.py` | 修改（配置源 + 延迟初始化） |
| Python | `memory_system/embedder/embedder_service.py` | 修改（配置源，时序确认） |
| Python | `main.py` | 修改（无 Key 启动 + init 信号 handler） |
| 协议 | `Tools/message.proto` | 新增 `InitRequest`/`InitResponse` |
| 工具 | `.gitignore` | 新增 `api_config.json` 忽略

## 3. 详细设计

### 3.1 配置数据契约（Unity ↔ Python 共用）

`Data/Config/api_config.json` 为 Unity 写入、Python 读取的公共空间（与 `agent_server_port.txt` 同目录体系）。JSON 结构：

```json
{
  "AGENT_API_BASE": "https://...",
  "AGENT_API_KEY": "sk-...",
  "AGENT_MODEL": "deepseek-v4-flash",
  "MEMORY_API_BASE": "...",
  "MEMORY_API_KEY": "...",
  "MEMORY_MODEL": "...",
  "EMBEDDING_API_BASE": "...",
  "EMBEDDING_API_KEY": "...",
  "EMBEDDING_MODEL": "...",
  "RERANKER_API_BASE": "...",
  "RERANKER_API_KEY": "...",
  "RERANKER_MODEL": "..."
}
```

- 编码 **UTF-8**，字段名与打包方案 §4.2 一致。
- 本期**明文**；加密（AES + 机器绑定密钥）留正式发布前（见 PRD §7）。

### 3.2 Unity（Environment）

#### 3.2.0 公共 JSON 读写工具 `JsonConfigIO.cs`（新增，`Services` 命名空间）

抽取**与业务无关**的 JSON 文件读写基础设施，供 `ApiConfigStore`（本期）与未来的 `GameSettingsStore`（分辨率/语言等，后续版本）复用：

| 方法 | 行为 |
|------|------|
| `string ConfigDir()` | 计算游戏根 `Data/Config/`（`Application.dataPath` 上级 → `Data/Config`，与 `AgentService.GetPort()` 同规则） |
| `T LoadJson<T>(string fileName, T fallback)` | 读 `ConfigDir()/fileName`，UTF-8、解析失败/不存在返回 fallback |
| `void SaveJson<T>(string fileName, T data)` | 序列化（缩进 JSON，UTF-8）写入 `ConfigDir()/fileName`，目录不存在则创建 |

**架构结论：API 配置与游戏设置分离**，理由：
1. `api_config.json` 属**敏感凭证**（含 Key，需 gitignore、后续加密），`game_settings.json` 属普通偏好——生命周期、安全要求完全不同，合一会让普通设置也被拖入「必须加密/gitignore」范畴。
2. 读取方不同：`api_config.json` 需 Python 读（固定公共路径 `Data/Config/`），游戏设置仅 Unity 自读，未来路径策略（如 `persistentDataPath`）可独立演进。
3. 复用点仅在「JSON 读写」这层，故抽 `JsonConfigIO` 共享，业务模型（`ApiConfigStore`/`GameSettingsStore`）各自独立。

#### 3.2.1 `ApiConfigStore.cs`（新增，`Services` 命名空间）

基于 `JsonConfigIO` 的业务封装，职责：

| 方法 | 行为 |
|------|------|
| `ApiConfig Load()` | `JsonConfigIO.LoadJson<ApiConfig>("api_config.json", 空配置)`；文件不存在/解析失败返回空 |
| `void Save(ApiConfig)` | `JsonConfigIO.SaveJson("api_config.json", config)` |
| `bool IsComplete()` | 12 项字段均非空 |

数据类 `ApiConfig`：12 个 string 字段（`AgentApiBase` … `RerankerModel`），提供 `ToDictionary()` / `FromDictionary()` 便于序列化与按键名取用。

#### 3.2.2 `UITitle.cs`（修改）

现状：`UITitle` 已持有 `mLLMAgentPanel`/`mLLMMemoryPanel`/`mEmbeddingPanel`/`mRerankerPanel`、`mSaveConfigMsgBox`、`mNoApiKeyMsgbox` 引用；`OnConfirmSaveConfig`/`OnCancelSaveConfig` 已有占位实现；`OnClickNewGame`/`OnClickContinueGame` 直接进 GameFlow。

改动：

1. **新增序列化字段**：四个面板各 3 个 `TMP_InputField`（`mAgentBaseInput/mAgentKeyInput/mAgentModelInput`，Memory/Embedding/Reranker 同理，共 12 个）；保存弹窗确认按钮引用。
2. **回填**：`Start()`（或首次 `ShowConfig`）时 `ApiConfigStore.Load()` 一次，将值填入 12 个文本框；打开每个子面板前确保已回填。
3. **退出变更检测**：进入子面板时记录 12 个文本框内容快照（或记录是否 dirty）；`ShowConfig()`/ESC 退出子面板时比对当前值与快照，有变更则 `mSaveConfigMsgBox.SetActive(true)`，无变更直接返回。
4. **`OnConfigSaveConfig`**（替换原 `OnConfirmSaveConfig` 占位）：
   - 收集 12 个文本框内容 → 构造 `ApiConfig` → `ApiConfigStore.Save(...)`；
   - 关闭 `MsgboxSaveConfig` → 返回设置总览。
5. **`OnCancelSaveConfig`**：关闭弹窗 → 返回设置总览（不写盘）。
6. **`OnClickNewGame` / `OnClickContinueGame` 前置拦截**：
   - `ApiConfigStore.IsComplete()` 为 true → 原逻辑进 GameFlow；
   - 为 false → 显示 `MsgboxNoApiKey`，返回。

> 注意：四个面板在 `Title.unity` 中为 Prefab 实例，文本框层级（`PanelBase`/`PanelApiKey`/`PanelModel` 三个子面板对应 BASE/KEY/MODEL）在场景/预制体绑定阶段按实际层级赋值，脚本仅持引用。

#### 3.2.3 场景/预制体绑定（编辑器操作）

- 在 `Title.unity`（或对应 Panel Prefab）将各子面板的 3 个 `InputField (TMP)` 拖入 `UITitle` 对应字段。
- `MsgboxSaveConfig` 的「保存」按钮 `OnClick` 指向 `UITitle.OnConfigSaveConfig`；「取消」指向 `OnCancelSaveConfig`（场景中已有 `OnConfirmSaveConfig` 绑定，需改挂新方法名）。
- `MsgboxNoApiKey` 的确认按钮 `OnClick` 关闭自身（绑定 `SetActive(false)` 或 `UIMsgBox` 默认 ESC 行为）。

### 3.3 Python（Brain）

#### 3.3.1 `config/api_config_loader.py`（新增）

统一配置读取层，作用是在进程启动早期把 `api_config.json` 的 12 项配置注入 `os.environ`，使后续所有 `os.getenv(...)` 调用点**无需改动语义**即可生效。

```python
# config/api_config_loader.py
API_CONFIG_KEYS = [
    "AGENT_API_BASE", "AGENT_API_KEY", "AGENT_MODEL",
    "MEMORY_API_BASE", "MEMORY_API_KEY", "MEMORY_MODEL",
    "EMBEDDING_API_BASE", "EMBEDDING_API_KEY", "EMBEDDING_MODEL",
    "RERANKER_API_BASE", "RERANKER_API_KEY", "RERANKER_MODEL",
]

def api_config_path() -> str:
    # 以本文件位置推算 PythonServer 根，再上两级 Data/Config/api_config.json
    # （与 main.PORT_CONFIG_FILE 同规则）
    ...

def load_api_config_into_env(force: bool = False) -> dict:
    """读取 api_config.json；对每个字段，若当前 os.environ 中不存在或为空，
    且 json 中该字段非空，则注入 os.environ。返回已注入的键值。"""
    ...
```

关键点：
- **优先级 `api_config.json > .env`**：`.env` 由 `load_dotenv()` 在各自模块 import 时已读入 `os.environ`；`load_api_config_into_env` 在 `main()` 早期调用，仅当 `os.environ` 中某键为空/缺失时用 json 覆盖。打包版无 `.env` 时 json 成为唯一来源，行为自然正确。
- 幂等、可重复调用；`main.py` 与测试均可安全调用。

#### 3.3.2 `main.py`（修改：无 Key 启动 + init 信号）

现状：`main()`（`304-325` 行）在启动时就 `await MemoryManager().initialize()`（`312` 行），此时读 `.env` Key。

改动：

1. **`main()` 移除全局 `MemoryManager().initialize()`**：`main()` 只做 `start_console_logging` + 起 `server.astart()` 监听端口（+ 保留 `TimeSystem().aset_time` 等不依赖 Key 的初始化）。Python 因此**无 Key 即可启动**。
2. **新增 `InitRequest` handler**（协议见 §3.5）：Unity 连上后、进场景前发送。handler 内顺序：
   - `load_api_config_into_env()`：读 `api_config.json` 注入 `os.environ`；
   - `await MemoryManager().initialize()`：用注入后的 Key 构造 Graphiti/LLM/Embedder；
   - 构造 `InitResponse(success, errormsg)` 回给 Unity。
   - **幂等**：重复收到 `InitRequest` 时 `MemoryManager.initialize()` 已有 `_initialized` 短路，直接返回成功。
3. **未初始化保护**：任何需要 LLM/记忆的操作（如 `AgentCreateRequest` 触发 `Agent` 推理）若在 init 信号前到达，`get_llm_with_tools()`/`MemoryManager` 应明确报「未初始化」错（可加 `is_initialized` 校验），而非用空 Key 静默失败（验收标准要求）。
4. **开发期兼容**：保留 `--auto-init` 命令行参数（argparse），手动起 Python 时带 `--auto-init` 则等效于收到 init 信号（读 json → initialize），沿用现有开发工作流；不带参数则进入「无 Key 监听，等 init 信号」模式。

> 注：`agent_interuptible.py` / `memory_manager.py` / `embedder_service.py` 的改动（§3.3.3/3.3.4/3.3.5）是让「无 Key 启动不报错、init 后能读到 Key」的前提，必须与本节一起实施。

#### 3.3.3 `agent_interuptible.py`（修改，llm 延迟构造）

现状（`125-139,181` 行）：模块级 `model = ChatOpenAI(...)`、`llm_with_tools = model.bind_tools(tools)`。

问题：若在模块 import 时读 env，而 `main.py` 的 `load_api_config_into_env()` 发生在 `MemoryManager().initialize()` 之前但 **`agent_interuptible` 由 `main.py` 顶部 `from ... import AgentManager` 先 import**，会先于 env 注入。因此把 `model`/`llm_with_tools` 改为**延迟构造**：

```python
_llm_with_tools = None

def get_llm_with_tools():
    global _llm_with_tools
    if _llm_with_tools is None:
        _model = ChatOpenAI(
            model_name=os.getenv("AGENT_MODEL"),
            openai_api_base=os.getenv("AGENT_API_BASE"),
            openai_api_key=os.getenv("AGENT_API_KEY"),
            streaming=False, verbose=True,
            request_timeout=float(os.getenv("AGENT_LLM_TIMEOUT", "120")),
            max_retries=int(os.getenv("AGENT_LLM_MAX_RETRIES", "1")),
        )
        _llm_with_tools = _model.bind_tools(tools)
    return _llm_with_tools
```

- `chatbot` 节点（`366` 行）改为 `await get_llm_with_tools().ainvoke(prompt)`。
- `tools` 列表保持模块级（不依赖 Key）；`graph` 的 `chatbot` 节点引用改为闭包调用。
- 首轮真正推理时才构造，此时 `main()` 已完成 env 注入，Key 已就绪。
- `chain`（`245-249` 行，已标废弃）：其引用 `llm_with_tools` 是模块级表达式，若保留会导致 import 时触发构造。方案：**直接删除该废弃 `chain` 定义**（或改为函数内引用 `get_llm_with_tools()`），确保模块 import 阶段不构造 LLM。
- 注意模块级 `memory_manager = MemoryManager()`（`252` 行）会在 import 时实例化 `MemoryManager`——若 `__init__` 内构造 `LLMConfig`/`_compress_model` 改为构造时 `os.getenv`，此时 env 尚未注入，Key 为 None。因此 `memory_manager.py` 的 `__init__` 中**不再构造 LLM 对象**（`LLMConfig`/`_compress_model` 延迟到 `initialize()` 内构造），或模块级 `memory_manager` 引用改为惰性单例。此项是 `memory_manager.py` 改动的核心点之一，须与 §3.3.4 合并执行。

#### 3.3.4 `memory_manager.py`（修改）

现状（`30-32,51-63`）：模块级 `mem_model_api_base/key/name = os.getenv(...)`，`__init__` 中固化 `LLMConfig`/`_compress_model`；且 `agent_interuptible.py` 模块级 `memory_manager = MemoryManager()` 会**在 import 阶段**实例化。

关键风险：若 `__init__` 内构造 LLM 对象，且读取时机早于 `main()` 的 `load_api_config_into_env()`，则 Key 为 None。

改动：
1. **`__init__` 不再构造 LLM 对象**：`self._llm_config` / `self._compress_model` 初始为 `None`，仅在 `initialize()` 内、env 注入完成后实时 `os.getenv` 构造（抽 `_build_llm_config()` / `_build_compress_model()` 辅助函数）。
2. `initialize()`（`96-...`）内 `self.graphiti = Graphiti(llm_client=OpenAIGenericClient(config=self._llm_config), ...)` 处，`self._llm_config` 已是 `initialize()` 内构造好的实例。
3. 移除对模块级 `mem_model_api_base/key/name` 的依赖（或改为仅作 `.env` 兜底注释）。
4. `_compress_model` 使用者（记忆压缩路径）在 `initialize()` 后取用，保证非 None。

> 时序保证：`MemoryManager.initialize()` 由 `main()` 在 `load_api_config_into_env()` 之后调用（§3.3.2），且 `EmbedderService.initialize()` 由其内部触发，故 embedder/reranker 读取到的也是 json 注入后的值。

#### 3.3.5 `embedder_service.py`（修改）

`initialize()`（`62-86` 行）在 `os.getenv("EMBEDDING_*"/"RERANKER_*")` 构造 embedder/reranker。因 `EmbedderService` 单例由 `MemoryManager.initialize()` 触发、晚于 env 注入，**无需改动代码**即可读到 json 值；仅需确认调用时序在 `load_api_config_into_env()` 之后（由 `main()` 顺序保证）。可选：把 `os.getenv` 提取到统一配置层常量，保持一致风格。

### 3.4 配置生效链路（本期）

```
Title 保存 → ApiConfigStore.Save → Data/Config/api_config.json（明文）
   ↓
Python 启动（无 Key，仅监听端口；开发期可 --auto-init）
   ↓
Unity 连上后发 InitRequest（进场景前）
   ↓
main: InitRequest handler → load_api_config_into_env() → os.environ 注入 12 项
   ↓
MemoryManager.initialize() / Agent 首轮推理 → 读到 Key 构造 LLM/Embedder
```

- 开发期：`api_config.json` 缺失时 `load_api_config_into_env` 不注入任何键，走 `.env` 原值 → 现有工作流不受影响（`--auto-init` 直接完成上述 init 流程）。
- 改配置生效：`api_config.json` 变更需**重启 Python**（开发期重启进程；打包版退出游戏后重进，Python 子进程随游戏重启），或下次进入游戏时由 init 信号重新读取。

### 3.5 协议：`InitRequest` / `InitResponse`（新增）

参考打包方案 §4.3/§4.6（v0.23.3 复用 `SceneStartRequest` 或新增 `InitRequest` 均可）。本期采用**新增独立消息**，职责更清晰：

```proto
// Tools/message.proto
message InitRequest { }        // 空消息：请求初始化（读 api_config → 构造 LLM/Embedder）
message InitResponse {
  bool success = 1;
  string errormsg = 2;
}
```

- `NetMessageRequest` oneof 新增字段（如 `initRequest = 33`）；`NetMessageResponse` 新增 `initResponse = 10`。
- **协议修改流程（强制）**：改 `Tools/message.proto` → `1.genproto.cmd` → `MessageDispatch.cs` → Rebuild `CSharpClient.sln` → `2.copyprotocol.cmd`。**禁止**手改 `message_pb2.py` / `message.cs`。
- Unity 侧：`AgentService` 新增 `SendInit()` + `OnInit` 事件 + `MessageDistributer` 订阅 `InitResponse`；`AgentServiceAsyncExtensions` 新增 `InitAsync()`（模式同 `DeleteMemoryAsync`）。调用时机：Title 点「新游戏/继续游戏」校验通过后、`GameFlowManager.StartNewGame/ContinueGame` 之前（或作为 Flow 首个 Step）。
- Python 侧：`main.py` 新增 `@server.on_message(message_pb2.InitRequest)` handler，逻辑见 §3.3.2。

## 4. 实现步骤

1. 协议：`Tools/message.proto` 新增 `InitRequest`/`InitResponse`（§3.5）；按流程重新生成并分发（`1.genproto.cmd` → `MessageDispatch.cs` → Rebuild → `2.copyprotocol.cmd`）。
2. Python：新增 `config/api_config_loader.py`（含 `api_config_path`、`load_api_config_into_env`）。
3. Python：`main.py` 移除全局 `MemoryManager().initialize()`；新增 `InitRequest` handler（读 json → 注入 env → `initialize()` → 回 `InitResponse`）；支持 `--auto-init`。
4. Python：`agent_interuptible.py` 将 `model`/`llm_with_tools` 改延迟构造，`chatbot` 用 `get_llm_with_tools()`；删除废弃 `chain`。
5. Python：`memory_manager.py` `__init__` 不构造 LLM 对象，`LLMConfig`/`_compress_model` 延迟到 `initialize()` 内构造；`embedder_service.py` 确认时序（无需代码改动）。
6. Unity：新增 `JsonConfigIO.cs`（公共 JSON 读写）；新增 `ApiConfigStore.cs`（`Load`/`Save`/`IsComplete`）。
7. Unity：`AgentService.cs`/`AgentServiceAsyncExtensions.cs`/`MessageDispatch.cs` 接入 `InitRequest`/`InitResponse` 发送与事件。
8. Unity：修改 `UITitle.cs`（12 个 InputField 引用、回填、快照、变更检测、`OnConfigSaveConfig`/`OnCancelSaveConfig`、入口拦截 + 校验通过后发 `InitAsync`）。
9. Unity：场景/预制体绑定 InputField 与弹窗按钮。
10. 工具：`.gitignore` 新增 `api_config.json`。
11. 自测（见 §6），更新文档状态。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `agent_interuptible` 模块 import 时触发 `llm_with_tools` 构造（早于 env 注入） | 延迟构造 + `get_llm_with_tools()` 工厂；`tools` 保持模块级 |
| `chatbot` 节点引用模块级 `llm_with_tools` 未同步改 | 检索全部 `llm_with_tools` 引用点逐一替换；`chain` 废弃代码同步处理 |
| 打包版无 `.env` 时 Key 缺失导致初始化失败 | `load_api_config_into_env` 空键注入；缺 Key 时 `ChatOpenAI` 构造不报错（无网络请求），报错在首次调用；Title 完整性校验兜底 |
| 未发 init 信号直接调 Agent 推理 → 空 Key 静默失败 | `get_llm_with_tools()`/`MemoryManager` 增加「未初始化」显式报错；init handler 幂等 |
| `InitRequest` 重复到达 | `MemoryManager.initialize()` 已有 `_initialized` 短路；handler 幂等返回成功 |
| 协议生成流程遗漏（`message_pb2.py`/`message.cs` 手改） | 严格走 `Tools/message.proto` → 生成脚本 → `MessageDispatch.cs` → Rebuild 流程；禁止手改生成物 |
| `api_config.json` 路径在打包后变化 | 路径规则与 `AgentService.GetPort()`/`PORT_CONFIG_FILE` 一致（`dataPath` 上级 / `PROJECT_ROOT/../Data`），打包后按 §3.3 方案统一 |
| 明文 Key 入库 | `api_config.json` 加入 `.gitignore`；开发期自行生成的本地文件不入库 |
| `MemoryManager.__init__` 固化 Key / 模块级实例化早于 env 注入读到旧值或 None | `__init__` 不构造 LLM 对象，`LLMConfig`/`_compress_model` 延迟到 `initialize()`（晚于 env 注入）构造；模块级 `memory_manager = MemoryManager()` 实例化不再触发 LLM 构造 |
| 开发期手动起 Python 后不触发 init → 功能不可用 | 提供 `--auto-init` 参数等效 init 信号，兼容现有开发工作流 |

回退：本期改动涉及协议（`InitRequest`）+ Python 延迟初始化 + Unity UI。回滚 = 还原 `message.proto` 与生成物、还原 `main.py`/`agent_interuptible.py`/`memory_manager.py`/`embedder_service.py`、移除 `JsonConfigIO.cs`/`ApiConfigStore.cs`、还原 `UITitle.cs` 与场景绑定、移除 `.gitignore` 条目。协议字段按 §3.5 预留编号（`initRequest=33`/`initResponse=10`）可平滑回滚。

## 6. 测试建议

**Python 侧（可自测）**：
1. 无 Key 启动：不带 `api_config.json`、不带 `--auto-init` 运行 `main.py` → 正常建 Kuzu 库、监听端口、接受连接（不报错）。
2. 发 `InitRequest`（模拟 Unity）→ 日志确认 `load_api_config_into_env` 注入 + `MemoryManager.initialize()` 执行；`InitResponse.success=true`。再发 `AgentCreateRequest` 联调 Agent 推理/记忆，确认用 json 的 Key。
3. 未发 `InitRequest` 直接调 Agent 推理 → 报「未初始化」错（非空 Key 静默失败）。
4. 删除 `api_config.json` → `--auto-init` 运行 → 确认走 `.env`，现有行为不变。
5. 单测 `load_api_config_into_env`：json 缺字段、env 已有值不覆盖、幂等。
6. 单测 `get_llm_with_tools`：未调用前不构造（`_llm_with_tools is None`），调用后构造且参数来自 env。
7. 重复发 `InitRequest` → 幂等，第二次直接成功。

**Unity 侧（需编辑器/Play 模式）**：
1. 打开四个面板，确认文本框回填 `api_config.json` 值。
2. 修改任一文本框退出 → 弹 `MsgboxSaveConfig`；不改退出 → 不弹。
3. 点保存 → 检查 `api_config.json` 落盘内容；重开面板回填正确。
4. 12 项配置不全时点新/继续游戏 → 弹 `MsgboxNoApiKey` 且不进场景；补全后点进入 → 发 `InitRequest` → 正常进 GameFlow。

**验收标志**：`api_config.json` 存在时 Python 无 Key 启动、收到 `InitRequest` 后按其中 Key 正常推理/记忆检索；缺失时回退 `.env` 开发工作流不受影响；Title 四项 UI 交互全部符合 PRD §6。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-20 | 完成 Python 侧全部改造（api_config_loader / main.py 无 Key 启动 + InitRequest + --auto-init / agent_interuptible llm 延迟构造 / memory_manager 延迟初始化 / console_logger UTF-8），并通过 api_config_loader、get_llm_with_tools 单测与端到端 InitRequest 链路自测。 |
| 2026-08-20 | 完成 Unity 侧代码（JsonConfigIO.cs、ApiConfigStore.cs、AgentService/AgentServiceAsyncExtensions 接入 InitRequest/InitResponse、UITitle.cs 配置面板 + 入口拦截 + InitAsync）。 |
| 2026-08-20 | 完成 `.gitignore` 忽略 `api_config.json`；交付《场景绑定指引.md》供用户手动绑定 Title 场景/Prefab。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
