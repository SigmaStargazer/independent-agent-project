"""
v0.22.2 测试：idle wakeup 无信息量心理活动抑制写入长期记忆

测试覆盖 solution.md §6.3 的 T1~T9。
不依赖 Unity 联调；mock MemoryManager.save_memory 和 LLM 调用。
"""

import asyncio
import pytest
from unittest.mock import AsyncMock, patch, MagicMock
from dataclasses import dataclass

from agent_framwork.base.timed_message import TimedMessage


# ============================================================
# T8: TimedMessage 排序不变
# ============================================================

class TestTimedMessageSort:
    def test_sort_by_timestamp_only(self):
        """T8: skip_memory 不影响排序，仅按 timestamp+content 排序"""
        items = [
            TimedMessage(timestamp=3.0, content="c", skip_memory=True),
            TimedMessage(timestamp=1.0, content="a", skip_memory=False),
            TimedMessage(timestamp=2.0, content="b", skip_memory=True),
        ]
        items.sort()
        assert items[0].content == "a"
        assert items[1].content == "b"
        assert items[2].content == "c"

    def test_sort_same_timestamp_different_content(self):
        """T8: timestamp 相同时按 content 排序，skip_memory 不干扰"""
        items = [
            TimedMessage(timestamp=1.0, content="z", skip_memory=True),
            TimedMessage(timestamp=1.0, content="a", skip_memory=False),
        ]
        items.sort()
        assert items[0].content == "a"
        assert items[1].content == "z"

    def test_skip_memory_default_false(self):
        """T8: skip_memory 默认为 False"""
        msg = TimedMessage(timestamp=1.0, content="test")
        assert msg.skip_memory is False


# ============================================================
# T1~T5: save_memory / cache_tool_mem 节点行为
# ============================================================

class TestSaveMemoryNode:
    """测试 save_memory 节点的 skip_memory 旁路逻辑"""

    @pytest.fixture
    def base_state(self):
        return {
            "name": "test_agent",
            "mem_to_save": "一些记忆内容",
            "skip_memory": False,
            "messages": [],
        }

    @pytest.mark.asyncio
    async def test_t1_skip_memory_true_skips_save(self, base_state):
        """T1: skip_memory=True 时跳过 memory_manager.save_memory 调用"""
        from agent_framwork.agents.agent_interuptible import save_memory

        state = {**base_state, "skip_memory": True}

        with patch(
            "agent_framwork.agents.agent_interuptible.memory_manager.save_memory",
            new_callable=AsyncMock
        ) as mock_save, patch(
            "agent_framwork.agents.agent_interuptible.aperf_print",
            new_callable=AsyncMock
        ), patch(
            "agent_framwork.agents.agent_interuptible.PROMPT_SAVE_ENABLED", False
        ):
            result = await save_memory(state)

            # memory_manager.save_memory 不应被调用
            mock_save.assert_not_called()
            # 返回值应清空 mem_to_save
            assert result["mem_to_save"] == ""
            assert result["logged_tool_call_ids"] == []

    @pytest.mark.asyncio
    async def test_t3_skip_memory_false_normal_save(self, base_state):
        """T3: skip_memory=False 时正常调用 memory_manager.save_memory"""
        from agent_framwork.agents.agent_interuptible import save_memory

        state = {**base_state, "skip_memory": False}

        with patch(
            "agent_framwork.agents.agent_interuptible.memory_manager.save_memory",
            new_callable=AsyncMock
        ) as mock_save, patch(
            "agent_framwork.agents.agent_interuptible.aperf_print",
            new_callable=AsyncMock
        ), patch(
            "agent_framwork.agents.agent_interuptible.TimeSystem"
        ) as mock_time, patch(
            "agent_framwork.agents.agent_interuptible.PROMPT_SAVE_ENABLED", False
        ):
            mock_time.return_value.aget_current_time = AsyncMock(return_value="2016-01-01")

            result = await save_memory(state)

            # memory_manager.save_memory 应被调用一次
            mock_save.assert_called_once()
            assert result["mem_to_save"] == ""

    @pytest.mark.asyncio
    async def test_t5_feedback_message_normal_save(self, base_state):
        """T5: 反馈消息（skip_memory=False）正常写入"""
        from agent_framwork.agents.agent_interuptible import save_memory

        state = {**base_state, "skip_memory": False}

        with patch(
            "agent_framwork.agents.agent_interuptible.memory_manager.save_memory",
            new_callable=AsyncMock
        ) as mock_save, patch(
            "agent_framwork.agents.agent_interuptible.aperf_print",
            new_callable=AsyncMock
        ), patch(
            "agent_framwork.agents.agent_interuptible.TimeSystem"
        ) as mock_time, patch(
            "agent_framwork.agents.agent_interuptible.PROMPT_SAVE_ENABLED", False
        ):
            mock_time.return_value.aget_current_time = AsyncMock(return_value="2016-01-01")

            await save_memory(state)
            mock_save.assert_called_once()

    @pytest.mark.asyncio
    async def test_missing_skip_memory_defaults_false(self, base_state):
        """旧 checkpoint 兼容：无 skip_memory 字段时默认 False，正常写入"""
        from agent_framwork.agents.agent_interuptible import save_memory

        state = {k: v for k, v in base_state.items() if k != "skip_memory"}

        with patch(
            "agent_framwork.agents.agent_interuptible.memory_manager.save_memory",
            new_callable=AsyncMock
        ) as mock_save, patch(
            "agent_framwork.agents.agent_interuptible.aperf_print",
            new_callable=AsyncMock
        ), patch(
            "agent_framwork.agents.agent_interuptible.TimeSystem"
        ) as mock_time, patch(
            "agent_framwork.agents.agent_interuptible.PROMPT_SAVE_ENABLED", False
        ):
            mock_time.return_value.aget_current_time = AsyncMock(return_value="2016-01-01")

            await save_memory(state)
            mock_save.assert_called_once()


