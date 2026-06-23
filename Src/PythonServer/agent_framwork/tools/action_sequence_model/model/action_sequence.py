from typing import Annotated, Union, List, get_args
from pydantic import Field, BaseModel
from .action import WaitAction, MoveAction, InteractAction, SelectAction, InputAction

# 基于"action"来判断ActionStep的类型
ActionStep = Annotated[
    Union[
        WaitAction,
        MoveAction,
        InteractAction,
        SelectAction,
        InputAction,
    ],
    Field(discriminator="action")
]


def get_known_action_names() -> set[str]:
    """从 ActionStep Union 中自动收集所有合法 action 字面量。

    用于技能模板的宽松校验（参见 skill_tools._parse_action_sequence_template）。
    今后只需把新的 ActionXxx 类挂到 ActionStep Union 中，即会被自动识别。
    """
    names: set[str] = set()
    # ActionStep = Annotated[Union[...], FieldInfo(discriminator="action")]
    # get_args(ActionStep)[0] 是 Union[...]
    union_type = get_args(ActionStep)[0]
    for cls in get_args(union_type):
        action_field = cls.model_fields.get("action")
        if action_field is None:
            continue
        for value in get_args(action_field.annotation):
            if isinstance(value, str):
                names.add(value)
    return names

# class ActionSequence(BaseModel):
#     action_sequence: List[ActionStep] = Field(
#         ..., min_length=1,
#         description="按顺序执行的动作序列。每个动作将在满足condition后结束。"
#     )