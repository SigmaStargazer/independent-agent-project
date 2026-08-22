# 技术方案 — v0.23.1 API Key 配置优化

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-08-22

---

## 1. 方案概述

分两条线实现：

1. **需求一（复制按钮）**：纯 Unity UI 侧。新增 `UILLMAgent` / `UILLMMemory` 两个脚本，各自提供 `OnClickCopy`，通过读写 `UISetting` 暴露的文本框访问方法，把另一组的 Base/Key/Model 复制到当前面板的 3 个文本框。**不自动保存**，沿用 ESC → `MsgboxSaveApiKey` 既有流程。

2. **需求二（测试后保存）**：跨 Unity/Python。在 `MsgboxSaveApiKey`（4 个模型配置 Panel 专用）点「测试后保存」（原「保存退出」改名）后，Unity **不写盘**，仅用文本框当前值发起「当前面板模型」的 API 可用性测试。测试走 Python：新增 `ApiTestRequest/ApiTestResponse` 协议，Unity 携带当前面板对应组的 `category/base/key/model`，Python 用 `config/api_tester.py` **临时构造**轻量探测客户端发最小请求（不初始化任何系统），把 `success/errormsg` 返回 Unity；Unity 据此切换 `MsgboxModelTesting` / `MsgboxModelAvailable` / `MsgboxModelUnavailable`。**保存动作推迟到测试通过后**：仅在 `MsgboxModelAvailable` 点「保存退出」时写盘（避免不可用配置覆盖原可用配置）。

核心架构原则（v0.23.0b 确立）：**LLM 能力统一收敛在 Python**；**Title 阶段零系统**——测试不触发任何系统初始化，只发一次轻量探测请求。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | `config/api_tester.py` | 新增：API 连通性测试模块（零系统，与 api_config_loader 同域） |
| Python | `network/message_pb2.py` | 重新生成（新增 ApiTestRequest/ApiTestResponse） |
| Python | `main.py` | 新增 `handle_api_test_request` |
| 协议 | `Tools/message.proto` | 新增 ApiTestRequest/ApiTestResponse + NetMessage 字段 |
| Unity | `Src/Lib/AgentProtocol/message.cs` | 重新生成 |
| Unity | `Src/Lib/Common/Network/MessageDispatch.cs` | 新增分发 |
| Unity | `AgentService.cs` | 新增 `OnApiTest` 事件 + `SendApiTest` + `OnApiTestResponse_` |
| Unity | `AgentServiceAsyncExtensions.cs` | 新增 `ApiTestAsync` |
| Unity | `UISetting.cs` | 「测试后保存」流程改造 + 暴露文本框读写方法 + 测试通过后保存 + 移除 SaveMsgFrom/UILevel |
| Unity | `UITitle.cs` | 新增 `MsgboxSaveApiKey` + 3 个结果 Msgbox 引用与切换逻辑 + 移除 SaveMsgFrom/UILevel |
| Unity | `UILLMAgent.cs` / `UILLMMemory.cs` | 新增：复制按钮逻辑 |
| Unity | 场景/Prefab | 挂载脚本、绑定 Msgbox（用户手动绑定） |

## 3. 详细设计

### 3.1 协议（Tools/message.proto）

```proto
// API 连通性测试：Title 阶段「测试后保存」后，测试当前面板模型的可用性（v0.23.1）
message ApiTestRequest {
    string category = 1;   // llm | embedding | rerank（当前面板对应测试类型）
    string api_base = 2;   // 该组 base url
    string api_key = 3;    // 该组 key
    string model = 4;      // 该组 model 名
}

message ApiTestResponse {
    bool success = 1;
    string errormsg = 2;
}
```

`NetMessageRequest` 增加 `ApiTestRequest apiTestRequest = 35;`，`NetMessageResponse` 增加 `ApiTestResponse apiTestResponse = 12;`。

协议修改流程按 `AGENTS.md §5.1`：改 `message.proto` → 运行 `1.genproto.cmd` → 手改 `MessageDispatch.cs` → Rebuild `CSharpClient.sln` → `2.copyprotocol.cmd` 部署到 Unity。

### 3.2 Python（Brain）— `config/api_tester.py`

新增独立测试模块，**不进** `EmbedderService` / `agent_interuptible` 的实例缓存，保证零系统：

