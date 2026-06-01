import asyncio
import uuid
from typing import Annotated, List
from pydantic import Field
from agent_framwork.tools.action_sequence_model.model.action_sequence import ActionStep

from langchain_core.runnables import RunnableConfig
from langchain_core.tools import tool, InjectedToolCallId
from typing_extensions import Annotated
from langgraph.prebuilt import InjectedState

from graphiti_core.search import search_config_recipes

# from agent_framwork.tools.action_sequence_model.model.action_sequence import ActionSequence

from agent_framwork.systems.alarm_system import AlarmSystem
from agent_framwork.systems.time_system import TimeSystem

from network.servers import AgentServerNetMessage, TOOL_WAITERS
from network import message_pb2

# tool中调用langgraph中state内参数的方式，请参考下面的 InjectedState 中的内容：
# https://langchain-ai.github.io/langgraph/reference/agents/#langgraph.prebuilt.tool_node.ToolNode.inject_tool_args

TOOL_TIMEOUT = 30#RPC调用超时时间

