import asyncio
import time

from langchain_core.prompts import ChatPromptTemplate, MessagesPlaceholder
from langchain_openai import ChatOpenAI
from langchain_core.output_parsers import StrOutputParser
from langchain_core.messages import HumanMessage, AIMessage, SystemMessage, ToolMessage
from langchain_core.runnables import RunnablePassthrough

from typing import Annotated
from typing_extensions import TypedDict

from langgraph.graph import StateGraph, START, END
from langgraph.graph.message import add_messages
from langgraph.prebuilt import ToolNode, tools_condition

# from contextlib import ExitStack
from langgraph.checkpoint.memory import MemorySaver

from agent_framwork.tools import base_tools
from agent_framwork.systems.time_system import TimeSystem

# # 千问
# model_api_base = "https://dashscope.aliyuncs.com/compatible-mode/v1"
# model_api_key = "sk-7a3958e0fdf840e49a2edd83b25dd228"
# model_name = "qwen-max"

# moonshot
model_api_base = "https://api.moonshot.cn/v1"
model_api_key = "sk-0cYUM2FsdWqmyJeth1He0FXlCVlcxScjNb3YPYHjl78vyEgY"
model_name = "kimi-k2-0711-preview"

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
         base_tools.add_alarm,
         base_tools.get_alarm_list,
         base_tools.remove_alarm]

llm_with_tools = model.bind_tools(tools)

MAX_CONTEXT_SIZE = 20

def _filter_messages(messages, k=20):
        """
        用于删减上下文长度
        """
        return messages[-k:]

# 提示词模板
system_template = """你的名字是{name}，{description}。
向你发送信息的是系统管理员，它会不断告诉你关于周遭的情况。你需要根据情况做出相应的反应
注意：
你的直接回复只会被当作你的心理活动，不会被任何人看到。
如果需要与用户、agent等具体某一对象进行交流，请使用相应的工具。"""
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

# 创建graph
class State(TypedDict):
    messages: Annotated[list, add_messages]
    name: str
    description: str

graph_builder = StateGraph(State)

async def chatbot(state: State):
    response = await chain.ainvoke({"messages": state["messages"], "name": state["name"], "description": state["description"]})
    return {"messages": [response]}

graph_builder.add_node("chatbot", chatbot)

tool_node = ToolNode(tools=tools)
graph_builder.add_node("tools", tool_node)

graph_builder.add_conditional_edges(
    "chatbot",
    tools_condition,
)
graph_builder.add_edge("tools", "chatbot")
graph_builder.add_edge(START, "chatbot")

# Agent类
class Agent:
    def __init__(self, name: str, description: str, memory: MemorySaver):
        self.name = name
        self.description = description
        self.memory = memory

        self.config = {"configurable": {"thread_id": self.name}}
        self.queue = asyncio.Queue()  # 每个智能体都有自己的消息队列

        self.graph = graph_builder.compile(checkpointer=self.memory)
        print(f"[{self.name}]Agent is created.")

    async def asend_message(self, message: str):
        now = await TimeSystem().aget_current_time_str()
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
                        while True:
                            try:
                                response = await self.graph.ainvoke({"messages": [("user", full_messages)], 
                                                                    "name": self.name, 
                                                                    "description": self.description}, 
                                                                    self.config)
                                output = response["messages"][-1].content
                                print(f"[{self.name}]Response: {output}")
                                full_messages = ""
                                break
                            except Exception as e:
                                print(f"[{self.name}]Error occurred: {e}")
                                await asyncio.sleep(1)
                                # if i < 2:
                                #     time.sleep(2)  # 等待2秒再重试
                    else:
                        await asyncio.sleep(0.1)
        except asyncio.CancelledError:
            print(f"[{self.name}]Processing task has been cancelled.")
