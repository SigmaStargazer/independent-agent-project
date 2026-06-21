"""
INVALID-001：无效测试记录，不作为 v0.21.4 验收依据。

该脚本曾只验证 Python Pydantic 层是否接受 LeftPosition / RightPosition 等
字段名，未覆盖本次修复的核心链路，因此按开发事故记录保留为占位说明。

请运行 test_v021_4_self_test.py 执行当前环境可自测用例。
"""


if __name__ == "__main__":
    raise SystemExit(
        "INVALID-001: 该脚本是无效测试记录，不作为验收依据；"
        "请运行 test_v021_4_self_test.py"
    )
