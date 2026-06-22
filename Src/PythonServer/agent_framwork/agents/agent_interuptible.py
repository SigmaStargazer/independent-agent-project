# from ast import Str
import asyncio
import uuid
import os
import time
import json
import random
from datetime import datetime

from langchain_core.prompts import ChatPromptTemplate, MessagesPlaceholder
from langchain_openai import ChatOpenAI
from langchain_core.output_parsers import StrOutputParser
from langchain_core.messages import AIMessage, HumanMessage
from langchain_core.runnables import RunnablePassthrough

from typing import Annotated, Literal
from typing_extensions import TypedDict

from langgraph.graph import StateGraph, START, END
from langgraph.graph.message import add_messages
from langgraph.prebuilt import ToolNode

# from contextlib import ExitStack
from langgraph.checkpoint.memory import MemorySaver

from memory_system.memory_manager import MemoryManager
from agent_framwork.base.timed_message import TimedMessage
from network.servers import AgentServerNetMessage, TOOL_WAITERS
from network import message_pb2

# from graphiti_core.nodes import EpisodeType
# from graphiti_core.search import search_config_recipes

from agent_framwork.tools import base_tools
from agent_framwork.systems.time_system import TimeSystem
from agent_framwork.utils.prompt_utils import (
    trim_messages_by_token,
    estimate_system_prompt_tokens,
    get_tools_token_count,
)
from agent.tools import SKILL_TOOLS

from tools.perf_tool import aperf_print

from dotenv import load_dotenv
load_dotenv()

# ===== Prompt 保存与上下文裁剪配置 =====
PROMPT_SAVE_ENABLED = os.getenv("PROMPT_SAVE_ENABLED", "false").lower() == "true"
PROMPT_SAVE_DIR = os.path.normpath(os.path.join(
    os.path.dirname(__file__), "..", "..",
    os.getenv("PROMPT_SAVE_DIR", "logs/prompts")
))

IDLE_WAKEUP_CONFIG_PATH = os.path.normpath(os.path.join(
    os.path.dirname(__file__), "..", "..",
    "config", "idle_wakeup.json"
))
DEFAULT_IDLE_WAKEUP_CONFIG = {
    "enabled": True,
    "delay_min_seconds": 120,
    "delay_max_seconds": 300,
    "summary_max_events": 3,
    "summary_timeout_seconds": 5,
    "ignore_self_events": True,
    "message_template": "你已经空闲了一段时间，可以稍微留意一下周围。\n{world_event_summary}\n如果没有值得行动或交流的事情，可以继续保持当前状态。"
}


def load_idle_wakeup_config() -> dict:
    config = DEFAULT_IDLE_WAKEUP_CONFIG.copy()
    try:
        with open(IDLE_WAKEUP_CONFIG_PATH, "r", encoding="utf-8") as f:
            loaded = json.load(f)
        if isinstance(loaded, dict):
            config.update(loaded)
    except FileNotFoundError:
        print(f"[IdleWakeup] 配置文件不存在，使用默认配置: {IDLE_WAKEUP_CONFIG_PATH}")
    except Exception as e:
        print(f"[IdleWakeup] 读取配置失败，使用默认配置: {e}")

    min_delay = float(config.get("delay_min_seconds", config.get("min_delay_seconds", 120)))
    max_delay = float(config.get("delay_max_seconds", config.get("max_delay_seconds", 300)))
    if min_delay <= 0:
        min_delay = 120
    if max_delay < min_delay:
        max_delay = min_delay

    summary_max_events = int(config.get("summary_max_events", config.get("max_world_events", 3)))
    summary_timeout = float(config.get("summary_timeout_seconds", config.get("rpc_timeout_seconds", 5)))

    config["enabled"] = bool(config.get("enabled", True))
    config["min_delay_seconds"] = min_delay
    config["max_delay_seconds"] = max_delay
    config["max_world_events"] = max(0, summary_max_events)
    config["ignore_self_events"] = bool(config.get("ignore_self_events", True))
    config["rpc_timeout_seconds"] = max(1.0, summary_timeout)
    config["message_template"] = str(config.get("message_template") or DEFAULT_IDLE_WAKEUP_CONFIG["message_template"])
    return config


IDLE_WAKEUP_CONFIG = load_idle_wakeup_config()

# # 千问
# model_api_base = "https://dashscope.aliyuncs.com/compatible-mode/v1"
# model_api_key = "sk-7a3958e0fdf840e49a2edd83b25dd228"
# model_name = "qwen-max"

# # moonshot
# model_api_base = "https://api.moonshot.cn/v1"
# model_api_key = "sk-0cYUM2FsdWqmyJeth1He0FXlCVlcxScjNb3YPYHjl78vyEgY"
# model_name = "kimi-k2-0711-preview"

model_api_base = os.getenv("AGENT_API_BASE")
model_api_key = os.getenv("AGENT_API_KEY")
model_name = os.getenv("AGENT_MODEL")
model_timeout = float(os.getenv("AGENT_LLM_TIMEOUT", "120"))
model_max_retries = int(os.getenv("AGENT_LLM_MAX_RETRIES", "1"))

