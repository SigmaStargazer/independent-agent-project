import asyncio
import time

from langchain_core.prompts import ChatPromptTemplate, MessagesPlaceholder
from langchain_openai import ChatOpenAI
from langchain_core.output_parsers import StrOutputParser
from langchain_core.messages import HumanMessage, AIMessage, SystemMessage, ToolMessage
from langchain_core.runnables import RunnablePassthrough

from typing import Annotated, Literal
from typing_extensions import TypedDict

from langgraph.graph import StateGraph, START, END
from langgraph.graph.message import add_messages
from langgraph.prebuilt import ToolNode, tools_condition

# from contextlib import ExitStack
from langgraph.checkpoint.memory import MemorySaver
from memory_system.memory_manager import MemoryManager

from graphiti_core.nodes import EpisodeType
from graphiti_core.search import search_config_recipes

from agent_framwork.tools import base_tools
from agent_framwork.systems.time_system import TimeSystem

from tools.perf_tool import perf_print

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
model_name = "glm-4.6"

model = ChatOpenAI(
        model_name = model_name,
        openai_api_base = model_api_base,
        openai_api_key = model_api_key,
        streaming = False,
        verbose = True
    )
output_parser = StrOutputParser()

tools = [base_tools.communicate_to_agent, 
        #  base_tools.communicate_to_user,
         base_tools.get_agent_list, 
         base_tools.get_cur_time,
         base_tools.add_alarm,
         base_tools.get_alarm_list,
         base_tools.remove_alarm,
         base_tools.search_fact_memories,
         base_tools.search_episode_memories]

llm_with_tools = model.bind_tools(tools)

MAX_CONTEXT_SIZE = 20

def _filter_messages(messages, k=20):
        """
        用于删减上下文长度
        """
        return messages[-k:]