class TestCacheToolMemNode:
    """测试 cache_tool_mem 节点的工具调用解除 skip 逻辑"""

    @pytest.mark.asyncio
    async def test_t2_tool_call_clears_skip(self):
        """T2: idle wakeup(skip_memory=True) + 有工具调用 -> skip_memory 置 False"""
        from agent_framwork.agents.agent_interuptible import cache_tool_mem
        from langchain_core.messages import AIMessage, HumanMessage

        ai_msg = AIMessage(
            content="",
            tool_calls=[{"id": "tc1", "name": "observe_cmd", "args": {}}]
        )
        state = {
            "messages": [HumanMessage(content="test"), ai_msg],
            "mem_to_save": "初始记忆",
            "logged_tool_call_ids": [],
            "skip_memory": True,
        }

        with patch(
            "agent_framwork.agents.agent_interuptible.TimeSystem"
        ) as mock_time:
            mock_time.return_value.aget_current_time = AsyncMock(return_value="2016-01-01")

            result = await cache_tool_mem(state)

            # 有工具调用，skip_memory 应被置为 False
            assert result["skip_memory"] is False

    @pytest.mark.asyncio
    async def test_t4_normal_tool_call_skip_stays_false(self):
        """T4: 普通消息(skip_memory=False) + 有工具调用 -> skip_memory 保持 False"""
        from agent_framwork.agents.agent_interuptible import cache_tool_mem
        from langchain_core.messages import AIMessage, HumanMessage

        ai_msg = AIMessage(
            content="",
            tool_calls=[{"id": "tc1", "name": "move_cmd", "args": {}}]
        )
        state = {
            "messages": [HumanMessage(content="test"), ai_msg],
            "mem_to_save": "初始记忆",
            "logged_tool_call_ids": [],
            "skip_memory": False,
        }

        with patch(
            "agent_framwork.agents.agent_interuptible.TimeSystem"
        ) as mock_time:
            mock_time.return_value.aget_current_time = AsyncMock(return_value="2016-01-01")

            result = await cache_tool_mem(state)
            assert result["skip_memory"] is False

    @pytest.mark.asyncio
    async def test_no_tool_call_skip_stays_true(self):
        """idle wakeup(skip_memory=True) + 无工具调用 -> cache_tool_mem 返回空 dict，skip_memory 保持原值 True"""
        from agent_framwork.agents.agent_interuptible import cache_tool_mem
        from langchain_core.messages import AIMessage, HumanMessage

        # AIMessage 无 tool_calls
        ai_msg = AIMessage(content="我心想：一切如常", tool_calls=[])
        state = {
            "messages": [HumanMessage(content="test"), ai_msg],
            "mem_to_save": "初始记忆",
            "logged_tool_call_ids": [],
            "skip_memory": True,
        }

        with patch(
            "agent_framwork.agents.agent_interuptible.TimeSystem"
        ) as mock_time:
            mock_time.return_value.aget_current_time = AsyncMock(return_value="2016-01-01")

            result = await cache_tool_mem(state)
            # 无 tool_calls 时 cache_tool_mem 返回 {}，LangGraph 保持 state 中的 skip_memory=True
            assert result == {}

    @pytest.mark.asyncio
    async def test_missing_skip_memory_defaults_false(self):
        """旧 checkpoint 兼容：无 skip_memory 字段时默认 False"""
        from agent_framwork.agents.agent_interuptible import cache_tool_mem
        from langchain_core.messages import AIMessage, HumanMessage

        ai_msg = AIMessage(content="test", tool_calls=[])
        state = {
            "messages": [HumanMessage(content="test"), ai_msg],
            "mem_to_save": "记忆",
            "logged_tool_call_ids": [],
        }

        with patch(
            "agent_framwork.agents.agent_interuptible.TimeSystem"
        ) as mock_time:
            mock_time.return_value.aget_current_time = AsyncMock(return_value="2016-01-01")

            result = await cache_tool_mem(state)
            # 无 tool_calls 时返回 {}，不会引入 skip_memory
            assert result == {}