```python
# config/api_tester.py
import asyncio
from langchain_core.messages import HumanMessage
from langchain_openai import ChatOpenAI
from graphiti_core.embedder.openai import OpenAIEmbedderConfig, OpenAIEmbedder
from graphiti_core.cross_encoder.openai_reranker_client import OpenAIRerankerClient
from graphiti_core.llm_client.config import LLMConfig

TEST_TIMEOUT = 30.0  # 秒

async def test_api_connectivity(category, api_base, api_key, model) -> tuple[bool, str]:
    """零系统探测：临时构造客户端发最小请求，验证 api 连通性。
    返回 (success, errormsg)。不初始化任何单例/系统。"""
    try:
        if category == "llm":
            llm = ChatOpenAI(
                model_name=model, openai_api_base=api_base,
                openai_api_key=api_key, streaming=False,
                request_timeout=TEST_TIMEOUT, max_retries=0,
            )
            resp = await asyncio.wait_for(
                llm.ainvoke([HumanMessage(content="ping")]),
                timeout=TEST_TIMEOUT,
            )
            # resp.content 非空即认为可用
            return (True, "") if (resp and getattr(resp, "content", None)) else (False, "模型未返回内容")
        elif category == "embedding":
            emb = OpenAIEmbedder(config=OpenAIEmbedderConfig(
                api_key=api_key, embedding_model=model, base_url=api_base,
            ))
            vecs = await asyncio.wait_for(emb.create(["ping"]), timeout=TEST_TIMEOUT)
            return (True, "") if vecs else (False, "embedding 未返回向量")
        elif category == "rerank":
            rer = OpenAIRerankerClient(config=LLMConfig(
                api_key=api_key, model=model, base_url=api_base,
            ))
            out = await asyncio.wait_for(rer.rank("ping", ["ping"]), timeout=TEST_TIMEOUT)
            return (True, "") if out is not None else (False, "rerank 未返回结果")
        else:
            return (False, f"未知测试类型: {category}")
    except asyncio.TimeoutError:
        return (False, f"测试超时（>{TEST_TIMEOUT}s）")
    except Exception as e:
        return (False, f"测试失败: {e}")
```

> 说明：`ChatOpenAI` 用 `request_timeout`（v0.21.0 hotfix 教训：`langchain_openai` 该字段名是 `request_timeout`，不是 `timeout`）；`OpenAIRerankerClient` 的探测接口是 `rank`（`SafeBatchOpenAIReranker.rerank` 的 `rerank` 是其 Safe 层自定义名，底层 `rank` 才是原始接口，本次实测确认）。

### 3.3 Python — `main.py`

新增 handler，只做转发，不碰任何系统：

```python
from config.api_tester import test_api_connectivity

@server.on_message(message_pb2.ApiTestRequest)
async def handle_api_test_request(msg, context):
    response = message_pb2.ApiTestResponse()
    try:
        success, errormsg = await test_api_connectivity(
            category=msg.category, api_base=msg.api_base,
            api_key=msg.api_key, model=msg.model,
        )
        response.success = success
        response.errormsg = errormsg
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
    await context['server'].send_message(response, context)
```

### 3.4 Unity — 协议接入

- `MessageDispatch.cs`：`NetMessageRequest` 分支加 `apiTestRequest`；`NetMessageResponse` 分支加 `apiTestResponse`。
- `AgentService.cs`：
  - `public event UnityAction<bool, string> OnApiTest;`
  - `Subscribe<ApiTestResponse>(this.OnApiTestResponse_)` / 对应 Unsubscribe
  - `SendApiTest(category, apiBase, apiKey, model)`：构造 `NetMessage` → `apiTestRequest`
  - `OnApiTestResponse_`：`OnApiTest?.Invoke(response.Success, response.Errormsg)`
  - `DisconnectNotify` 分支补 `apiTestRequest`（断线时失败回调）
- `AgentServiceAsyncExtensions.cs`：

```csharp
public static UniTask ApiTestAsync(string category, string apiBase, string apiKey, string model)
{
    var tcs = new UniTaskCompletionSource();
    void Handler(bool success, string reason)
    {
        AgentService.Instance.OnApiTest -= Handler;
        if (success)
            tcs.TrySetResult();
        else
            tcs.TrySetException(new Exception(reason));
    }
    AgentService.Instance.OnApiTest += Handler;
    AgentService.Instance.SendApiTest(category, apiBase, apiKey, model);
    return tcs.Task;
}
```

### 3.5 Unity — UISetting 改造

#### 3.5.1 暴露文本框读写方法（供 UILLMAgent/UILLMMemory 复制用）

```csharp
// 供复制按钮跨面板读写文本框：group 取值 agent | memory | embedding | reranker（配置组，区别于协议测试类型）
public string GetBase(string group);    // 读某组 Base 文本框
public string GetKey(string group);     // 读某组 Key 文本框
public string GetModel(string group);   // 读某组 Model 文本框
public void SetGroup(string group, string baseV, string keyV, string modelV);  // 覆盖某组 3 个文本框
```

#### 3.5.2 测试后保存流程改造