# 提示词模板
system_template = """你扮演的角色名叫{name}，{description}。
向你发送信息的是系统管理员，它会不断告诉你关于周遭的情况。你需要根据情况做出相应的反应
规则：
1）仅当你使用了工具时，才会对外界产生实际影响。
2）你的直接回复只作为你的心理活动，不会被任何人看到。
因此如果你需要与用户、agent等具体某一对象进行交流，也请使用相应的工具，避免你想要传递的信息无法传达给对方。

现在时间:
{curtime}
你刚联想到:
{mem_fact}

{mem_episode}
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
    description: str # agent描述
    messages: Annotated[list, add_messages] # agent上下文
    group_id: str # graphiti的group_id，用于分区
    mem_fact: str # 检索到的事实记忆
    mem_episode: str # 检索到的情景记忆
    mem_to_save: str # 待存储的记忆
    logged_tool_call_ids: list[str] # 用于记忆工具调用时，去重tool_message中重试的部分

# 定义节点
async def search_memory(state: State):
    """
    根据用户问题，检索事实记忆和情景记忆
    """
    query = state['messages'][-1].content
    group_id = state['group_id']
    limit = 1 # 检索的记忆数量

    mem_fact = ""
    mem_episode = ""
    
    # perf_print("memory_manager初始化开始")
    # await memory_manager.initialize()  # 确保初始化完成

    perf_print("rag事实记忆开始")
    memories = await memory_manager.graphiti.search(
        query, 
        # COMBINED 检索能一次性获取事实（edges）、实体（nodes）和主题（communities）。
        # RRF速度快
        # config=search_config_recipes.COMBINED_HYBRID_SEARCH_RRF, 
        num_results = limit,
        group_ids=[group_id]
    )
    for memory in memories:
        # print(memory) # 测试
        mem_fact += f"- {memory.fact}\n"
        if hasattr(memory, 'valid_at') and memory.valid_at:
            mem_fact += f'事实产生时间: {memory.valid_at}\n'
        if hasattr(memory, 'invalid_at') and memory.invalid_at:
            mem_fact += f'事实失效时间: {memory.invalid_at}\n'
    print(f'{mem_fact}')
    perf_print("rag事实记忆完成")

    perf_print("rag情景记忆开始")
    time_key = "valid_at" # 表里的时间key
    episodes_uuid_list = []
    memories = await memory_manager.graphiti._search(
        query, 
        config=search_config_recipes.EDGE_HYBRID_SEARCH_RRF, 
        group_ids=[group_id]
    )
    for memory in memories.edges:
        if hasattr(memory, 'episodes') and memory.episodes:
            episodes_uuid_list += memory.episodes
    condition = f"WHERE n.uuid in {episodes_uuid_list}" if episodes_uuid_list else ""
    cypher = f"""
        MATCH (n: Episodic) {condition}
        RETURN n
        ORDER BY n.{time_key} ASC
        LIMIT {limit};
        """ 
    # print(cypher)
    # 检索episodes的实际内容
    try:
        response = await memory_manager.conn.execute(cypher)
        for row in response.rows_as_dict():
            memory = row['n']
            mem_episode += f"情景: \"{memory['content']}\"\n"
            # if 'valid_at' in memory:
            #     valid_at_time_str = memory[time_key].strftime('%Y-%m-%d %H:%M:%S')
            #     mem_episode += f"发生时间: {valid_at_time_str}\n"
            mem_episode += "---\n"
    except RuntimeError as e: # 一般为刚刚建立库，检索失败
        print(f"情景记忆检索失败: {e}")
    print(f'{mem_episode}')
    perf_print("rag情景记忆完成")

    # 需要存储接收到的用户信息
    perf_print("缓存记忆开始")
    cur_time = await TimeSystem().aget_current_time(to_str = True)
    mem_to_save = f"[{cur_time}]{state['name']}收到信息: {query}"
    perf_print("缓存记忆完成")
    
    print(mem_to_save) # 测试
    return {
        "mem_fact": mem_fact, 
        "mem_episode": mem_episode,
        "mem_to_save": mem_to_save
    }

async def chatbot(state: State):
    mem_to_save = state['mem_to_save']

    cur_time = await TimeSystem().aget_current_time()
    prompt = await prompt_template.ainvoke({"messages": state['messages'],
                                     "name": state['name'],
                                     "description": state['description'],
                                     "curtime": cur_time,
                                     "mem_fact": state['mem_fact'],
                                     "mem_episode": state['mem_episode']})
    # print(f"【prompt】:{prompt}") # 测试
    # for message in prompt.messages:
    #     message.pretty_print()
    # response = await llm_with_tools.ainvoke(prompt)
    perf_print("模型输出开始")
    response = await llm_with_tools.ainvoke(prompt)
    print(response.content)
    perf_print("模型输出完成")
    

    # 需要存储接收到的用户信息
    if response.content.strip(): # 工具调用时，response.content为空
        perf_print("缓存记忆开始")
        cur_time = await TimeSystem().aget_current_time(to_str = True)
        mem_to_save += "\n" + f"[{cur_time}]{state['name']}心想: {response.content}"
        perf_print("缓存记忆完成")
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
            new_entries.append(f"[{cur_time}]{state['name']} 使用了 {tool_call['name']}，输入为 {tool_call['args']}")
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
    mem_to_save = state['mem_to_save']
    
    perf_print("存储记忆开始")
    print("【待存储记忆start】")
    print(mem_to_save)
    print("【待存储记忆end】")
    curtime = await TimeSystem().aget_current_time()
    await MemoryManager().graphiti.add_episode(
        name=f"{state['name']}_mem_{curtime}",
        episode_body=mem_to_save,
        source=EpisodeType.text,
        source_description=f"{state['name']}_mem_{curtime}", 
        reference_time=curtime,
        group_id=state['group_id']
    )
    perf_print("存储记忆完成")

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
# graph_builder.add_conditional_edges(
#     "chatbot",
#     tools_condition,
# )
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
    def __init__(self, name: str, description: str):
        self.name = name
        self.description = description
        
        self.group_id = name.encode('utf-8').hex()
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
                                                                "description": self.description,
                                                                "group_id": self.group_id}, 
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