model = ChatOpenAI(
        model_name = model_name,
        openai_api_base = model_api_base,
        openai_api_key = model_api_key,
        streaming = False,
        verbose = True,
        request_timeout = model_timeout,
        max_retries = model_max_retries,
    )
output_parser = StrOutputParser()

# 生产工具列表
tools = [
        # base_tools.communicate_to_agent, 
        base_tools.communicate_to_user,
        #  base_tools.get_agent_list, 
        base_tools.get_cur_time,
        base_tools.set_focus_state,
        base_tools.search_fact_memories,
        base_tools.search_episode_memories,
        base_tools.stop_action_cmd,
        base_tools.observe_cmd,
        base_tools.monitor_target_cmd,
        base_tools.get_monitor_records_cmd,
        base_tools.get_world_event_log_cmd,
        base_tools.move_cmd,
        base_tools.follow_target_cmd,
        base_tools.interact_cmd,
        base_tools.select_cmd,
        base_tools.input_cmd,
        base_tools.plan_action_sequence_cmd,
        base_tools.start_action_sequence_cmd,
        base_tools.continue_action_sequence_cmd,
        # base_tools.stop_action_sequence_cmd,
        base_tools.set_timer_cmd,
        base_tools.get_timer_list_cmd,
        base_tools.remove_timer_cmd,
        # base_tools.add_alarm,
        #  base_tools.get_alarm_list,
        #  base_tools.remove_alarm
        ] + SKILL_TOOLS

# # # 测试工具列表
# tools = [
#     base_tools.get_cur_time,
#     base_tools.set_focus_state,
#     base_tools.search_fact_memories,
#     base_tools.search_episode_memories
# ]
         
llm_with_tools = model.bind_tools(tools)

MAX_CONTEXT_SIZE = 20

def _filter_messages(messages, k=20):
        """
        用于删减上下文长度
        """
        messages = messages[-k:]
        return messages

# 提示词模板
# system_template = """你扮演的角色名叫{name}，{description}。
# 向你发送信息的是系统管理员，它会不断告诉你关于周遭的情况。你需要根据情况做出相应的反应
# 规则：
# 1）仅当你使用了工具时，才会对外界产生实际影响。
# 2）你的直接回复只作为你的心理活动，不会被任何人看到。
# 因此如果你需要与用户、agent等具体某一对象进行交流，也请使用相应的工具，避免你想要传递的信息无法传达给对方。

# # 现在时间:
# {curtime}
# # 你刚联想到:
# {mem_fact}

# {mem_episode}
# """
system_template = """{mem_summary}

<现在时间>
{curtime}
</现在时间>

<回想>
{mem_fact}

{mem_episode}
</回想>

<动作技能记忆>
{mem_skill_index}

以上是当前可能用到的动作序列模板。如果其中某个模板与当前场景匹配，
把参数（如占位符、对象序号、距离阈值等）替换为当前场景的实际值，
直接调用 plan_action_sequence 一次性执行，无需先 load_action_skill。
如果不太匹配或想知道你掌握的全部技能，可以用 list_action_skills 查阅完整列表，
用 load_action_skill 拉取某个技能的所有模板。
</动作技能记忆>

<规则>
你会不断从周围环境获取信息，你需要自主决定是否需要做出响应、下一步行动是什么，但请注意：
1）你的直接回复不会被任何人看到，也不会对外界产生任何影响，只有你自己能看见！只作为你的心理活动。
2）仅当你使用了工具时，才会对外界产生实际影响。
由1）、2）可知，如需与任一对象进行交流，务必使用communicate系列工具来向指定对象发送消息，避免你想要传递的信息无法传达给对方。
</规则>
"""


prompt_template = ChatPromptTemplate.from_messages(
    [
        ("system", system_template),
        MessagesPlaceholder(variable_name="messages")
    ]
)
# 初始化chain(废弃)
chain = (
    RunnablePassthrough.assign(messages=lambda x: _filter_messages(x["messages"], k = MAX_CONTEXT_SIZE)) 
    | prompt_template 
    | llm_with_tools
    )

# 记忆管理器
memory_manager = MemoryManager()

# 定义状态
class State(TypedDict):
    name: str # agent名称
    index: int # agent id
    messages: Annotated[list, add_messages] # agent上下文
    # group_id: str # graphiti的group_id，用于分区
    mem_summary: str # 检索到的agent简介
    mem_fact: str # 检索到的事实记忆
    mem_episode: str # 检索到的情景记忆
    mem_skill_index: str # 动作技能记忆索引（RAG 注入 system prompt）
    mem_to_save: str # 待存储的记忆
    logged_tool_call_ids: list[str] # 用于记忆工具调用时，去重tool_message中重试的部分