`OnConfirmTestConfig()`（Btn3「测试后保存」，原「保存退出」改名）：**不写盘**，仅用文本框当前值发测试。

```csharp
public async void OnConfirmTestConfig()
{
    // 1. 不保存到 api_config.json——用文本框当前值测试，避免不可用配置覆盖原可用配置。
    mCurrentConfig = CollectInputsToApiConfig();
    RefreshInputsFromConfig();

    // 2. 请求 UITitle 进入「测试中」状态（关 MsgboxSaveApiKey、开 ModelTesting、锁输入）
    OnStartApiTest?.Invoke(CurrentTestCategory());  // 新回调，UITitle 实现

    // 3. 发起测试（异步，await）
    bool ok;
    string errmsg;
    try
    {
        var (cat, baseV, keyV, modelV) = GetCurrentGroupConfig();
        await AgentServiceAsyncExtensions.ApiTestAsync(cat, baseV, keyV, modelV);
        ok = true; errmsg = "";
    }
    catch (Exception e)
    {
        ok = false; errmsg = e.Message;
    }

    // 4. 通知 UITitle 测试完成（关 ModelTesting、开 Available/Unavailable）
    OnApiTestFinished?.Invoke(ok, errmsg);
}
```

`OnConfirmSaveAfterTest()`（MsgboxModelAvailable 的「保存退出」）：**此刻才保存**并返回 PanelSetting。

```csharp
public void OnConfirmSaveAfterTest()
{
    mCurrentConfig = CollectInputsToApiConfig();   // 从文本框收集（仍为当前面板那组）
    ApiConfigStore.Save(mCurrentConfig);           // 测试通过后才写盘
    RefreshInputsFromConfig();
    OnRequestBackToSetting?.Invoke();               // 固定返回 PanelSetting（UITitle 切换）
}
```

> 简化说明：由于 `MsgboxSaveApiKey` / 结果 Msgbox 的返回目标固定为 PanelSetting，`UISetting` 不再持有 `SaveMsgFrom`/`UILevel`，也无需 `OnRequestBack(level)` 带参回调；改为无参 `OnRequestBackToSetting`（UITitle 注入，内部直接 `ShowSetting()`）。

`OnCancelExitConfig()`（Btn2「退出」）：**不保存** → 关 MsgboxSaveApiKey → 固定返回 PanelSetting。因 MsgboxSaveApiKey 固定从 4 个模型配置 Panel 弹出、目标固定为 PanelSetting，**不再需要** v0.23.0b 的 `SaveMsgFrom`/`UILevel` 来源记录逻辑。

#### 3.5.3 「取消测试」处理

`MsgboxModelTesting` 点「取消」时：Unity 需**丢弃进行中的异步结果**。用 `CancellationToken` 或在 `UISetting` 里用一个 `bool mTestCancelled` 标志：取消置 true，测试完成回调时若已取消则**不弹结果弹窗**，只关闭 Testing 并停留当前 Panel。

#### 3.5.4 新增回调

```csharp
public System.Action<string> OnStartApiTest;            // 参数：测试类型（llm/embedding/rerank）
public System.Action<bool, string> OnApiTestFinished;   // 参数：success, errormsg
public System.Action OnRequestBackToSetting;            // 固定返回 PanelSetting（UITitle 注入，内部 ShowSetting()）
```

辅助方法：
- `string CurrentTestCategory()`：当前面板对应测试类型（LLMAgent/LLMMemory → `llm`，Embedding → `embedding`，Reranker → `rerank`）。
- `(cat, baseV, keyV, modelV) GetCurrentGroupConfig()`：取当前面板那组文本框的值（LLMAgent → AGENT 组，LLMMemory → MEMORY 组，Embedding → EMBEDDING 组，Reranker → RERANKER 组）。测试类型 `cat` 即 `CurrentTestCategory()`。

### 3.6 Unity — UITitle 改造

- 新增引用：`mSaveApiKeyMsgBox`（4 个模型配置 Panel 退出专用）+ 3 个结果 Msgbox `mModelTestingMsgbox` / `mModelAvailableMsgbox` / `mModelUnavailableMsgbox`（`Awake` 中全部 `SetActive(false)`）。`MsgboxSaveSetting` 本版本不再使用（保留，供以后增加其他设置项时用）。
- 注入 `mSetting.OnStartApiTest` / `mSetting.OnApiTestFinished` 回调。
- `OnStartApiTest(category)`：关 `mSaveApiKeyMsgBox` → 开 `mModelTestingMsgbox` → `LockInput()`。
- `OnApiTestFinished(success, errmsg)`：
  - 关 `mModelTestingMsgbox`
  - success → 开 `mModelAvailableMsgbox`
  - 否则 → 开 `mModelUnavailableMsgbox`（可在弹窗上显示 `errmsg`）