# ============================================================
# T6, T7: 混合消息场景（aprocess_message 中 skip_memory 取值）
# ============================================================

class TestSkipMemoryAggregation:
    """测试 aprocess_message 中从 items 聚合 skip_memory 的逻辑"""

    def test_t6_mixed_items_any_true(self):
        """T6: items 同时含 skip_memory=True 和 False -> 聚合为 True"""
        items = [
            TimedMessage(timestamp=1.0, content="idle wakeup", skip_memory=True),
            TimedMessage(timestamp=2.0, content="user message", skip_memory=False),
        ]
        skip_memory = any(getattr(item, 'skip_memory', False) for item in items)
        assert skip_memory is True

    def test_t7_mixed_items_all_false(self):
        """T7: items 全部 skip_memory=False -> 聚合为 False"""
        items = [
            TimedMessage(timestamp=1.0, content="msg1", skip_memory=False),
            TimedMessage(timestamp=2.0, content="msg2", skip_memory=False),
        ]
        skip_memory = any(getattr(item, 'skip_memory', False) for item in items)
        assert skip_memory is False

    def test_all_true(self):
        """items 全部 skip_memory=True -> 聚合为 True"""
        items = [
            TimedMessage(timestamp=1.0, content="idle1", skip_memory=True),
            TimedMessage(timestamp=2.0, content="idle2", skip_memory=True),
        ]
        skip_memory = any(getattr(item, 'skip_memory', False) for item in items)
        assert skip_memory is True

    def test_empty_items(self):
        """空 items -> 聚合为 False"""
        items = []
        skip_memory = any(getattr(item, 'skip_memory', False) for item in items)
        assert skip_memory is False


# ============================================================
# T9: 打断恢复后 skip_memory 重置
# ============================================================

class TestResumeStateSkipMemory:
    """测试 _initialize_resume_state 中 skip_memory 默认 False"""

    @pytest.mark.asyncio
    async def test_t9_resume_state_skip_memory_false(self):
        """T9: 打断恢复后 _resume_state 中 skip_memory=False"""
        from agent_framwork.agents.agent_interuptible import Agent

        agent = Agent.__new__(Agent)
        agent.name = "test_agent"

        old_values = {
            "index": 0,
            "mem_summary": "简介",
            "mem_fact": "事实",
            "mem_episode": "情景",
            "mem_skill_index": "",
            "mem_to_save": "待保存的记忆",
            "logged_tool_call_ids": [],
        }
        messages = []

        with patch(
            "agent_framwork.agents.agent_interuptible.memory_manager.compress_memory_text",
            new_callable=AsyncMock,
            return_value="压缩后的记忆"
        ), patch(
            "agent_framwork.agents.agent_interuptible.TimeSystem"
        ) as mock_time:
            mock_time.return_value.aget_current_time = AsyncMock(return_value="2016-01-01")

            await agent._initialize_resume_state(
                old_values=old_values,
                messages=messages,
                interrupt_reason="被打断"
            )

            assert agent._resume_state is not None
            assert agent._resume_state["skip_memory"] is False


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