# 定义节点
async def search_memory(state: State):
    """
    根据用户问题，并发检索事实记忆、情景记忆、动作技能记忆索引
    """
    name = state['name']
    query = state['messages'][-1].content
    # group_id = state['group_id']
    limit = 1 # 检索的记忆数量
    skill_top_n = int(os.getenv("SKILL_INDEX_TOP_N", "3"))

    await aperf_print(f"[{name}]加载agent简介开始")
    mem_summary = await memory_manager.load_agent_summary(name=state['name'])
    await aperf_print(f"[{name}]加载agent简介完成")

    # 并发：事实记忆 + 情景记忆 + 动作技能索引
    group_id = state['name'].encode('utf-8').hex()

    async def _fact():
        await aperf_print(f"[{name}]rag事实记忆开始")
        r = await memory_manager.search_fact_memory(name=state['name'], query=query, limit=limit)
        await aperf_print(f"[{name}]rag事实记忆完成")
        return r

    async def _episode():
        await aperf_print(f"[{name}]rag情景记忆开始")
        r = await memory_manager.search_episode_memory(name=state['name'], query=query, limit=limit)
        await aperf_print(f"[{name}]rag情景记忆完成")
        return r

    async def _skill_index():
        await aperf_print(f"[{name}]rag动作技能索引开始")
        try:
            r = await MemoryManager().action_skill.get_skill_index(
                group_id=group_id, query=query, top_n=skill_top_n
            )
        except Exception as e:
            print(f"[{name}] 动作技能索引检索失败：{e}")
            r = ""
        await aperf_print(f"[{name}]rag动作技能索引完成")
        return r

    mem_fact, mem_episode, mem_skill_index = await asyncio.gather(
        _fact(), _episode(), _skill_index()
    )

    # 需要存储接收到的用户信息
    await aperf_print(f"[{name}]缓存输入记忆开始")
    cur_time = await TimeSystem().aget_current_time(to_str = True)
    mem_to_save = query
    mem_to_save += f"\n[{cur_time}]我开始注意到上述信息"
    await aperf_print(f"[{name}]缓存输入记忆完成")

    # print(mem_to_save) # 测试
    return {
        "mem_summary": mem_summary,
        "mem_fact": mem_fact,
        "mem_episode": mem_episode,
        "mem_skill_index": mem_skill_index,
        "mem_to_save": mem_to_save
    }

async def chatbot(state: State):
    name = state['name']
    mem_to_save = state['mem_to_save']

    cur_time = await TimeSystem().aget_current_time()

    # 动态上下文裁剪
    system_vars = {
        "name": state['name'],
        "curtime": cur_time,
        "mem_summary": state['mem_summary'],
        "mem_fact": state['mem_fact'],
        "mem_episode": state['mem_episode'],
        "mem_skill_index": state.get('mem_skill_index', '') or "（暂无掌握的技能）"
    }
    system_tokens = await estimate_system_prompt_tokens(prompt_template, system_vars)
    tools_tokens = get_tools_token_count(tools)
    trimmed_messages = trim_messages_by_token(
        messages=state['messages'],
        system_prompt_tokens=system_tokens,
        tools_token_count=tools_tokens,
    )

    prompt = await prompt_template.ainvoke({
        "messages": trimmed_messages,
        **system_vars
    })

    # [开发模式] 终端打印 prompt
    if PROMPT_SAVE_ENABLED:
        print(f"====prompt开始====") 
        for message in prompt.messages:
            message.pretty_print()
        print(f"====prompt结束====") 

    await aperf_print(f"[{name}]模型输出开始")
    response = await llm_with_tools.ainvoke(prompt)
    print(response.content)
    await aperf_print(f"[{name}]模型输出完成")
    

    # 需要存储接收到的用户信息
    if response.content.strip(): # 工具调用时，response.content为空
        await aperf_print(f"[{name}]缓存模型输出记忆开始")
        cur_time = await TimeSystem().aget_current_time(to_str = True)
        mem_to_save += "\n" + f"[{cur_time}]我心想: {response.content}"
        await aperf_print(f"[{name}]缓存模型输出记忆完成")
        # print(mem_to_save)
    
    return {
        "messages": [response], 
        "mem_to_save": mem_to_save
    }

async def cache_tool_mem(state: State):
    """
    缓存工具记忆
    """
    messages = state["messages"]
    mem_to_save = state.get("mem_to_save", "")
    logged_ids = set(state.get("logged_tool_call_ids", []))

    # 找到最后一条AI Message（即tool call的内容）
    last_ai_message = None
    for msg in reversed(messages):
        if isinstance(msg, AIMessage):
            last_ai_message = msg
            break

    if not last_ai_message or not last_ai_message.tool_calls:
        return {} # 找不到tool call就无需进行任何更新

    new_entries = []
    new_ids = []

    for tool_call in last_ai_message.tool_calls:
        tid = tool_call["id"]
        if tid not in logged_ids:
            cur_time = await TimeSystem().aget_current_time(to_str = True)
            new_entries.append(f"[{cur_time}]我使用了 {tool_call['name']}，输入为 {tool_call['args']}")
            new_ids.append(tid)

    if new_entries:
        mem_to_save += "\n" + "\n".join(new_entries)
        logged_ids.update(new_ids)

    return {
        "mem_to_save": mem_to_save,
        "logged_tool_call_ids": list(logged_ids)  # 注意：set 不能直接存，需转 list 或保持为 set（取决于 state schema）
    }