- `MsgboxSaveApiKey` 按钮绑定（用户在场景中绑定到 `UITitle` 公开方法）：
  - `CloseSaveApiKeyMsgBox()`：Btn1 取消，关 Msgbox，停留当前 Panel
  - `OnExitSaveApiKey()`：Btn2 退出，**固定返回 PanelSetting**（不保存，`ShowSetting()`）
  - `OnConfirmTestApiKey()`：Btn3 测试后保存，调 `mSetting.OnConfirmTestConfig()`
- 结果 Msgbox 按钮绑定（由用户在场景中绑定到 `UITitle` 公开方法）：
  - `CloseModelTestingMsgBox()`：关 Testing，停留当前 Panel
  - `CloseAvailableContinue()`：关 Available，停留当前 Panel（继续配置）
  - `OnAvailableSaveExit()`：调 `mSetting.OnConfirmSaveAfterTest()`（保存配置 + 返回 PanelSetting）
  - `CloseUnavailableContinue()`：关 Unavailable，停留当前 Panel
  - `CloseUnavailableExit()`：关 Unavailable，返回 PanelSetting（不保存）

> 简化：`MsgboxSaveApiKey` / `MsgboxSaveSetting` 退出后返回目标**固定**（前者固定 PanelSetting，后者将来用于设置页），因此 v0.23.0b 中「记录弹窗来源层级」的 `SaveMsgFrom` / `UILevel` / `OnRequestBack(level)` 机制**全部移除**。

### 3.7 Unity — UILLMAgent / UILLMMemory

两个脚本结构对称，仅「源/目标」不同：

```csharp
// UILLMAgent.cs（挂 PanelLLMAgent）
public class UILLMAgent : MonoBehaviour
{
    [SerializeField] private UISetting mSetting;   // 场景中拖 UIConfig

    public void OnClickCopy()
    {
        if (mSetting == null) return;
        // 把 Memory 组配置复制到当前（Agent）面板
        mSetting.SetGroup("agent",
            mSetting.GetBase("memory"),
            mSetting.GetKey("memory"),
            mSetting.GetModel("memory"));
    }
}

// UILLMMemory.cs（挂 PanelLLMMemory）：对称，把 Agent 组复制到 Memory 面板
```

复制只改文本框，不自动保存（走 ESC 既有流程）。

## 4. 实现步骤

1. `message.proto` 新增 ApiTestRequest/ApiTestResponse → 跑 `1.genproto.cmd` 重新生成 py/cs。
2. 手改 `MessageDispatch.cs` 新增分发。
3. Python：新增 `config/api_tester.py`；`main.py` 新增 `handle_api_test_request`（`from config.api_tester import test_api_connectivity`）。
4. Python 自测（见 §6）：直接调 `test_api_connectivity` 各类型跑通。
5. Unity：`AgentService.cs` + `AgentServiceAsyncExtensions.cs` 接入 ApiTest。
6. Rebuild `CSharpClient.sln` → `2.copyprotocol.cmd` 部署到 Unity。
7. Unity：`UISetting.cs` 改造（暴露读写方法 + 测试后保存流程 + 取消标志 + 回调 + 移除 SaveMsgFrom/UILevel）。
8. Unity：`UITitle.cs` 新增 `MsgboxSaveApiKey` 与 3 个结果 Msgbox 引用、切换逻辑、移除来源记录。
9. Unity：新增 `UILLMAgent.cs` / `UILLMMemory.cs`。
10. 更新 `场景绑定指引.md`：列出用户手动绑定项（`MsgboxSaveApiKey` + 3 个结果 Msgbox 挂载/按钮、两个新脚本挂载与 mSetting 引用）。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| 测试超时过长影响体验 | 测试超时独立设 30s（比运行时的 120s 短），超时即判不可用并提示 |
| 取消测试与异步回调竞态 | 用 `mTestCancelled` 标志：取消后测试完成回调不弹结果弹窗，只关 Testing 停留当前 Panel |
| `langchain_openai`/Graphiti 接口变化 | 本次已实测 `request_timeout`、`OpenAIRerankerClient.rank`、`OpenAIEmbedder.create` 等签名（见 §3.2） |
| Unity 断线时测试请求无响应 | `DisconnectNotify` 补 `apiTestRequest` 分支：断线即失败回调 → 弹 Unavailable |
| 需求二改变了「保存退出即切面板」的既有行为 | 方案明确：点「测试后保存」不写盘、不切面板，改为测试；仅 Available 点「保存退出」（写盘+返回 PanelSetting）或 Unavailable 点「退出」（不写盘+返回 PanelSetting） |

## 6. 测试建议

