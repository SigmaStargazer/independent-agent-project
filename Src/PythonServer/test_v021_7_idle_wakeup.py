"""v0.21.7 Idle Wakeup 自测脚本

覆盖 solution.md §6.0 中 T1~T8 共 8 个测试用例。
直接运行：
    cd Src/PythonServer
    python test_v021_7_idle_wakeup.py

不依赖 Unity / LLM / Kuzu / Graphiti。仅 mock 极少量的 random.uniform / asyncio.create_task / DB-side I/O。
"""
import asyncio
import importlib
import json
import os
import sys
import tempfile
import unittest
from unittest import mock


CUR_DIR = os.path.dirname(os.path.abspath(__file__))
if CUR_DIR not in sys.path:
    sys.path.insert(0, CUR_DIR)


def _import_agent_module():
    import agent_framwork.agents.agent_interuptible as ai
    return ai


class IdleWakeupConfigLoaderTest(unittest.TestCase):
    """T1 / T2：load_idle_wakeup_config"""

    def test_T2_real_config_file_has_first_delay_fields(self):
        ai = _import_agent_module()
        cfg = ai.load_idle_wakeup_config()
        self.assertEqual(cfg["first_min_delay_seconds"], 25.0)
        self.assertEqual(cfg["first_max_delay_seconds"], 35.0)
        self.assertEqual(cfg["min_delay_seconds"], 120.0)
        self.assertEqual(cfg["max_delay_seconds"], 300.0)

    def test_T1_missing_file_falls_back_to_defaults(self):
        ai = _import_agent_module()
        bogus_path = os.path.join(tempfile.gettempdir(), "_v0217_not_exist.json")
        if os.path.exists(bogus_path):
            os.remove(bogus_path)
        with mock.patch.object(ai, "IDLE_WAKEUP_CONFIG_PATH", bogus_path):
            cfg = ai.load_idle_wakeup_config()
        self.assertEqual(cfg["first_min_delay_seconds"], 25.0)
        self.assertEqual(cfg["first_max_delay_seconds"], 35.0)
        self.assertEqual(cfg["min_delay_seconds"], 120.0)
        self.assertEqual(cfg["max_delay_seconds"], 300.0)


class FakeAgent:
    """复用 Agent._schedule_idle_wakeup / _cancel_idle_wakeup 的最小宿主。"""

    def __init__(self, ai_module):
        self.name = "T"
        self._running = True
        self._is_graph_running = False
        self._idle_wakeup_seq = 0
        self._idle_wakeup_task = None
        self._pending_first_wakeup = False
        self._interrupt_event = asyncio.Event()
        self.message_queue = asyncio.Queue()
        self.feedback_queue = asyncio.Queue()
        self._is_idle_wakeup_enabled = ai_module.Agent._is_idle_wakeup_enabled.__get__(self, FakeAgent)
        self._can_schedule_idle_wakeup = ai_module.Agent._can_schedule_idle_wakeup.__get__(self, FakeAgent)
        self._cancel_idle_wakeup = ai_module.Agent._cancel_idle_wakeup.__get__(self, FakeAgent)
        self._schedule_idle_wakeup = ai_module.Agent._schedule_idle_wakeup.__get__(self, FakeAgent)

    async def _idle_wakeup_after_delay(self, seq, delay):
        return None


