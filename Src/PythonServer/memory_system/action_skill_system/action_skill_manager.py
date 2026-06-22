# -*- coding: utf-8 -*-
"""ActionSkillManager: 单例，管理 ActionSkill / ActionSequenceTemplate 在 Kuzu 中的 CRUD + RAG。

设计要点：
- Kuzu 连接来自 DBConnectionService（不持有，每次取）
- Embedding 来自 EmbedderService（不持有，每次取）
- 写操作复用 DBConnectionService.access() 上下文，避免 backup 期间写坏数据
- ActionSequenceTemplate 的 description 在写入时计算 embedding 并存入 Kuzu，避免 RAG 时重算
- 对外接口以 name 为参数，内部按 name 查 uuid 再操作
"""
from __future__ import annotations

import asyncio
import json
import math
import traceback
from typing import List, Optional, Tuple

from agent_framwork.base.singleton import singleton
from .skill_model import (
    ActionSkill,
    ActionSequenceTemplate,
    normalize_step_explanations,
)
from memory_system.db_conn import DBConnectionService
from memory_system.embedder import EmbedderService


# 余弦相似度（手动实现以避免 numpy 依赖）
def _cosine_similarity(a: List[float], b: List[float]) -> float:
    if not a or not b or len(a) != len(b):
        return 0.0
    dot = 0.0
    na = 0.0
    nb = 0.0
    for x, y in zip(a, b):
        dot += x * y
        na += x * x
        nb += y * y
    if na == 0 or nb == 0:
        return 0.0
    return dot / (math.sqrt(na) * math.sqrt(nb))