**Python 自测（不启动 Unity）**：

- 直接用合法/非法配置调 `test_api_connectivity`，覆盖 `llm/embedding/rerank` 三种类型：
  - 合法 Key → 返回 `(True, "")`
  - 非法 Key / 错误 base / 不存在的 model → 返回 `(False, errmsg)`
  - 故意超时（如指向不可达地址）→ 返回 `(False, 超时)`
- 验证测试后各系统仍为零状态：`MemoryManager().is_initialized == False`、`EmbedderService().is_initialized == False`、无 Agent。

**Unity 联调**：

- 需求一：配好 Agent 面板 → 进 Memory 面板点复制 → 三个文本框被填充 → ESC 弹 `MsgboxSaveApiKey` → 保存后写盘正确。
- 需求二（4 种面板各验一次）：点「测试后保存」→ 弹 ModelTesting → 正常配置弹 Available（此时 `api_config.json` 未被写入）；改错 Key 弹 Unavailable（含错误原因，`api_config.json` 保持原值）。
- `MsgboxSaveApiKey` 按钮：Btn1 取消停留当前 Panel；Btn2 退出固定返回 PanelSetting（不保存）；Btn3 测试后保存开始测试。
- 测试通过后保存：在 Available 点「保存退出」→ 写盘并返回 PanelSetting；Unavailable 点「退出」→ 不写盘返回 PanelSetting。
- 取消测试：点「测试后保存」后在 ModelTesting 弹窗点「取消」→ 关弹窗停留当前面板，不弹结果。
- Available/Unavailable 的「继续配置/退出」按钮行为符合 PRD §4.3。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-21 | 生成方案（PRD/solution），待用户确认 |
| 2026-08-21 | 审核通过，开始开发。实现过程中确认：rerank 模型是配给 Graphiti 用的，用 OpenAIRerankerClient（LLM chat 二分类）测试该配置能否用于 Graphiti；rerank 类别保留（协议 category 仍为 llm \| embedding \| rerank）；新增 UILLMRerank 复制脚本（从 Agent 组拷贝）。 |
| 2026-08-21 | 开发收尾（Unity 脚本 + 构建部署 + 文档）。完成 UISetting/UITitle/UIMsgBox 改造、3 个复制脚本、Rebuild + 部署 DLL、Python 自测复跑、rerank 实测、新增场景绑定指引。详见 7.3。 |
| 2026-08-22 | 联调修复：Reranker 面板配 deepseek-v4-flash 测试失败（Reasoning 模型不支持 logprobs）。实测确认加 `extra_body={'thinking':{'type':'disabled'}}` 后可正常返回 logprobs。方案 A（用户确认）：`SafeBatchOpenAIReranker` override `rank` 统一关闭 thinking，`api_tester` rerank 改用同一封装类；`deepseek-v4-flash`/`qwen-turbo` 均可用，运行时 Graphiti 路径实测通过。详见 7.4。 |
| 2026-08-22 | 联调修复：Msgbox 点「继续配置」后 ESC 无法弹 MsgboxSaveApiKey。根因：`OnConfirmTestConfig` 把文本框值写进 `mCurrentConfig`（dirty 基准）导致 `HasConfigChanged()` 恒 false。修复：删除该赋值，测试值实时读取。详见 7.5。 |

### 7.1 当前开发进度（2026-08-21，对话中断时快照）

**已完成：**

1. **协议** `Tools/message.proto`：
   - 新增 `ApiTestRequest`（category/api_base/api_key/model）+ `ApiTestResponse`（success/errormsg）。
   - `NetMessageRequest.apiTestRequest = 35;`、`NetMessageResponse.apiTestResponse = 12;`。
   - 已跑 `1.genproto.cmd` 重新生成 `network/message_pb2.py` 与 `Src/Lib/AgentProtocol/message.cs`。
2. **分发** `Src/Lib/Common/Network/MessageDispatch.cs`：
   - `NetMessageResponse` 分支已加 `apiTestResponse` 分发。
   - 注：`apiTestRequest` 是 Unity→Python 方向，Python 端 handler 直接按字段名注册，无需在 C# 分发里加 Request 分支（C# 只收 Response）。
3. **Python** `config/api_tester.py`（新增）：`test_api_connectivity(category, api_base, api_key, model)` 零系统探测。
   - `llm`：`ChatOpenAI.ainvoke(["ping"])`，超时 30s。
   - `embedding`：`OpenAIEmbedder(config).create(["ping"])`。
   - `rerank`：`OpenAIRerankerClient(LLMConfig(...)).rank("ping", ["ping"])`（LLM 二分类，与 Graphiti 运行时一致）。
   - 未知类型返回 `(False, 未知测试类型)`。
