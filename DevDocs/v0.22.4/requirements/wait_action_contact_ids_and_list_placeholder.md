# 需求：WaitAction 补齐 allowed_contact_obj_ids + List[int] 模板占位符支持

> 来源：`DevDocs/需求池/backlog.md` 条目 1（P0）、条目 2（P0）
> 日期：2026-07-10

## 一、条目 1：WaitAction 缺 allowed_contact_obj_ids

### 问题

训练日志（2026-06-23_13-41-56）中，小明掌握了「乘平台渡陷阱」的总体思路（等平台到近端 -> 走上平台 -> wait actionTime >= 5 -> 走下平台）后，仍然反复触发「触碰到: 2. 陷阱」失败（line 2920、5244、5415、5584、6105）。

根因：`WaitAction` 没有 `allowed_contact_obj_ids` 字段，只有 `MoveAction` 有。Agent 无法表达「我站着等的这 5 秒里，允许跟陷阱（2）和平台（3）发生接触」。wait 期间平台移动穿过陷阱区域时，Agent 与陷阱的接触被判定为碰撞，动作序列中断。

### 期望

给 `WaitAction` 加上与 `MoveAction` 同名同义的 `allowed_contact_obj_ids` 字段，让 Agent 能表达「等待期间允许接触哪些物体」。

## 二、条目 2：List[int] 字段在模板里的占位符表达边界

### 问题

v0.21.6 让模板可以内联 `{snake_case}` 占位符，但占位符只能写在 JSON 字符串里。`allowed_contact_obj_ids` 是 `List[int]`，Agent 在日志中写出 `"allowed_contact_obj_ids": [{platform_index}]`（line 6760、6781），被「不是合法 JSON」拦下。Agent 只能把字段留空 + 在 `usage_notes` 里写「需手动填入平台序号」，复用时容易漏填。

### 期望

让 `List[int]` 字段也能在模板里参数化，使 Agent 沉淀的模板可以直接用占位符表达「这里的物体序号执行时再填」。
