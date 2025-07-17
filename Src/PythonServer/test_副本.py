from langchain_core.prompts import ChatPromptTemplate, MessagesPlaceholder
from langchain_openai import ChatOpenAI
from langchain_core.output_parsers import StrOutputParser
from langchain_core.messages import HumanMessage, AIMessage, SystemMessage, ToolMessage

from langchain_community.chat_message_histories import ChatMessageHistory # 存储历史对话消息的类
from langchain_core.chat_history import BaseChatMessageHistory # 存储聊天消息历史记录的抽象类
from langchain_core.runnables.history import RunnableWithMessageHistory #RunnableWithMessageHistory包装另一个 Runnable 并为其管理聊天消息历史记录；它负责读取和更新聊天消息历史记录。

from typing import Annotated
from typing_extensions import TypedDict
from langchain_core.tools import tool

from langgraph.graph import StateGraph, START, END
from langgraph.graph.message import add_messages
from langgraph.prebuilt import ToolNode, tools_condition

from contextlib import ExitStack
from langgraph.checkpoint.memory import MemorySaver

import asyncio

from agent_framwork.tools.base_tools import get_function

model_api_base = "https://dashscope.aliyuncs.com/compatible-mode/v1"
model_api_key = "sk-7a3958e0fdf840e49a2edd83b25dd228"
model_name = "qwen-max"

model = ChatOpenAI(
        model_name = model_name,
        openai_api_base = model_api_base,
        openai_api_key = model_api_key,
        streaming = False,
        verbose = True
    )
output_parser = StrOutputParser()

# @tool
# async def get_fuction() -> str:
#     """获取功能清单"""
#     task_list = """差旅申请：帮用户发起一个出差旅申请流程。 
# 会议室查询：帮用户查询会议室的预定与使用情况。 
# 会议室预定：帮用户预定会议室。 
# 企业管理办法知识库问答：在企业管理办法知识库中检索相关内容，解答用户的问题。 
# 帮助：解答用户关于东信小智能做什么、能发起哪些流程、不能做什么等问题。"""
#     return task_list

tools = [get_function]

llm_with_tools = model.bind_tools(tools)

# 提示词模板
system_template = "你的名字是{name}，"+"{description}"
prompt_template = ChatPromptTemplate.from_messages(
    [
        ("system", system_template),
        MessagesPlaceholder(variable_name="messages")
    ]
)
# 初始化chain
chain = prompt_template | llm_with_tools

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
# Any time a tool is called, we return to the chatbot to decide the next step
graph_builder.add_edge("tools", "chatbot")
graph_builder.add_edge(START, "chatbot")

class Agent:
    def __init__(self, name: str, description: str, memory: MemorySaver()):
        self.name = name
        self.description = description
        self.memory = memory

        self.config = {"configurable": {"thread_id": self.name}}
        self.queue = asyncio.Queue()  # 每个智能体都有自己的消息队列

        self.graph = graph_builder.compile(checkpointer=self.memory)

    async def send_message(self, message: str):
        print(f"get message: {message}")
        await self.queue.put(message)
    
    async def process_message(self):
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
                        print(f"Processing message: {full_messages}")
                        try:
                            # response = await self.chain_with_message_history.ainvoke(
                            #     {"messages": [HumanMessage(content=full_messages)], "name": self.name},
                            #     config=self.config,
                            # )
                            response = await self.graph.ainvoke({"messages": [("user", full_messages)], 
                                                                 "name": self.name, 
                                                                 "description": self.description}, 
                                                                self.config)
                            output = response["messages"][-1].content
                            print(f"response: {output}")
                            full_messages = ""
                        except Exception as e:
                            print(f"Error occurred: {e}")
                    else:
                        await asyncio.sleep(0.1)
        except asyncio.CancelledError:
            print("Processing task has been cancelled.")

memory = MemorySaver()

async def main():
    agent_A = Agent(name="小明", description="是一个帮助机器人", memory=memory)
    agent_B = Agent(name="小红", description="是一个聊天机器人", memory=memory)
    # AgentA 开始处理消息
    processing_task = asyncio.create_task(agent_A.process_message())  # 创建任务后不再取消
    processing_task = asyncio.create_task(agent_B.process_message())  # 创建任务后不再取消

    # 模拟同时发送多个消息
    await agent_A.send_message('你好，我是小亮')
    await agent_A.send_message('能介绍下你自己吗？')
    print("发送第一批信息")

    await asyncio.sleep(0.5)

    await agent_A.send_message('对了，')
    await agent_A.send_message('你还记得我的名字吗？')
    print("发送第二批信息")

    await asyncio.sleep(0.5)

    # 模拟同时发送多个消息
    await agent_B.send_message('你好，我是小强')
    await agent_B.send_message('能介绍下你自己吗？')
    print("发送第一批信息")

    await asyncio.sleep(0.5)

    await agent_B.send_message('对了，')
    await agent_B.send_message('你还记得我的名字吗？')
    print("发送第二批信息")

    await asyncio.sleep(15)

    await agent_B.send_message('你的功能有哪些？')
    print("发送第三批信息")
    
    # # 如果是服务器里持续监听，则不需要以下代码：
    # # 等待 AgentB 处理完所有消息
    # await agent_A.queue.join()
    # # 处理完所有消息后，结束while true的无限循环
    # processing_task.cancel()


    # 等待一段时间，观察消息处理情况
    await asyncio.sleep(30)

asyncio.run(main())