class IdleWakeupScheduleTest(unittest.TestCase):
    """T3 / T4 / T8：_schedule_idle_wakeup 区间选择 + _cancel 不动 flag"""

    def setUp(self):
        self.ai = _import_agent_module()
        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)
        self.agent = FakeAgent(self.ai)

    def tearDown(self):
        self.loop.close()
        asyncio.set_event_loop(None)

    def _run_schedule_with_mocks(self):
        captured = {}

        def fake_uniform(a, b):
            captured["range"] = (a, b)
            return (a + b) / 2

        def fake_create_task(coro):
            coro.close()
            return mock.MagicMock(done=lambda: False, cancel=lambda: None)

        with mock.patch("random.uniform", side_effect=fake_uniform), \
             mock.patch("asyncio.create_task", side_effect=fake_create_task):
            self.agent._schedule_idle_wakeup()
        return captured

    def test_T3_first_wakeup_uses_short_range_and_clears_flag(self):
        self.agent._pending_first_wakeup = True
        captured = self._run_schedule_with_mocks()
        self.assertEqual(captured["range"], (25.0, 35.0))
        self.assertFalse(self.agent._pending_first_wakeup)
        self.assertIsNotNone(self.agent._idle_wakeup_task)

    def test_T4_normal_wakeup_uses_long_range_and_keeps_flag_false(self):
        self.agent._pending_first_wakeup = False
        captured = self._run_schedule_with_mocks()
        self.assertEqual(captured["range"], (120.0, 300.0))
        self.assertFalse(self.agent._pending_first_wakeup)

    def test_T8_cancel_only_does_not_change_flag(self):
        self.agent._pending_first_wakeup = True
        fake_task = mock.MagicMock(done=lambda: False, cancel=mock.MagicMock())
        self.agent._idle_wakeup_task = fake_task
        old_seq = self.agent._idle_wakeup_seq
        self.agent._cancel_idle_wakeup()
        self.assertTrue(self.agent._pending_first_wakeup, "_cancel_idle_wakeup 不应清空 _pending_first_wakeup")
        self.assertEqual(self.agent._idle_wakeup_seq, old_seq + 1)
        fake_task.cancel.assert_called_once()


class AsendMessageSetsFlagTest(unittest.IsolatedAsyncioTestCase):
    """T5：_asend_message 必须把 _pending_first_wakeup 置 True"""

    async def test_T5_asend_message_sets_pending_first_wakeup(self):
        ai = _import_agent_module()
        agent = mock.MagicMock(spec=ai.Agent)
        agent._pending_first_wakeup = False
        agent.runtime_state = {"focus_state": False}
        agent._message_interval = []
        agent._message_lock = asyncio.Lock()
        agent.message_queue = asyncio.Queue()
        agent.feedback_queue = asyncio.Queue()
        agent.name = "T"
        agent._cancel_idle_wakeup = mock.MagicMock()
        agent.ainterrupt = mock.AsyncMock()
        agent.astart = mock.AsyncMock()

        with mock.patch("agent_framwork.agents.agent_interuptible.TimeSystem") as ts:
            ts.return_value.aget_current_time = mock.AsyncMock(return_value="未启动")
            await ai.Agent._asend_message(agent, "hello", is_feedback=False, force_interrupt=False)

        self.assertTrue(agent._pending_first_wakeup, "_asend_message 后必须置 _pending_first_wakeup=True")
        agent._cancel_idle_wakeup.assert_called_once()


class AinterruptKeepsFlagTest(unittest.IsolatedAsyncioTestCase):
    """T6：ainterrupt 不能清空 _pending_first_wakeup（关键纪律点）"""

    async def test_T6_ainterrupt_does_not_clear_flag(self):
        """以源码级文本扫描 + 行为级断言双重保证。"""
        ai = _import_agent_module()
        import inspect
        src = inspect.getsource(ai.Agent.ainterrupt)
        self.assertNotIn(
            "_pending_first_wakeup",
            src,
            "ainterrupt 源码出现 _pending_first_wakeup —— 与 v0.21.7 PRD §3.1.4 纪律冲突，禁止在 ainterrupt 中读写该字段",
        )


class AfinishClearsFlagTest(unittest.IsolatedAsyncioTestCase):
    """T7：afinish 必须把 _pending_first_wakeup 清掉"""

    async def test_T7_afinish_clears_pending_first_wakeup(self):
        ai = _import_agent_module()
        import inspect
        src = inspect.getsource(ai.Agent.afinish)
        self.assertIn(
            "self._pending_first_wakeup = False",
            src,
            "afinish 必须显式置 _pending_first_wakeup=False",
        )


if __name__ == "__main__":
    unittest.main(verbosity=2)
