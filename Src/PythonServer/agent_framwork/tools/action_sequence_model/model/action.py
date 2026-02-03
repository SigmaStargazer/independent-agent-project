from pydantic import Field
from typing import Literal
from .base_action import BaseAction

class WaitAction(BaseAction):
    action: Literal["wait"] = Field(default="wait", description="等待，直至满足条件") #要作为Annotated的判断条件，必须是Literal[

class MoveAction(BaseAction):
    action: Literal["move"] = Field(default="move", description="移动，直至满足条件")
    direction: Literal["left", "right"] = Field(
        ..., description="移动方向"
    )