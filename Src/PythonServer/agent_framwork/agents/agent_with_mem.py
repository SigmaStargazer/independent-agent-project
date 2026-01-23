# from ast import Str
import asyncio

from langchain_core.prompts import ChatPromptTemplate, MessagesPlaceholder
from langchain_openai import ChatOpenAI
from langchain_core.output_parsers import StrOutputParser
from langchain_core.messages import AIMessage
from langchain_core.runnables import RunnablePassthrough

from typing import Annotated, Literal
from typing_extensions import TypedDict

from langgraph.graph import StateGraph, START, END
from langgraph.graph.message import add_messages
from langgraph.prebuilt import ToolNode

# from contextlib import ExitStack
from langgraph.checkpoint.memory import MemorySaver
from memory_system.memory_manager import MemoryManager

# from graphiti_core.nodes import EpisodeType
# from graphiti_core.search import search_config_recipes

from agent_framwork.tools import base_tools
from agent_framwork.systems.time_system import TimeSystem

from tools.perf_tool import aperf_print

# # 千问
# model_api_base = "https://dashscope.aliyuncs.com/compatible-mode/v1"
# model_api_key = "sk-7a3958e0fdf840e49a2edd83b25dd228"
# model_name = "qwen-max"

# # moonshot
# model_api_base = "https://api.moonshot.cn/v1"
# model_api_key = "sk-0cYUM2FsdWqmyJeth1He0FXlCVlcxScjNb3YPYHjl78vyEgY"
# model_name = "kimi-k2-0711-preview"

# 智谱
model_api_base = "https://open.bigmodel.cn/api/paas/v4"
model_api_key = "61383e606d871c028870a8f251a77f08.JH0An7yUOzhd8cIi"
model_name = "glm-4.7"

model = ChatOpenAI(
        model_name = model_name,
        openai_api_base = model_api_base,
        openai_api_key = model_api_key,
        streaming = False,
        verbose = True
    )
output_parser = StrOutputParser()

tools = [base_tools.communicate_to_agent, 
         base_tools.communicate_to_user,
         base_tools.get_agent_list, 
         base_tools.get_cur_time,
         base_tools.search_fact_memories,
         base_tools.search_episode_memories,
         base_tools.move_cmd,
         base_tools.interact_cmd,
        # base_tools.add_alarm,
        #  base_tools.get_alarm_list,
        #  base_tools.remove_alarm
        ]

llm_with_tools = model.bind_tools(tools)

MAX_CONTEXT_SIZE = 20

def _filter_messages(messages, k=20):
        """
        用于删减上下文长度
        """
        return messages[-k:]

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

<规则>
你会不断从周围环境获取信息，你需要自主决定下一步行动，但请注意：
1）你的直接回复将只作为你的心理活动，不会被任何人看到，也不会对外界产生任何影响。
2）仅当你使用了工具时，才会对外界产生实际影响。
3）如需与任一对象进行交流，请使用communicate系列工具，避免你想要传递的信息无法传达给对方。
</规则>
"""


prompt_template = ChatPromptTemplate.from_messages(
    [
        ("system", system_template),
        MessagesPlaceholder(variable_name="messages")
    ]
)
# 初始化chain
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
    mem_to_save: str # 待存储的记忆
    logged_tool_call_ids: list[str] # 用于记忆工具调用时，去重tool_message中重试的部分

# 定义节点
async def search_memory(state: State):
    """
    根据用户问题，检索事实记忆和情景记忆
    """
    name = state['name']
    query = state['messages'][-1].content
    # group_id = state['group_id']
    limit = 1 # 检索的记忆数量

    mem_summary = ""
    mem_fact = ""
    mem_episode = ""
    
    await aperf_print(f"[{name}]加载agent简介开始")
    mem_summary = await memory_manager.load_agent_summary(name=state['name'])
    await aperf_print(f"[{name}]加载agent简介完成")

    await aperf_print(f"[{name}]rag事实记忆开始")
    mem_fact = await memory_manager.search_fact_memory(name=state['name'], query=query, limit=limit)
    await aperf_print(f"[{name}]rag事实记忆完成")

    await aperf_print(f"[{name}]rag情景记忆开始")
    mem_episode = await memory_manager.search_episode_memory(name=state['name'], query=query, limit=limit)
    await aperf_print(f"[{name}]rag情景记忆完成")

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
        "mem_to_save": mem_to_save
    }

async def chatbot(state: State):
    name = state['name']
    mem_to_save = state['mem_to_save']

    cur_time = await TimeSystem().aget_current_time()
    prompt = await prompt_template.ainvoke({"messages": state['messages'],
                                     "name": state['name'],
                                     "curtime": cur_time,
                                     "mem_summary": state['mem_summary'],
                                     "mem_fact": state['mem_fact'],
                                     "mem_episode": state['mem_episode']})
    # 测试：打印prompt
    print(f"【prompt】:") 
    for message in prompt.messages:
        message.pretty_print()

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
    await MemoryManager().save_memory(name=state['name'], memory=mem_to_save, curtime=curtime)
    await aperf_print(f"[{name}]存储记忆完成")

    return {"mem_to_save": ""}

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

        self.memory = MemorySaver()
        self.config = {"configurable": {"thread_id": self.name}}

        self.queue = asyncio.Queue()  # 每个智能体都有自己的消息队列

        self.graph = graph_builder.compile(checkpointer=self.memory)
        print(f"[{self.name}]Agent is created.")

    async def asend_message(self, message: str):
        now = await TimeSystem().aget_current_time(to_str = True)
        if now == "未启动":
            print(f"[{self.name}]Get message: {message}")
            await self.queue.put(message)
        else:
            message_with_time = f"[{now}]"+message
            print(f"[{self.name}]Get message: {message_with_time}")
            await self.queue.put(message_with_time)
    
    async def aprocess_message(self):
        try:
            full_messages = ""
            while True:
                # 尝试一次性取出所有当前队列中的消息
                try:
                    while True:
                        message = self.queue.get_nowait()
                        if full_messages:
                            full_messages += "\n"
                        full_messages += message
                        self.queue.task_done()
                except asyncio.QueueEmpty:
                    # 如果有消息就处理
                    if full_messages:
                        print(f"[{self.name}]Processing message: {full_messages}")
                        ## 重试
                        # for i in range(3):
                        # while True:
                        try:
                            response = await self.graph.ainvoke({"messages": [("user", full_messages)], 
                                                                "name": self.name, 
                                                                # "group_id": self.group_id
                                                                }, 
                                                                self.config)
                            output = response["messages"][-1].content
                            print(f"[{self.name}]Response: {output}")
                            full_messages = ""
                            # break
                        except Exception as e:
                            print(f"[{self.name}]Error occurred: {e}")
                            await asyncio.sleep(1)
                                # if i < 2:
                                #     time.sleep(2)  # 等待2秒再重试
                    else:
                        await asyncio.sleep(0.1)
        except asyncio.CancelledError:
            print(f"[{self.name}]Processing task has been cancelled.")

    async def clear_langgraph_memory(self):
        """删除 LangGraph MemorySaver 中的对话状态"""
        # 创建新的 MemorySaver 实例替换旧的
        self.memory = MemorySaver()
        # 重新编译 graph 以应用新的 checkpointer
        self.graph = graph_builder.compile(checkpointer=self.memory)
        print(f"[{self.name}] LangGraph 对话记忆已清空")