4. **Python** `main.py`：
   - `from config.api_tester import test_api_connectivity`。
   - 新增 `handle_api_test_request`（`@server.on_message(message_pb2.ApiTestRequest)`），只做转发。
5. **Python 自测**（`test_api_tester_selftest.py`，临时文件，验收后可删）已通过：
   - llm 合法 → `(True, "")`；embedding 合法 → `(True, "")`；非法 Key → `(False, 401 AuthenticationError)`；未知类型 → `(False, ...)`。
   - rerank 已单独实测：`qwen-turbo`（兼容 Graphiti chat 二分类）→ `(True, "")`；`gte-rerank-v2`（专用 rerank 模型，无法用于 Graphiti）→ `(False, ...)` 如实反映。
   - 零系统验证通过：测试后 `MemoryManager().is_initialized == False`、`EmbedderService().is_initialized == False`、Agent 数 == 0。
6. **Unity** `AgentService.cs`（协议接入完成）：
   - 新增 `public event UnityAction<bool, string> OnApiTest;`。
   - Subscribe/Unsubscribe `ApiTestResponse → OnApiTestResponse_`。
   - `DisconnectNotify` 补 `apiTestRequest` 分支（断线 → OnApiTest(false, error)）。
   - 新增 `SendApiTest(category, apiBase, apiKey, model)`。
   - 新增 `OnApiTestResponse_`（Invoke OnApiTest）。
7. **Unity** `AgentServiceAsyncExtensions.cs`（完成）：新增 `ApiTestAsync(category, apiBase, apiKey, model)`（UniTask，失败抛 Exception）。

**待完成（已在本次对话完成，见 7.3）：**

- [x] 构建：Rebuild `CSharpClient.sln`（AgentProtocol + Common）→ 跑 `Tools/2.copyprotocol.cmd` 部署 DLL 到 Unity `Assets/References`。
- [x] Unity `UISetting.cs` 改造（§3.5）。
- [x] Unity `UITitle.cs` 改造（§3.6）。
- [x] Unity 新增复制脚本（§3.7）：`UILLMAgent.cs` / `UILLMMemory.cs` / `UILLMRerank.cs`。
- [ ] 场景绑定（用户手动）：挂 `MsgboxSaveApiKey` + 3 结果 Msgbox、绑定按钮到 UITitle 公开方法、挂 3 个复制脚本并拖 `mSetting`（详见 `场景绑定指引.md`）。
- [ ] 更新文档状态：PRD/solution 待确认→已确认→已实现；更新 `场景绑定指引.md`。

### 7.2 实现中确认的关键决策（重开对话务必遵守）

1. **rerank 类别保留**（不删）：协议 `category` 仍为 `llm | embedding | rerank`。`PanelReranker` 用 `OpenAIRerankerClient`（LLM chat 二分类）测试配置能否用于 Graphiti。
2. **复制脚本共 3 个**：`UILLMAgent`/`UILLMMemory`/`UILLMRerank`（Reranker 从 Agent 组拷贝）。
3. **测试后保存流程**（PRD §4.2）：`MsgboxSaveApiKey.Btn3「测试后保存」` 不写盘 → 测试 → Available「保存退出」才写盘；Unavailable「退出」不写盘。
4. 返回目标固定 PanelSetting → 移除 `SaveMsgFrom`/`UILevel`。
5. `message.proto` 当前状态：字段与注释均为 `llm | embedding | rerank`（未重新生成，因只改注释不影响字段结构；但**注意**：上一轮对话曾有"改注释后未重新生成"的中间状态，重开对话后先确认 message.proto 与 message_pb2.py/message.cs 一致即可）。

### 7.3 实现记录（2026-08-21 本次对话完成，开发收尾）

**已完成（Unity 脚本 + 构建部署 + 文档）：**

1. **Unity `UISetting.cs` 改造（§3.5 全部落地）**：
   - 暴露文本框读写：`GetBase/GetKey/GetModel(group)`、`SetGroup(group, baseV, keyV, modelV)`（group: `agent|memory|embedding|reranker`）。
   - 测试后保存：`OnConfirmTestConfig()`（Btn3，不写盘，用文本框当前值测试）→ `OnStartApiTest?.Invoke(CurrentTestCategory())` → `await AgentServiceAsyncExtensions.ApiTestAsync(...)` → `OnApiTestFinished?.Invoke(ok, errmsg)`。
   - `OnConfirmSaveAfterTest()`（MsgboxModelAvailable「保存退出」，此刻才 `ApiConfigStore.Save` + `OnRequestBackToSetting`）。
   - `OnExitToSetting()`（Btn2 退出，不保存，固定返回 PanelSetting）。
   - 取消测试：`CancelApiTest()` 置 `mTestCancelled` 标志，测试完成回调若已取消则不弹结果弹窗。
   - 新增回调：`Action<string> OnStartApiTest`、`Action<bool,string> OnApiTestFinished`、`Action OnRequestBackToSetting`。
   - `CurrentTestCategory()`（按当前激活子面板判断：LLMAgent/LLMMemory→`llm`，Embedding→`embedding`，Reranker→`rerank`）、`GetCurrentGroupConfig()`（返回当前面板那组文本框值）。
   - **移除** `SaveMsgFrom`/`UILevel`/`OnRequestBack(level)`/`CloseAndRequestBack`（固定返回 PanelSetting，无需来源记录）。
