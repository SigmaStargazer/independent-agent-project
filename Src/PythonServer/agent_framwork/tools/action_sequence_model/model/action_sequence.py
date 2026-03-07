from typing import Annotated, Union, List
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

class ActionSequence(BaseModel):
    action_sequence: List[ActionStep] = Field(
        ..., min_length=1,
        description="按顺序执行的动作序列。每个动作将在满足condition后结束。"
    )