async def save_memory(state: State):
    """
    将所有缓存记忆存入graphiti
    """
    name = state['name']
    mem_to_save = state['mem_to_save']
    
    await aperf_print(f"[{name}]存储记忆开始")
    curtime = await TimeSystem().aget_current_time()
    await memory_manager.save_memory(name=state['name'], memory=mem_to_save, curtime=curtime)
    await aperf_print(f"[{name}]存储记忆任务启动，后台进行中")

    # Prompt 保存：用全量 messages 重新渲染完整 prompt 并写入文件
    if PROMPT_SAVE_ENABLED:
        await _save_prompt_log(state, curtime)

    return {
        "mem_to_save": "",
        "logged_tool_call_ids": []
        }


async def _save_prompt_log(state: State, curtime):
    """将本轮完整 prompt 写入文件"""
    name = state['name']

    # 用全量 state['messages'] 重新渲染完整 prompt
    cur_time_str = await TimeSystem().aget_current_time()
    prompt = await prompt_template.ainvoke({
        "messages": state['messages'],
        "name": state['name'],
        "curtime": cur_time_str,
        "mem_summary": state['mem_summary'],
        "mem_fact": state['mem_fact'],
        "mem_episode": state['mem_episode'],
        "mem_skill_index": state.get('mem_skill_index', '') or "（暂无掌握的技能）"
    })

    # 拼接 pretty_repr 文本
    content = ""
    for msg in prompt.messages:
        content += msg.pretty_repr() + "\n\n"

    # 使用实际时间作为文件名
    now = datetime.now()
    filename = now.strftime("%Y-%m-%d_%H-%M-%S")

    save_dir = os.path.join(PROMPT_SAVE_DIR, name)
    os.makedirs(save_dir, exist_ok=True)

    # 同一秒内多次推理时追加毫秒后缀
    filepath = os.path.join(save_dir, filename + ".log")
    if os.path.exists(filepath):
        filename += f"_{now.microsecond // 1000:03d}"
        filepath = os.path.join(save_dir, filename + ".log")

    with open(filepath, "w", encoding="utf-8") as f:
        f.write(content)

# 条件
def route_chatbot(state: State) -> Literal["tools", "save_memory"]:
    messages = state["messages"]
    if not messages:
        raise ValueError("No messages in state")
    
    last_message = messages[-1]
    
    if isinstance(last_message, AIMessage) and last_message.tool_calls:
        return "tools"
    else:
        return "save_memory"

# 创建graph
graph_builder = StateGraph(State)

graph_builder.add_node("search_memory", search_memory)
graph_builder.add_node("chatbot", chatbot)
tool_node = ToolNode(tools=tools)
graph_builder.add_node("tools", tool_node)
graph_builder.add_node("cache_tool_mem", cache_tool_mem)
graph_builder.add_node("save_memory", save_memory)

graph_builder.add_edge(START, "search_memory")
graph_builder.add_edge("search_memory", "chatbot")
graph_builder.add_conditional_edges(
    "chatbot",
    route_chatbot,
    {
        "tools": "tools",
        "save_memory": "save_memory"
    }
)
graph_builder.add_edge("tools", "cache_tool_mem")
graph_builder.add_edge("cache_tool_mem", "chatbot")
graph_builder.add_edge("save_memory", END)