2. **Unity `UITitle.cs` 改造（§3.6 全部落地）**：
   - 新增引用：`mSaveApiKeyMsgBox` + 3 个结果 Msgbox（`mModelTestingMsgbox`/`mModelAvailableMsgbox`/`mModelUnavailableMsgbox`，类型 `UIMsgBox`），Awake 全部 `SetActive(false)`。
   - `MsgboxSaveSetting`（`mSaveSettingMsgBox`）本版不再使用（保留字段备用）。
   - 注入回调：`mSetting.OnStartApiTest` / `OnApiTestFinished` / `OnRequestBackToSetting = ShowSetting`（Awake）。
   - `OnStartApiTest(category)`：关 SaveApiKey → 开 ModelTesting → LockInput。
   - `OnApiTestFinished(success, errmsg)`：关 ModelTesting → success 开 Available / 否则 `Unavailable.SetText("模型不可用：\n"+errmsg)` 并开 Unavailable。
   - `MsgboxSaveApiKey` 按钮：`CloseSaveApiKeyMsgBox()`（Btn1 取消）、`OnExitSaveApiKey()`（Btn2 退出）、`OnConfirmTestApiKey()`（Btn3 → mSetting.OnConfirmTestConfig()）。
   - 结果 Msgbox 按钮：`CloseModelTestingMsgBox()`（调 mSetting.CancelApiTest + 关弹窗）、`CloseAvailableContinue()`、`OnAvailableSaveExit()`（→ mSetting.OnConfirmSaveAfterTest()）、`CloseUnavailableContinue()`、`CloseUnavailableExit()`（→ mSetting.OnExitToSetting()）。
   - **移除** `SaveMsgFrom`/`UILevel`/`OnRequestBackFromSaveMsg(level)` 机制。
3. **Unity `UIMsgBox.cs`**：新增 `SetText(string)` 公开方法（结果弹窗动态显示失败原因）。
4. **Unity 复制脚本（§3.7 全部落地，3 个）**：
   - `UILLMAgent.cs`（挂 PanelLLMAgent）：`OnClickCopy` 从 memory 组拷贝到 agent 组。
   - `UILLMMemory.cs`（挂 PanelLLMMemory）：`OnClickCopy` 从 agent 组拷贝到 memory 组。
   - `UILLMRerank.cs`（挂 PanelReranker）：`OnClickCopy` 从 agent 组拷贝到 reranker 组（Graphiti 用 LLM 做 rerank，常与 Agent 同模型）。
   - 均用 `mSetting.GetBase/GetKey/GetModel + SetGroup`，只改文本框**不自动保存**。
5. **构建部署**：Rebuild `CSharpClient.sln`（`Protocol.dll`/`Common.dll` 成功构建）→ 跑 `Tools/2.copyprotocol.cmd` 部署到 Unity `Assets/References`（两 DLL 时间戳已更新，含 `ApiTestRequest/ApiTestResponse`）。
6. **Python 自测复跑通过**（`test_api_tester_selftest.py`）：llm/embedding 合法 → `(True,"")`；非法 Key → `(False, 401 AuthenticationError)`；未知类型 → `(False,...)`；零系统验证通过（MemoryManager/EmbedderService 未初始化、Agent 数 0）。
7. **rerank 实测记录（本次复跑）**：用 `AGENT_MODEL=deepseek-v4-flash`（Reasoning 模型）测 rerank → `(False, "Reasoning model does not support n > 1...")` 如实反映（该模型配到 Reranker 面板后 Graphiti 运行时也会失败）。`RERANKER_MODEL=gte-rerank-v2`（专用 rerank 模型）同样不可用于 Graphiti。用兼容模型 `qwen-turbo` 时返回 `(True,"")`（此前实测）。**符合设计意图**：rerank 测试如实反映配置能否用于 Graphiti。
8. **文档**：新增 `DevDocs/v0.23.1/场景绑定指引.md`；更新本 solution 状态。

