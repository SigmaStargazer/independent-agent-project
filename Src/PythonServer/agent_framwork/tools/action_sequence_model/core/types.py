from enum import Enum
from dataclasses import dataclass
from typing import Optional, Set

class AccessKind(Enum):
    SCALAR = "scalar"      # 只能直接用
    OBJECT = "object"      # 允许 .member
    VECTOR2 = "vector2"    # 特殊对象
    LIST = "list"          # 允许 [i]

@dataclass(frozen=True)
class ConditionVariable:
    name: str
    desc: str
    kind: AccessKind
    members: Optional[Set[str]] = None

# 1. 变量类型管理
CONDITION_VARIABLES = {
    "myself": ConditionVariable(
        name="myself",
        desc="你自身的数据。是一个sceneObj对象。",
        kind=AccessKind.OBJECT,
        members={"Position", "Velocity", "State"}
    ),
    "objects": ConditionVariable(
        name="objects",
        desc="场景中其他物体的数据的列表。是一个List[sceneObj]。",
        kind=AccessKind.LIST,
        members={"Position", "Velocity", "State"},
    ),
    "displacement": ConditionVariable(
        name="displacement",
        desc="当前动作开始至今的横向位移。是一个float。",
        kind=AccessKind.SCALAR
    ),
    # "displacement": ConditionVariable(
    #     name="displacement",
    #     desc="当前动作开始至今的位移。是一个Vector2。",
    #     kind=AccessKind.VECTOR2,
    #     members={"x", "y"}
    # ),
    "actionTime": ConditionVariable(
        name="actionTime",
        desc="当前动作开始至今所经过的时间。是一个float。",
        kind=AccessKind.SCALAR
    ),
}

# 2. 变量描述管理
def _get_conditions_desc():
    return "\n".join([f"{k}: {v.desc}" for k, v in CONDITION_VARIABLES.items()])

ACTION_DESC = "要执行的动作类型"
CONDITION_DESC = f"""移动结束条件（DynamicExpresso 表达式）。# 可用变量：
{_get_conditions_desc()}

# sceneObj类的属性：
Position: 物体当前位置。是一个Vector2对象。
Velocity: 物体当前速度。是一个Vector2对象。
State: 物体当前状态，如'Idle'、'Move'等。

# 示例：displacement.x >= 10 && myself.state == 'Move'"""