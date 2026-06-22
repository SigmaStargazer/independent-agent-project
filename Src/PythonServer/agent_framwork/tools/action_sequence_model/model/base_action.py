from pydantic import BaseModel, Field, field_validator
import re
from ..core.constants import (
    STRING_LITERAL_RE,
    SINGLE_QUOTED_STRING_LITERAL_RE,
    VECTOR2_UPPER_MEMBER_RE,
    IDENTIFIER_RE,
    FORBIDDEN_PATTERNS,
)
from ..core.types import CONDITION_VARIABLES, AccessKind, ACTION_DESC, CONDITION_DESC

class BaseAction(BaseModel):
    action: str  = Field(..., description=ACTION_DESC)
    # condition: str = Field(
    #     default="",
    #     description=CONDITION_DESC
    # )

class StateChangeAction(BaseAction):
    condition: str = Field(..., description=CONDITION_DESC)
    @field_validator("condition")
    @classmethod
    def validate_condition(cls, expr):

        if not expr.strip():
            raise ValueError("condition 不能为空")

        single_quote_match = SINGLE_QUOTED_STRING_LITERAL_RE.search(expr)
        if single_quote_match:
            literal = single_quote_match.group(0)
            value = literal[1:-1]
            raise ValueError(
                "condition 中的字符串必须使用双引号，不能使用单引号；"
                f"请将 {literal} 改为 \"{value}\""
            )

        upper_member_match = VECTOR2_UPPER_MEMBER_RE.search(expr)
        if upper_member_match:
            field_name = upper_member_match.group(1)
            raise ValueError(
                "condition 中 Vector2 坐标字段必须使用小写 .x / .y；"
                f"请将 .{field_name} 改为 .{field_name.lower()}"
            )

        # ---------- 0. 去掉字符串字面量 ----------
        expr_no_string = STRING_LITERAL_RE.sub("''", expr)

        # ---------- 1. 黑名单 ----------
        for pattern in FORBIDDEN_PATTERNS:
            if re.search(pattern, expr_no_string):
                raise ValueError(f"condition 包含非法语法: {pattern}")

        # ---------- 2. 提取所有标识符 ----------
        identifiers = set(IDENTIFIER_RE.findall(expr_no_string))
        if not identifiers:
            raise ValueError("condition 不能为空")

        # ---------- 3. 校验根变量 ----------
        for ident in identifiers:
            if ident in CONDITION_VARIABLES:
                continue
            if ident in {"true", "false", "null", "Math"}:
                continue

            if f".{ident}" in expr_no_string:
                continue

            raise ValueError(f"condition 使用了未注册变量: {ident}")

        # ---------- 4. 访问结构校验 ----------
        cls._validate_access(expr_no_string)

        return expr

    @staticmethod
    def _validate_access(expr: str):
        for var in CONDITION_VARIABLES.values():
            name = var.name

            # ---- 标量：不能 . 或 []
            if var.kind == AccessKind.SCALAR:
                if re.search(rf"\b{name}\s*[\.\[]", expr):
                    raise ValueError(f"{name} 是标量，不能成员或索引访问")

            # ---- OBJECT / VECTOR2：只能 .member
            elif var.kind in (AccessKind.OBJECT, AccessKind.VECTOR2):
                if re.search(rf"\b{name}\s*\[", expr):
                    raise ValueError(f"{name} 不允许索引访问")

                for m in re.finditer(rf"\b{name}\.(\w+)", expr):
                    member = m.group(1)
                    if var.members and member not in var.members:
                        raise ValueError(f"{name} 不允许访问成员 {member}")

            # ---- LIST：只能 objects[i].member
            elif var.kind == AccessKind.LIST:
                if re.search(rf"\b{name}\.(\w+)", expr):
                    raise ValueError(f"{name} 必须通过索引访问，如 {name}[i].X")

                for m in re.finditer(rf"\b{name}\[(\d+)\]\.(\w+)", expr):
                    member = m.group(2)
                    if var.members and member not in var.members:
                        raise ValueError(
                            f"{name}[i] 不允许访问成员 {member}"
                        )