**待用户完成：**
- [ ] Unity 编辑器场景绑定（按 `DevDocs/v0.23.1/场景绑定指引.md`）：挂 `MsgboxSaveApiKey` + 3 结果 Msgbox（挂 `UIMsgBox`）、绑定按钮到 UITitle 公开方法、挂 3 个复制脚本并拖 `mSetting`。
- [ ] Unity 联调验收（见 `场景绑定指引.md` §5）。

### 7.4 联调修复记录（2026-08-22，Reranker 测试失败：Reasoning 模型不支持 logprobs）

**现象**：Unity Reranker 面板点「测试后保存」（配 `deepseek-v4-flash`，经 `UILLMRerank` 从 Agent 组复制）→ Python 返回 `(False, "Reasoning model does not support n > 1, logit_bias, logprobs, top_logprobs")`。

**根因**：Graphiti 用 LLM chat 二分类（logprobs）做 rerank（`OpenAIRerankerClient.rank`），而 DeepSeek V4 系默认开启 thinking（Reasoning 模式）不支持 `logprobs/logit_bias/top_logprobs` → 400。这是运行时固有行为，测试如实反映。

**验证**（实测通过）：
- `deepseek-v4-flash` 加 `extra_body={'thinking': {'type':'disabled'}}` 后 `logprobs` 正常返回（top token + logprob）。
- 该字段对不支持它的模型（`qwen-turbo`）会被安全忽略，不影响兼容模型。
- `gte-rerank-v2` 是 DashScope 专用 rerank 模型（走独立协议，非 chat），无论如何无法用于 Graphiti LLM 二分类。

**修复（方案 A，用户 2026-08-22 确认）**：
1. `memory_system/embedder/safe_batch_reranker.py`：`SafeBatchOpenAIReranker` 新增 override `rank(query, passages)`（与 Graphiti 运行时 `cross_encoder.rank` 入口一致），唯一差异：`chat.completions.create` 加 `extra_body={'thinking': {'type':'disabled'}}`。实现复制自基类 `OpenAIRerankerClient.rank`，升级 graphiti_core 时需同步。
2. `config/api_tester.py`：rerank 分支由直接 `OpenAIRerankerClient` 改为 `SafeBatchOpenAIReranker`（与运行时同一封装类，测试/运行时行为一致）。

**自测结果**：
- `deepseek-v4-flash` rerank → `(True,"")` ✅
- `qwen-turbo` rerank → `(True,"")` ✅（不回归）
- 运行时路径（`EmbedderService` → `SafeBatchOpenAIReranker.rank`）用 deepseek-v4-flash → 正确打分 `[('小明在客厅', 0.998), ('桌子是木头的', 0.0008)]` ✅
- 完整自测脚本无回归；零系统保持 ✅

**结论**：Reranker 面板现可直接配 `deepseek-v4-flash`（或任何支持关闭 thinking 的 chat 模型）正常使用；`gte-rerank-v2` 仍不适用（协议不兼容，非本次范围）。

### 7.5 联调修复记录（2026-08-22，继续配置后 ESC 无法弹出 MsgboxSaveApiKey）

**现象**：在任一 Msgbox（ModelTesting/ModelAvailable/ModelUnavailable）点「继续配置」回到配置 Panel 后，按 ESC 直接退回 PanelSetting，无法弹出 `MsgboxSaveApiKey` 保存。

**根因**：`UISetting.OnConfirmTestConfig()`（MsgboxSaveApiKey.Btn3「测试后保存」）里有 `mCurrentConfig = CollectInputsToApiConfig();`——把文本框当前值写进了 dirty 检测基准 `mCurrentConfig`。此后点「继续配置」回到 Panel，`HasConfigChanged()` 用文本框值对比 `mCurrentConfig`（已等于文本框值）→ 返回 `false` → ESC 走 `ShowSetting()` 直接回 PanelSetting，不弹 `MsgboxSaveApiKey`。

**修复（单行删除）**：`OnConfirmTestConfig()` 删除 `mCurrentConfig = CollectInputsToApiConfig();`。测试值由 `GetCurrentGroupConfig()` 从文本框**实时读取**（原本就如此），无需写 `mCurrentConfig`；`OnConfirmSaveAfterTest()` 保存时会自己重新 `CollectInputsToApiConfig()`。删除后 `mCurrentConfig` 保持为文件原值，dirty 检测恢复正常。

**边界验证**：
- 点「测试后保存」→ 通过 →「继续配置」→ ESC → 文本框 ≠ 文件旧值 → 弹 `MsgboxSaveApiKey` ✅
- 点「测试后保存」→ 通过 →「保存退出」→ `OnConfirmSaveAfterTest` 保存 + `RefreshInputsFromConfig` 重置基准 ✅
- 测试中「取消」→ 停留 Panel，dirty 检测不受影响 ✅

---
