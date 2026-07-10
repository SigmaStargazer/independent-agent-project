from pydantic import Field
from typing import Literal, List
from .base_action import BaseAction, StateChangeAction

class WaitAction(StateChangeAction):
    action: Literal["wait"] = Field(default="wait", description="原地等待，直至满足条件") #要作为Annotated的判断条件，必须是Literal
    allowed_contact_obj_ids: List[int] = Field(
        default_factory=list,
        description="等待期间允许接触的物体序号列表，如站在移动平台上等待时填写平台与陷阱的物体序号。当接触到列表以外的物体时，会中断动作序列。若无则填空列表[]。"
    )

class MoveAction(StateChangeAction):
    action: Literal["move"] = Field(default="move", description="移动，直至满足条件")
    direction: Literal["left", "right"] = Field(..., description="移动方向")
    allowed_contact_obj_ids: List[int] = Field(
        ...,
        description="移动过程中允许接触的物体序号列表，如推箱子时填写箱子的物体序号。当撞上到列表以外的物体时，会中断动作序列。若无则填空列表[]。"
    )

class InteractAction(BaseAction):
    action: Literal["interact"] = Field(
        default="interact", 
        description="与设备进行交互。")

class SelectAction(BaseAction):
    action: Literal["select"] = Field(
        default="select", 
        description="当设备提供了选项时，选择其中一个选项。")
    selection: int = Field(
        ..., description="选择编号"
    )

class InputAction(BaseAction):
    action: Literal["input"] = Field(
        default="input", 
        description="当设备提供了输入框时，向设备输入文本。")
    input_text: str = Field(
        ..., description="输入文本"
    )