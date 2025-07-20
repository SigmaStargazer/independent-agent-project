import sys
import os

# 项目根目录
PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))

# 添加proto路径
sys.path.append(os.path.join(PROJECT_ROOT, 'Lib', 'proto'))

import message_pb2
from agent_framwork.base.singleton import singleton