@singleton
class ActionSkillManager:
    def __init__(self):
        self._initialized = False
        self._init_lock: Optional[asyncio.Lock] = None

    def _conn(self):
        """每次取 dbsvc 当前连接，避免持有引用导致文件锁残留。"""
        return DBConnectionService().get_conn()

    def _memory_access(self):
        """使用 DBConnectionService 的访问门，与 backup freeze 联动。"""
        return DBConnectionService().access()

    # ------------------------------------------------------------------
    # 初始化与 schema
    # ------------------------------------------------------------------
    async def initialize(self) -> "ActionSkillManager":
        """无参数；依赖 DBConnectionService / EmbedderService 已经 initialize。"""
        if self._initialized:
            return self
        if self._init_lock is None:
            self._init_lock = asyncio.Lock()

        async with self._init_lock:
            if self._initialized:
                return self
            await self._setup_schema()
            self._initialized = True
            print("[ActionSkillManager] initialized")
        return self

    def reset_for_reinitialize(self):
        """供 backup/restore 后调用，下次 initialize 重新执行 schema 检测。"""
        self._initialized = False

    async def _setup_schema(self):
        """创建表结构（IF NOT EXISTS 幂等）。

        注意：Kuzu prepared statement 解析器把字段名 `description` 当作了某种特殊 token
        （类似 issue #2676 中的 iri/from/to/key/desc 等保留字坑），未转义时 SET 多属性
        与 CREATE 节点都会解析失败。最终方案是字段名仍用 `description`，但所有 Cypher
        中对 description 字段的引用都用反引号转义：t.\\`description\\`。
        Python 层 dataclass / 工具参数 / 返回值键名都是 description（无感知）。
        """
        # 兼容旧 schema（v0.21.0 dev 期间曾用过 desc / descr 字段名）：
        # 检测当前表是否是带 description 字段的最终 schema；不匹配就 drop 重建。
        need_rebuild = False
        try:
            probe = await self._conn().execute(
                "MATCH (t:ActionSequenceTemplate) RETURN t.`description` AS d, t.step_explanations AS se LIMIT 1"
            )
            list(probe.rows_as_dict())
        except Exception:
            need_rebuild = True
        if not need_rebuild:
            try:
                probe = await self._conn().execute(
                    "MATCH (s:ActionSkill) RETURN s.`description` AS d LIMIT 1"
                )
                list(probe.rows_as_dict())
            except Exception:
                need_rebuild = True

        if need_rebuild:
            for tbl in ("HAS_TEMPLATE", "ActionSequenceTemplate", "ActionSkill"):
                try:
                    await self._conn().execute(f"DROP TABLE {tbl}")
                    print(f"[ActionSkillManager] dropped legacy table {tbl}")
                except Exception:
                    pass

        await self._conn().execute("""
        CREATE NODE TABLE IF NOT EXISTS ActionSkill (
            uuid STRING,
            name STRING,
            group_id STRING,
            `description` STRING,
            content STRING,
            version INT64 DEFAULT 1,
            source STRING DEFAULT 'learned',
            created_at STRING,
            updated_at STRING,
            PRIMARY KEY (uuid)
        )
        """)
        await self._conn().execute("""
        CREATE NODE TABLE IF NOT EXISTS ActionSequenceTemplate (
            uuid STRING,
            skill_uuid STRING,
            name STRING,
            group_id STRING,
            `description` STRING,
            `description_embedding` DOUBLE[],
            action_sequence_template STRING,
            step_explanations STRING,
            usage_notes STRING,
            created_at STRING,
            updated_at STRING,
            PRIMARY KEY (uuid)
        )
        """)
        # 关系表：HAS_TEMPLATE，方便后续按图查询；目前外键 skill_uuid 已足够
        try:
            await self._conn().execute("""
            CREATE REL TABLE IF NOT EXISTS HAS_TEMPLATE (
                FROM ActionSkill TO ActionSequenceTemplate,
                group_id STRING
            )
            """)
        except Exception as e:
            # 关系表创建失败不阻塞业务，仍然可以靠 skill_uuid 外键工作
            print(f"[ActionSkillManager] HAS_TEMPLATE rel table warn: {e}")

    # ------------------------------------------------------------------
    # 私有：embedding helper
    # ------------------------------------------------------------------
    async def _embed(self, text: str) -> List[float]:
        """计算单条文本的 embedding。失败返回空列表（写入时存空，不阻塞）。"""
        if not text:
            return []
        try:
            embedder = EmbedderService().get_embedder()
        except RuntimeError:
            return []
        try:
            # graphiti_core 的 OpenAIEmbedder.create 接收 input_data: list[str]，返回单条向量
            result = await embedder.create(input_data=[text])
            # 兼容不同 embedder 返回格式
            if isinstance(result, list):
                if result and isinstance(result[0], list):
                    return list(result[0])
                return [float(x) for x in result]
            return []
        except Exception as e:
            print(f"[ActionSkillManager] embed failed: {e}")
            return []

    # ------------------------------------------------------------------
    # 内部：uuid 查找
    # ------------------------------------------------------------------
    async def _find_skill_uuid(self, group_id: str, name: str) -> Optional[str]:
        cypher = (
            "MATCH (s:ActionSkill) "
            "WHERE s.group_id = $gid AND s.name = $name "
            "RETURN s.uuid AS uuid LIMIT 1"
        )
        result = await self._conn().execute(cypher, {"gid": group_id, "name": name})
        for row in result.rows_as_dict():
            return row.get("uuid")
        return None

    async def _find_template_uuid(self, skill_uuid: str, template_name: str) -> Optional[str]:
        cypher = (
            "MATCH (t:ActionSequenceTemplate) "
            "WHERE t.skill_uuid = $sid AND t.name = $name "
            "RETURN t.uuid AS uuid LIMIT 1"
        )
        result = await self._conn().execute(
            cypher, {"sid": skill_uuid, "name": template_name}
        )
        for row in result.rows_as_dict():
            return row.get("uuid")
        return None

    # ------------------------------------------------------------------
    # 公开：创建技能（含首个模板）
    # ------------------------------------------------------------------
    async def create_skill(
        self,
        group_id: str,
        skill: ActionSkill,
        curtime: str,
    ) -> None:
        """创建一个新技能。skill.templates 必须包含至少一个模板（业务约束）。"""
        if not skill.name:
            raise ValueError("ActionSkill.name 不能为空")
        skill.group_id = group_id
        if not skill.templates:
            raise ValueError("create_skill 必须至少包含一个 ActionSequenceTemplate")

        async with self._memory_access():
            # 重名检查
            existing = await self._find_skill_uuid(group_id, skill.name)
            if existing is not None:
                raise ValueError(
                    f"技能名 '{skill.name}' 已存在，"
                    f"请改用 add_action_skill_template 添加新场景的模板"
                )

            skill.created_at = curtime
            skill.updated_at = curtime

            # 写 ActionSkill 节点
            await self._conn().execute(
                "CREATE (s:ActionSkill {uuid: $uuid, name: $name, group_id: $gid, "
                "`description`: $description, content: $content, "
                "version: $version, source: $source, "
                "created_at: $created_at, updated_at: $updated_at})",
                {
                    "uuid": skill.uuid, "name": skill.name, "gid": group_id,
                    "description": skill.description, "content": skill.content,
                    "version": int(skill.version), "source": skill.source,
                    "created_at": curtime, "updated_at": curtime,
                },
            )
            # 写每个模板
            for tmpl in skill.templates:
                tmpl.skill_uuid = skill.uuid
                tmpl.group_id = group_id
                await self._insert_template(tmpl, curtime, check_unique=False)

    async def _insert_template(
        self,
        tmpl: ActionSequenceTemplate,
        curtime: str,
        check_unique: bool = True,
    ) -> None:
        """物理插入一行模板。check_unique=True 时先做 (skill_uuid, name) 唯一检查。"""
        if not tmpl.name:
            raise ValueError("ActionSequenceTemplate.name 不能为空")
        if check_unique:
            existing = await self._find_template_uuid(tmpl.skill_uuid, tmpl.name)
            if existing is not None:
                raise ValueError(
                    f"模板名 '{tmpl.name}' 在该技能下已存在，请换一个名称"
                )

        tmpl.step_explanations = normalize_step_explanations(
            tmpl.step_explanations,
            len(tmpl.action_sequence_template),
            require_complete=bool(tmpl.step_explanations),
        )

        if not tmpl.description_embedding:
            tmpl.description_embedding = await self._embed(tmpl.description)
        tmpl.created_at = tmpl.created_at or curtime
        tmpl.updated_at = curtime

        await self._conn().execute(
            "CREATE (t:ActionSequenceTemplate {uuid: $uuid, skill_uuid: $sid, "
            "name: $name, group_id: $gid, "
            "`description`: $description, `description_embedding`: $emb, "
            "action_sequence_template: $seq, step_explanations: $step_explanations, "
            "usage_notes: $notes, created_at: $created_at, updated_at: $updated_at})",
            {
                "uuid": tmpl.uuid, "sid": tmpl.skill_uuid, "name": tmpl.name,
                "gid": tmpl.group_id, "description": tmpl.description,
                "emb": tmpl.description_embedding,
                "seq": json.dumps(tmpl.action_sequence_template, ensure_ascii=False),
                "step_explanations": json.dumps(tmpl.step_explanations_dicts(), ensure_ascii=False),
                "notes": tmpl.usage_notes,
                "created_at": tmpl.created_at, "updated_at": curtime,
            },
        )
        # 关系（best-effort）
        try:
            await self._conn().execute(
                "MATCH (s:ActionSkill {uuid: $sid}), "
                "(t:ActionSequenceTemplate {uuid: $tid}) "
                "CREATE (s)-[:HAS_TEMPLATE {group_id: $gid}]->(t)",
                {"sid": tmpl.skill_uuid, "tid": tmpl.uuid, "gid": tmpl.group_id},
            )
        except Exception:
            pass

    async def create_skill_from_dict(
        self,
        group_id: str,
        skill_data: dict,
        curtime: str,
    ) -> None:
        """从字典创建技能，主要用于默认技能注入（强制 source=default）。

        skill_data 格式参见 default_skills.yaml。
        """
        templates = []
        for t in skill_data.get("templates", []) or []:
            seq = t.get("action_sequence_template", [])
            if isinstance(seq, str):
                try:
                    seq = json.loads(seq)
                except Exception:
                    seq = []
            templates.append(ActionSequenceTemplate(
                name=t.get("name", ""),
                description=t.get("description", ""),
                action_sequence_template=seq,
                step_explanations=normalize_step_explanations(
                    t.get("step_explanations", []) or [],
                    len(seq),
                    require_complete=bool(t.get("step_explanations")),
                ),
                usage_notes=t.get("usage_notes", ""),
            ))
        skill = ActionSkill(
            name=skill_data.get("name", ""),
            description=skill_data.get("description", ""),
            content=skill_data.get("content", ""),
            source="default",  # 导入时统一为 default
            templates=templates,
        )
        await self.create_skill(group_id, skill, curtime)

    # ------------------------------------------------------------------
    # 公开：追加模板
    # ------------------------------------------------------------------
    async def add_template(
        self,
        group_id: str,
        skill_name: str,
        template: ActionSequenceTemplate,
        curtime: str,
    ) -> None:
        async with self._memory_access():
            skill_uuid = await self._find_skill_uuid(group_id, skill_name)
            if skill_uuid is None:
                raise ValueError(
                    f"技能 '{skill_name}' 不存在，请先用 create_action_skill 创建"
                )
            template.skill_uuid = skill_uuid
            template.group_id = group_id
            await self._insert_template(template, curtime, check_unique=True)

    # ------------------------------------------------------------------
    # 公开：精进
    # ------------------------------------------------------------------
    async def refine_skill(
        self,
        group_id: str,
        skill_name: str,
        curtime: str,
        template_name: str = "",
        new_content: str = "",
        new_template_description: str = "",
        new_template: Optional[List[dict]] = None,
        new_step_explanations: Optional[list] = None,
        new_usage_notes: str = "",
    ) -> None:
        """精进技能。任何字段变化都使 ActionSkill.version + 1，并更新 updated_at。

        - template_name 留空：仅精进技能层（content）
        - template_name 非空：精进该模板（description / template / usage_notes）
        """
        async with self._memory_access():
            skill_uuid = await self._find_skill_uuid(group_id, skill_name)
            if skill_uuid is None:
                raise ValueError(f"技能 '{skill_name}' 不存在")

            updated_anything = False

            # 1) 技能层更新
            if new_content:
                await self._conn().execute(
                    "MATCH (s:ActionSkill) "
                    "WHERE s.uuid = $uuid "
                    "SET s.content = $content, s.source = 'refined'",
                    {"uuid": skill_uuid, "content": new_content},
                )
                updated_anything = True

            # 2) 模板层更新
            if template_name:
                tmpl_uuid = await self._find_template_uuid(skill_uuid, template_name)
                if tmpl_uuid is None:
                    raise ValueError(
                        f"模板 '{template_name}' 在技能 '{skill_name}' 下不存在"
                    )
                if (
                    new_template_description
                    or new_template is not None
                    or new_step_explanations is not None
                    or new_usage_notes
                ):
                    set_parts = []
                    params = {"uuid": tmpl_uuid, "ut": curtime}
                    existing_seq = None
                    if new_step_explanations is not None and new_template is None:
                        result = await self._conn().execute(
                            "MATCH (t:ActionSequenceTemplate) WHERE t.uuid = $uuid "
                            "RETURN t.action_sequence_template AS seq LIMIT 1",
                            {"uuid": tmpl_uuid},
                        )
                        for row in result.rows_as_dict():
                            try:
                                existing_seq = json.loads(row.get("seq", "") or "[]")
                            except Exception:
                                existing_seq = []
                            break
                    if new_template_description:
                        set_parts.append("t.`description` = $description")
                        set_parts.append("t.`description_embedding` = $emb")
                        params["description"] = new_template_description
                        params["emb"] = await self._embed(new_template_description)
                    if new_template is not None:
                        set_parts.append("t.action_sequence_template = $seq")
                        params["seq"] = json.dumps(new_template, ensure_ascii=False)
                    if new_step_explanations is not None:
                        step_count = len(new_template if new_template is not None else (existing_seq or []))
                        normalized_explanations = normalize_step_explanations(
                            new_step_explanations,
                            step_count,
                            require_complete=True,
                        )
                        set_parts.append("t.step_explanations = $step_explanations")
                        params["step_explanations"] = json.dumps(
                            [item.to_dict() for item in normalized_explanations],
                            ensure_ascii=False,
                        )
                    if new_usage_notes:
                        set_parts.append("t.usage_notes = $notes")
                        params["notes"] = new_usage_notes
                    set_parts.append("t.updated_at = $ut")
                    cypher = (
                        "MATCH (t:ActionSequenceTemplate) "
                        "WHERE t.uuid = $uuid "
                        "SET " + ", ".join(set_parts)
                    )
                    await self._conn().execute(cypher, params)
                    updated_anything = True

            if not updated_anything:
                return

            # 3) 技能 version + 1，updated_at
            await self._conn().execute(
                "MATCH (s:ActionSkill) "
                "WHERE s.uuid = $uuid "
                "SET s.version = s.version + 1, s.updated_at = $ut, s.source = 'refined'",
                {"uuid": skill_uuid, "ut": curtime},
            )

    # ------------------------------------------------------------------
    # 公开：删除
    # ------------------------------------------------------------------
    async def delete_skill(self, group_id: str, name: str) -> None:
        async with self._memory_access():
            skill_uuid = await self._find_skill_uuid(group_id, name)
            if skill_uuid is None:
                raise ValueError(f"技能 '{name}' 不存在")
            # 删除关系（best-effort）
            try:
                await self._conn().execute(
                    "MATCH (s:ActionSkill {uuid: $uuid})-[r:HAS_TEMPLATE]->() DELETE r",
                    {"uuid": skill_uuid},
                )
            except Exception:
                pass
            # 删除所有模板
            await self._conn().execute(
                "MATCH (t:ActionSequenceTemplate) WHERE t.skill_uuid = $uuid DELETE t",
                {"uuid": skill_uuid},
            )
            # 删除技能
            await self._conn().execute(
                "MATCH (s:ActionSkill {uuid: $uuid}) DELETE s",
                {"uuid": skill_uuid},
            )

    async def delete_template(
        self,
        group_id: str,
        skill_name: str,
        template_name: str,
    ) -> dict:
        """删除特定模板。返回 {'deleted': True, 'is_last': bool}。"""
        async with self._memory_access():
            skill_uuid = await self._find_skill_uuid(group_id, skill_name)
            if skill_uuid is None:
                raise ValueError(f"技能 '{skill_name}' 不存在")
            tmpl_uuid = await self._find_template_uuid(skill_uuid, template_name)
            if tmpl_uuid is None:
                raise ValueError(
                    f"模板 '{template_name}' 在技能 '{skill_name}' 下不存在"
                )
            # 删除关系（best-effort）
            try:
                await self._conn().execute(
                    "MATCH ()-[r:HAS_TEMPLATE]->(t:ActionSequenceTemplate {uuid: $uuid}) DELETE r",
                    {"uuid": tmpl_uuid},
                )
            except Exception:
                pass
            # 删除模板
            await self._conn().execute(
                "MATCH (t:ActionSequenceTemplate {uuid: $uuid}) DELETE t",
                {"uuid": tmpl_uuid},
            )
            # 检查是否还有模板
            cypher = (
                "MATCH (t:ActionSequenceTemplate) "
                "WHERE t.skill_uuid = $uuid "
                "RETURN count(t) AS cnt"
            )
            result = await self._conn().execute(cypher, {"uuid": skill_uuid})
            cnt = 0
            for row in result.rows_as_dict():
                cnt = int(row.get("cnt") or 0)
                break
            return {"deleted": True, "is_last": cnt == 0}

    # ------------------------------------------------------------------
    # 公开：查询
    # ------------------------------------------------------------------
    @staticmethod
    def _row_to_skill(row: dict) -> ActionSkill:
        return ActionSkill(
            uuid=row.get("uuid", ""),
            name=row.get("name", ""),
            group_id=row.get("group_id", ""),
            description=row.get("description", "") or "",
            content=row.get("content", "") or "",
            version=int(row.get("version") or 1),
            source=row.get("source", "") or "learned",
            created_at=row.get("created_at", "") or "",
            updated_at=row.get("updated_at", "") or "",
        )

    @staticmethod
    def _row_to_template(row: dict) -> ActionSequenceTemplate:
        seq_str = row.get("action_sequence_template", "") or ""
        try:
            seq = json.loads(seq_str) if seq_str else []
        except Exception:
            seq = []
        step_explanations_str = row.get("step_explanations", "") or ""
        try:
            step_explanations = normalize_step_explanations(
                step_explanations_str,
                len(seq),
                require_complete=False,
            )
        except Exception:
            step_explanations = []
        emb = row.get("description_embedding") or []
        return ActionSequenceTemplate(
            uuid=row.get("uuid", ""),
            skill_uuid=row.get("skill_uuid", "") or "",
            name=row.get("name", "") or "",
            group_id=row.get("group_id", "") or "",
            description=row.get("description", "") or "",
            description_embedding=list(emb) if emb else [],
            action_sequence_template=seq,
            step_explanations=step_explanations,
            usage_notes=row.get("usage_notes", "") or "",
            created_at=row.get("created_at", "") or "",
            updated_at=row.get("updated_at", "") or "",
        )

    async def _load_templates_of_skill(self, skill_uuid: str) -> List[ActionSequenceTemplate]:
        cypher = (
            "MATCH (t:ActionSequenceTemplate) "
            "WHERE t.skill_uuid = $uuid "
            "RETURN t.uuid AS uuid, t.skill_uuid AS skill_uuid, "
            "t.name AS name, t.group_id AS group_id, "
            "t.`description` AS description, t.`description_embedding` AS description_embedding, "
                "t.action_sequence_template AS action_sequence_template, "
                "t.step_explanations AS step_explanations, "
                "t.usage_notes AS usage_notes, "
            "t.created_at AS created_at, t.updated_at AS updated_at"
        )
        result = await self._conn().execute(cypher, {"uuid": skill_uuid})
        return [self._row_to_template(row) for row in result.rows_as_dict()]

    async def get_skill(self, group_id: str, name: str) -> Optional[ActionSkill]:
        async with self._memory_access():
            cypher = (
                "MATCH (s:ActionSkill) "
                "WHERE s.group_id = $gid AND s.name = $name "
                "RETURN s.uuid AS uuid, s.name AS name, s.group_id AS group_id, "
                "s.`description` AS description, s.content AS content, "
                "s.version AS version, s.source AS source, "
                "s.created_at AS created_at, s.updated_at AS updated_at "
                "LIMIT 1"
            )
            result = await self._conn().execute(cypher, {"gid": group_id, "name": name})
            skill = None
            for row in result.rows_as_dict():
                skill = self._row_to_skill(row)
                break
            if skill is None:
                return None
            skill.templates = await self._load_templates_of_skill(skill.uuid)
            return skill

    async def get_all_skills(self, group_id: str) -> List[ActionSkill]:
        async with self._memory_access():
            cypher = (
                "MATCH (s:ActionSkill) "
                "WHERE s.group_id = $gid "
                "RETURN s.uuid AS uuid, s.name AS name, s.group_id AS group_id, "
                "s.`description` AS description, s.content AS content, "
                "s.version AS version, s.source AS source, "
                "s.created_at AS created_at, s.updated_at AS updated_at"
            )
            result = await self._conn().execute(cypher, {"gid": group_id})
            skills = [self._row_to_skill(row) for row in result.rows_as_dict()]
            for sk in skills:
                sk.templates = await self._load_templates_of_skill(sk.uuid)
            return skills

    async def get_skill_index(
        self,
        group_id: str,
        query: str = "",
        top_n: int = 5,
    ) -> str:
        """返回注入 system prompt 的动作序列模板索引文本。

        策略：
        - 总模板数 ≤ top_n：跳过 RAG 全量返回
        - 总模板数 > top_n：按 query 做 embedding，按模板 description 分数排序取 top_n
        - 不设最低阈值
        """
        skills = await self.get_all_skills(group_id)
        if not skills:
            return ""

        pairs = [(sk, tmpl) for sk in skills for tmpl in sk.templates]
        if not pairs:
            return ""

        if len(pairs) <= top_n or not query:
            ranked = pairs
        else:
            query_emb = await self._embed(query)
            if not query_emb:
                ranked = pairs[:top_n]
            else:
                scored = []
                for sk, tmpl in pairs:
                    score = 0.0
                    if tmpl.description_embedding:
                        score = _cosine_similarity(query_emb, tmpl.description_embedding)
                    scored.append(((sk, tmpl), score))
                scored.sort(key=lambda x: x[1], reverse=True)
                ranked = [pair for pair, _ in scored[:top_n]]

        return self._format_template_index(ranked)

    @staticmethod
    def _format_template_index(
        pairs: List[Tuple[ActionSkill, ActionSequenceTemplate]]
    ) -> str:
        if not pairs:
            return ""
        lines = []
        for i, (sk, tmpl) in enumerate(pairs, 1):
            lines.append(f"{i}. 模板：{tmpl.name}")
            lines.append(f"   适用：{tmpl.description}")
            if tmpl.action_sequence_template:
                lines.append("   动作序列：")
                explanations_by_index = {
                    item.step_index: item for item in tmpl.step_explanations
                }
                for step_index, step in enumerate(tmpl.action_sequence_template):
                    lines.append(f"     - {json.dumps(step, ensure_ascii=False)}")
                    explanation = explanations_by_index.get(step_index)
                    if explanation:
                        lines.append("       这一步为什么这样做：")
                        if explanation.action_reason:
                            lines.append(f"         行动理由：{explanation.action_reason}")
                        if explanation.parameter_reason:
                            lines.append(f"         参数依据：{explanation.parameter_reason}")
                        if explanation.condition_reason:
                            lines.append(f"         结束条件依据：{explanation.condition_reason}")
                        if explanation.adjustment_hint:
                            lines.append(f"         变通提示：{explanation.adjustment_hint}")
            if tmpl.usage_notes:
                lines.append(f"   使用注意：{tmpl.usage_notes}")
            lines.append(f"   所属技能：[{sk.name}] {sk.description}")
            lines.append("")
        return "\n".join(lines).rstrip()

    @staticmethod
    def _format_index(skills: List[ActionSkill]) -> str:
        if not skills:
            return ""
        lines = []
        for i, sk in enumerate(skills, 1):
            lines.append(f"{i}. [{sk.name}] {sk.description}")
            for t in sk.templates:
                lines.append(f"   - {t.name}：{t.description}")
        return "\n".join(lines)

    async def get_skill_list(self, group_id: str) -> str:
        """完整技能列表（list_action_skills 工具用）。"""
        skills = await self.get_all_skills(group_id)
        if not skills:
            return "（你目前还没有掌握任何技能）"
        return self._format_index(skills)

    async def export_skills_yaml(self, group_id: str) -> str:
        """导出该 group_id 下所有技能为 YAML 字符串。保留原 source 值。"""
        try:
            import yaml
        except ImportError:
            raise RuntimeError("需要 PyYAML：uv add pyyaml")
        skills = await self.get_all_skills(group_id)
        data = {"skills": [sk.to_export_dict() for sk in skills]}
        return yaml.safe_dump(data, allow_unicode=True, sort_keys=False)