# Agent类
class Agent:
    def __init__(self, name: str):
        self.name = name
        
        # self.group_id = name.encode('utf-8').hex()
        # 需要将group_id转成中文，可使用：bytes.fromhex(group_id).decode('utf-8')

        # 每个智能体都有自己的消息队列
        self.message_queue = asyncio.Queue()  
        # 反馈消息队列。移动、动作序列等需要先结束对话再等到结果的工具，应把反馈信息通过asend_feedback返回
        self.feedback_queue = asyncio.Queue() 
        self.runtime_state = {
            "focus_state": False
        }

        self.memory = MemorySaver()
        self.session_id = str(uuid.uuid4())
        self.config = {
            "configurable": {
                "thread_id": f"{self.name}:{self.session_id}",
                "message_queue": self.message_queue,
                "feedback_queue": self.feedback_queue,
                "runtime_state": self.runtime_state
            }
        }

        self.graph = graph_builder.compile(checkpointer=self.memory)
        
        # fork 后恢复用 state
        self._resume_state = None
        # ========== runtime control ==========
        self._running = False
        self._interrupt_event = asyncio.Event()
        self._process_task: asyncio.Task | None = None
        self._invoke_task: asyncio.Task | None = None
        self._runtime_lock = asyncio.Lock()
        self._message_lock = asyncio.Lock()
        self._idle_wakeup_task: asyncio.Task | None = None
        self._idle_wakeup_seq = 0
        self._is_graph_running = False

        # 是否存在未完成checkpoint
        # self._has_unfinished_checkpoint = False
        # self._interrupt_memory_saved = False

        # 记录message的时间，message风暴时自动进入专注状态
        self._message_interval = []

        print(f"[{self.name}]Agent is created.")

    async def _initialize_resume_state(self,
        old_values: dict,
        messages: list,
        interrupt_reason: str | None = None
        ):
        mem_to_save = old_values.get("mem_to_save", "")

        if interrupt_reason:
            mem_to_save += (f"\n[系统] 当前思考被中断：{interrupt_reason}")

        mem_to_save = await memory_manager.compress_memory_text(
            name=self.name,
            memory=mem_to_save,
            curtime=await TimeSystem().aget_current_time(to_str=True),
            unfinished=True,
        )

        self._resume_state = {
            "messages": messages,
            "name": self.name,
            "index": old_values.get("index", 0),
            "mem_summary": old_values.get("mem_summary", ""),
            "mem_fact": old_values.get("mem_fact", ""),
            "mem_episode": old_values.get("mem_episode", ""),
            "mem_skill_index": old_values.get("mem_skill_index", ""),
            "mem_to_save": mem_to_save,
            "logged_tool_call_ids": old_values.get("logged_tool_call_ids", [])
        }

    def _is_idle_wakeup_enabled(self) -> bool:
        return bool(IDLE_WAKEUP_CONFIG.get("enabled", True))

    def _can_schedule_idle_wakeup(self) -> bool:
        return (
            self._is_idle_wakeup_enabled()
            and self._running
            and not self._is_graph_running
            and not self._interrupt_event.is_set()
            and self.message_queue.empty()
            and self.feedback_queue.empty()
        )

    def _cancel_idle_wakeup(self):
        print(f"[{self.name}] cancel idle wakeup")
        self._idle_wakeup_seq += 1
        task = self._idle_wakeup_task
        self._idle_wakeup_task = None
        if task and not task.done():
            task.cancel()

    def _schedule_idle_wakeup(self):
        self._cancel_idle_wakeup()
        if not self._can_schedule_idle_wakeup():
            return

        min_delay = IDLE_WAKEUP_CONFIG["min_delay_seconds"]
        max_delay = IDLE_WAKEUP_CONFIG["max_delay_seconds"]
        delay = random.uniform(min_delay, max_delay)
        seq = self._idle_wakeup_seq
        self._idle_wakeup_task = asyncio.create_task(self._idle_wakeup_after_delay(seq, delay))
        print(f"[{self.name}] idle wakeup scheduled in {delay:.1f}s")

    async def _idle_wakeup_after_delay(self, seq: int, delay: float):
        try:
            await asyncio.sleep(delay)
            if seq != self._idle_wakeup_seq or not self._can_schedule_idle_wakeup():
                return

            summary = await self._request_world_event_summary()
            if seq != self._idle_wakeup_seq or not self._can_schedule_idle_wakeup():
                return

            await self._enqueue_idle_wakeup_message(summary)
        except asyncio.CancelledError:
            return
        except Exception as e:
            print(f"[{self.name}] idle wakeup error: {e}")

    async def _request_world_event_summary(self) -> str:
        max_events = IDLE_WAKEUP_CONFIG["max_world_events"]
        if max_events <= 0:
            return ""

        request_id = f"idle_wakeup:{self.name}:{uuid.uuid4()}"
        loop = asyncio.get_running_loop()
        fut = loop.create_future()
        TOOL_WAITERS[request_id] = fut
        try:
            request = message_pb2.AgentGetWorldEventSummaryRequest()
            request.agent = self.name
            request.request_id = request_id
            request.max_events = max_events
            request.ignore_self_events = IDLE_WAKEUP_CONFIG["ignore_self_events"]

            await AgentServerNetMessage().broadcast_message(request)
            timeout = IDLE_WAKEUP_CONFIG["rpc_timeout_seconds"]
            result = await asyncio.wait_for(fut, timeout=timeout)
            return f"{result}"
        except asyncio.TimeoutError:
            return "[世界事件摘要]\n暂时没有获取到周围变化。"
        except Exception as e:
            return f"[世界事件摘要]\n获取周围变化时遇到异常：{e}"
        finally:
            TOOL_WAITERS.pop(request_id, None)

    async def _enqueue_idle_wakeup_message(self, world_event_summary: str):
        async with self._message_lock:
            if not self._can_schedule_idle_wakeup():
                return

            real_time = time.time()
            virtual_time = await TimeSystem().aget_current_time(to_str=True)
            template = IDLE_WAKEUP_CONFIG["message_template"]
            message = template.format(world_event_summary=world_event_summary.strip())
            if virtual_time != "未启动":
                message = f"[{virtual_time}]" + message

            print(f"[{self.name}]Idle wakeup message: {message}")
            await self.message_queue.put(TimedMessage(timestamp=real_time, content=message))
            self._idle_wakeup_seq += 1
            self._idle_wakeup_task = None

    async def astart(self):
        async with self._runtime_lock:
            if self._running:
                return

            # =========================
            # fork 新 lineage 后
            # 初始化 graph state
            # =========================
            if self._resume_state is not None:
                print(f"[{self.name}] restore resume state")
                await self.graph.aupdate_state(
                    self.config,
                    self._resume_state
                )
                self._resume_state = None
            self._interrupt_event = asyncio.Event()
            self._process_task = asyncio.create_task(self.aprocess_message())
            self._running = True
            # self._interrupt_memory_saved = False

            print(f"[{self.name}] processing started")
            self._schedule_idle_wakeup()
            # snapshot = await self.graph.aget_state(self.config)
            # has_checkpoint = (
            #     snapshot is not None 
            #     and snapshot.next
            # )
            # if has_checkpoint:
            #     print(f"[{self.name}] resume pending checkpoint")

    async def asend_message(self, message: str, force_interrupt: bool = False):
        await self._asend_message(message, is_feedback=False, force_interrupt=force_interrupt)

    async def asend_feedback(self, feedback: str, force_interrupt: bool = False):
        await self._asend_message(feedback, is_feedback=True, force_interrupt=force_interrupt)

    async def _asend_message(self, message: str, is_feedback: bool = False, force_interrupt: bool = False):
        self._cancel_idle_wakeup()
        # =========================
        # 0. 记录消息时间
        # =========================
        real_time = time.time()
        self._message_interval.append(real_time)
        # 只保留5秒
        self._message_interval[:] = [t for t in self._message_interval if t > real_time - 5]
        # 如果短时间内消息数达到5条，强制进专注状态：
        if len(self._message_interval) >= 5:
            self.runtime_state["focus_state"] = True
        # =========================
        # 1. 打断
        # =========================
        should_interrupt = (
            force_interrupt # 强制打断时，必定打断
            or is_feedback # 反馈消息时，必定打断
            or not self.runtime_state["focus_state"] # 非专注状态时，打断
        )
        if should_interrupt:
            await self.ainterrupt(reason="被打断")
        # =========================
        # 2. 发送消息
        # =========================
        async with self._message_lock:
            real_time = time.time()
            virtual_time = await TimeSystem().aget_current_time(to_str = True)
            if virtual_time == "未启动":
                text = message
            else:
                text = f"[{virtual_time}]"+message

            if is_feedback:
                print(f"[{self.name}]Get feedback: {text}")
                await self.feedback_queue.put(TimedMessage(timestamp=real_time, content=text))
            else:
                print(f"[{self.name}]Get message: {text}")
                await self.message_queue.put(TimedMessage(timestamp=real_time, content=text))
        # =========================
        # 3. 重启
        # =========================
        if should_interrupt:
            await self.astart()
    
    async def _drain_items(self,
        include_message_queue: bool,
        include_feedback_queue: bool
    ) -> list[TimedMessage]:
        items = []
        if include_message_queue:
            while not self.message_queue.empty():
                try:
                    msg = self.message_queue.get_nowait()
                except asyncio.QueueEmpty:
                    break
                items.append(msg)
                self.message_queue.task_done()
        if include_feedback_queue:
            while not self.feedback_queue.empty():
                try:
                    fb = self.feedback_queue.get_nowait()
                except asyncio.QueueEmpty:
                    break
                items.append(fb)
                self.feedback_queue.task_done()
        return items
    
    async def aprocess_message(self):
        self._running = True
        try:
            while not self._interrupt_event.is_set():
                # # =====================================
                # # 0. 优先恢复 checkpoint
                # # =====================================
                # snapshot = await self.graph.aget_state(self.config)
                # has_checkpoint = (
                #     snapshot is not None 
                #     and snapshot.next
                # )
                # if has_checkpoint:
                #     input_state = None # self.graph.ainvoke(None,self.config)时，会从checkpoint继续跑，避免重复执行 tool
                #     print(f"[{self.name}] resume from checkpoint")
                # else:
                # =====================================
                # 1. 阻塞直到至少有 message
                # 同时等待两个队列
                # =====================================
                msg_task = asyncio.create_task(self.message_queue.get())
                fb_task = asyncio.create_task(self.feedback_queue.get())

                interrupt_task = asyncio.create_task(self._interrupt_event.wait())

                done, pending = await asyncio.wait(
                    [msg_task, fb_task, interrupt_task], 
                    return_when=asyncio.FIRST_COMPLETED
                )
                # cleanup pending
                for task in pending:
                    task.cancel()
                await asyncio.gather(*pending, return_exceptions=True)

                # interrupted while waiting
                if interrupt_task in done:
                    # # 把已经取出的消息放回 queue
                    # for task in [msg_task, fb_task]:
                    #     if task in done:
                    #         try:
                    #             item = task.result()
                    #             if task is msg_task:
                    #                 await self.message_queue.put(item)
                    #             else:
                    #                 await self.feedback_queue.put(item)
                    #         except Exception:
                    #             pass
                    # for task in pending:
                    #     task.cancel()
                    break

                # =========================
                # 2. collect messages
                # =========================
                items = []
                # 哪个先完成就取哪个
                if msg_task in done:
                    first_item = msg_task.result()
                    self.message_queue.task_done()
                else:
                    first_item = fb_task.result()
                    self.feedback_queue.task_done()
                # for task in pending:
                #     try:
                #         await task
                #     except asyncio.CancelledError:
                #         pass
                items.append(first_item)
                # 2. 获取所有消息
                remaining_items = await self._drain_items(
                    include_message_queue=True,
                    include_feedback_queue=True
                )
                items.extend(remaining_items)
                items.sort()
                full_messages = "\n".join(item.content for item in items)
                
                # =====================================
                # 3. 初始化 input_state
                # =====================================
                msg_id = str(uuid.uuid4())
                human_msg = HumanMessage(content=full_messages, id=msg_id)
                
                # 初始输入状态
                input_state = {
                    "messages": [human_msg], 
                    "name": self.name
                }
                # 当前 checkpoint 正在处理中
                # self._has_unfinished_checkpoint = True

                # =========================
                # 4. invoke loop
                # =========================
                self._is_graph_running = True
                try:
                    while not self._interrupt_event.is_set():
                        try:
                            self._invoke_task = asyncio.create_task(
                                self.graph.ainvoke(input_state,self.config)
                            )
                            # ainvoke 已正式开始
                            response = await self._invoke_task
                            self.runtime_state["focus_state"] = False # 正常完成了一轮对话，关闭专注状态
                            output = response["messages"][-1].content
                            print(f"[{self.name}]Response: {output}")
                            break # 成功则跳出重试，回到最外层等待新消息
                        except asyncio.CancelledError:# self._invoke_task = asyncio.create_task时，报CancelledError
                            # interrupt cancel
                            if self._interrupt_event.is_set():# 进入打断流程时，break，不抛出异常
                                break
                            raise# 错误时，抛出异常
                        except Exception as e:
                            print(f"[{self.name}]Error occurred: {e}")

                            # ⭐ 核心：触发 LangGraph 的 Checkpoint 恢复机制
                            # 将输入置为 None，下次循环 ainvoke 时会直接从中断的 LLM 节点继续跑
                            input_state = None

                            await asyncio.sleep(2) # 遇到网络错误，歇2秒再试
                        finally:
                            self._invoke_task = None
                finally:
                    self._is_graph_running = False
                    self._schedule_idle_wakeup()
                        
        finally:
            self._running = False
            self._invoke_task = None
            self._process_task = None
            # self._interrupt_event.clear()
            print(f"[{self.name}] process stopped")

    # async def clear_langgraph_memory(self):
    #     """
    #     清空 LangGraph checkpoint
    #     但保留 agent runtime
    #     """
    #     self.memory = MemorySaver()
    #     self.graph = graph_builder.compile(checkpointer=self.memory)
    #     self.session_id = str(uuid.uuid4())
    #     self.config = {
    #         "configurable": {
    #             "thread_id": f"{self.name}:{self.session_id}",
    #             "message_queue": self.message_queue,
    #             "feedback_queue": self.feedback_queue,
    #             "runtime_state": self.runtime_state
    #         }
    #     }
    #     # 清空恢复状态
    #     self._resume_state = None
    #     # self._interrupt_memory_saved = False

    #     print(f"[{self.name}] LangGraph 对话记忆已清空")

    async def ainterrupt(
        self, 
        reason: str = "被打断",
        # resume_checkpoint: bool = True
        ):
        """
        优雅中断当前 Agent

        - 停止 ainvoke
        - 保存 mem_to_save
        - 保留 queue
        - 保留 checkpoint
        - 保留 messages

        Args:
            reason: 中断原因
        """
        async with self._runtime_lock:
            if not self._running:
                return
            print(f"[{self.name}] interrupt requested")
            self._cancel_idle_wakeup()

            # =========================
            # 1. 中断运行
            # =========================
            self._interrupt_event.set()
            # cancel invoke
            if self._invoke_task:
                try:
                    self._invoke_task.cancel()
                except Exception:
                    pass
            # wait process exit
            if self._process_task:
                try:
                    await asyncio.wait_for(
                        asyncio.gather(
                            self._process_task,
                            return_exceptions=True
                        ),
                        timeout=5
                    )
                except asyncio.TimeoutError:
                    print(f"[{self.name}] force interrupt timeout")
            # =========================
            # 2. 读取旧 state
            # =========================
            snapshot = await self.graph.aget_state(self.config)
            old_values = snapshot.values if snapshot else {}
            # =========================
            # 3. 保存 interrupt memory
            # =========================
            # if not self._interrupt_memory_saved:
            #     await self._save_interrupt_memory(reason)
            #     self._interrupt_memory_saved = True
            # =========================
            # 4. 清理 unfinished tool call
            # =========================
            messages = list(old_values.get("messages", []))

            if messages:
                # 找最后一个 AI tool call message
                last_ai_index = None
                for i in range(len(messages) - 1, -1, -1):
                    msg = messages[i]
                    if isinstance(msg, AIMessage) and msg.tool_calls:
                        last_ai_index = i
                        break
                if last_ai_index is not None:
                    ai_msg = messages[last_ai_index]
                    # 收集后续所有 ToolMessage 的 tool_call_id
                    completed_tool_call_ids = set()
                    for msg in messages[last_ai_index + 1:]:
                        tool_call_id = getattr(msg, "tool_call_id", None)
                        if tool_call_id:
                            completed_tool_call_ids.add(tool_call_id)
                    # 检查是否所有 tool_call 都已完成
                    unfinished = False
                    for tc in ai_msg.tool_calls:
                        if tc["id"] not in completed_tool_call_ids:
                            unfinished = True
                            break
                    # 只有 unfinished 才删除
                    if unfinished:
                        print(f"[{self.name}] remove unfinished tool call message")
                        messages = messages[:last_ai_index]
            # =========================
            # 5. 创建 resume state
            # =========================
            await self._initialize_resume_state(old_values=old_values, messages=messages, interrupt_reason=reason)
            # =========================
            # 6. fork 新 lineage
            # =========================
            self.memory = MemorySaver()
            self.graph = graph_builder.compile(checkpointer=self.memory)
            self.session_id = str(uuid.uuid4())
            self.config = {
                "configurable": {
                    "thread_id": f"{self.name}:{self.session_id}",
                    "message_queue": self.message_queue,
                    "feedback_queue": self.feedback_queue,
                    "runtime_state": self.runtime_state
                }
            }

            self._running = False
            print(f"[{self.name}] interrupted")

    async def _save_interrupt_memory(self, reason: str):
        try:
            snapshot = await self.graph.aget_state(self.config)
            if not snapshot:
                return
            values = snapshot.values

            mem_to_save = values.get("mem_to_save","")
            if not mem_to_save:
                return

            cur_time = await TimeSystem().aget_current_time(to_str=True)
            mem_to_save += (f"\n[{cur_time}]"f"[系统] 当前思考被中断：{reason}")
            curtime = await TimeSystem().aget_current_time()

            await memory_manager.save_memory(
                name=self.name,
                memory=mem_to_save,
                curtime=curtime
            )

            # 防止恢复后重复保存
            await self.graph.aupdate_state(
                self.config,
                {"mem_to_save": ""},
                as_node="save_memory"
            )

            print(f"[{self.name}] interrupt memory saved")

        except Exception as e:
            print(f"[{self.name}] "f"save interrupt memory error: {e}")

    async def afinish(self):
        """
        完全结束 Agent runtime

        - 停止 ainvoke
        - 停止 process loop
        - 丢弃 checkpoint
        - 丢弃 resume state
        - 清空 queue
        - 不保留未保存上下文
        """
        async with self._runtime_lock:
            print(f"[{self.name}] finish requested")
            self._cancel_idle_wakeup()
            self._running = False
            # =========================
            # 1. interrupt runtime
            # =========================
            self._interrupt_event.set()
            if self._invoke_task:
                try:
                    self._invoke_task.cancel()
                except Exception:
                    pass
            if self._process_task:
                try:
                    await asyncio.wait_for(
                        asyncio.gather(
                            self._process_task,
                            return_exceptions=True
                        ),
                        timeout=5
                    )
                except asyncio.TimeoutError:
                    print(f"[{self.name}] finish timeout")
            await self._save_interrupt_memory("Agent 结束运行，保存未完成的阶段性经历")
            # =========================
            # 2. clear queues
            # =========================
            await self._drain_items(
                include_message_queue=True,
                include_feedback_queue=True
            )
            # =========================
            # 3. clear runtime state
            # =========================
            self.runtime_state["focus_state"] = False
            # =========================
            # 4. clear resume state
            # =========================
            self._resume_state = None
            # =========================
            # 4. initialize queues
            # =========================
            self.message_queue = asyncio.Queue()
            self.feedback_queue = asyncio.Queue()
            self._message_interval = []
            # =========================
            # 5. clear langgraph state
            # =========================
            self.memory = MemorySaver()
            self.graph = graph_builder.compile(
                checkpointer=self.memory
            )
            self.session_id = str(uuid.uuid4())
            self.config = {
                "configurable": {
                    "thread_id": f"{self.name}:{self.session_id}",
                    "message_queue": self.message_queue,
                    "feedback_queue": self.feedback_queue,
                    "runtime_state": self.runtime_state
                }
            }
            # =========================
            # 6. clear tasks
            # =========================
            self._invoke_task = None
            self._process_task = None
            self._interrupt_event = asyncio.Event()

            print(f"[{self.name}] finished")
