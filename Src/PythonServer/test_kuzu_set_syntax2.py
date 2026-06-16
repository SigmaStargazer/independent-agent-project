# -*- coding: utf-8 -*-
"""更精细诊断 SET 多属性失败的原因。"""
import asyncio
import os
import shutil
import time

import kuzu

DB = "db_kuzu_set_test2"


async def main():
    # 清理
    if os.path.exists(DB):
        for _ in range(5):
            try:
                shutil.rmtree(DB)
                break
            except Exception:
                time.sleep(0.5)

    db = kuzu.Database(DB)
    conn = kuzu.AsyncConnection(db)

    print("=== schema (mimic ActionSequenceTemplate) ===")
    await conn.execute("""
    CREATE NODE TABLE T (
        uuid STRING,
        skill_uuid STRING,
        name STRING,
        group_id STRING,
        description STRING,
        description_embedding DOUBLE[],
        action_sequence_template STRING,
        usage_notes STRING,
        created_at STRING,
        updated_at STRING,
        PRIMARY KEY (uuid)
    )
    """)

    print("=== insert ===")
    await conn.execute(
        "CREATE (t:T {uuid: $u, skill_uuid: $s, name: $n, group_id: $g, "
        "description: $d, description_embedding: $e, "
        "action_sequence_template: $a, usage_notes: $no, "
        "created_at: $c, updated_at: $up})",
        {
            "u": "u1", "s": "s1", "n": "near", "g": "gid",
            "d": "old desc", "e": [0.1] * 1024,
            "a": "[]", "no": "",
            "c": "2026-06-13", "up": "2026-06-13",
        },
    )

    # 测试 1: 改 description + emb + updated_at（和实际项目一样）
    print("\n--- test 1: 多属性 SET (desc + emb + updated_at) ---")
    try:
        await conn.execute(
            "MATCH (t:T) WHERE t.uuid = $uuid "
            "SET t.description = $desc, t.description_embedding = $emb, t.updated_at = $ut",
            {"uuid": "u1", "desc": "new desc", "emb": [0.2] * 1024, "ut": "2026-06-14"},
        )
        print("✓ OK")
    except Exception as e:
        print(f"✗ {type(e).__name__}: {e}")

    # 测试 2: 只改 desc + updated_at（不动 emb）
    print("\n--- test 2: SET (desc + updated_at) ---")
    try:
        await conn.execute(
            "MATCH (t:T) WHERE t.uuid = $uuid "
            "SET t.description = $desc, t.updated_at = $ut",
            {"uuid": "u1", "desc": "new desc 2", "ut": "2026-06-14"},
        )
        print("✓ OK")
    except Exception as e:
        print(f"✗ {type(e).__name__}: {e}")

    # 测试 3: 用 $ 字符以外的命名？
    print("\n--- test 3: SET (emb 单独) ---")
    try:
        await conn.execute(
            "MATCH (t:T) WHERE t.uuid = $uuid "
            "SET t.description_embedding = $emb, t.updated_at = $ut",
            {"uuid": "u1", "emb": [0.3] * 1024, "ut": "2026-06-15"},
        )
        print("✓ OK")
    except Exception as e:
        print(f"✗ {type(e).__name__}: {e}")

    # Cleanup
    del conn
    del db
    import gc; gc.collect()
    time.sleep(0.5)
    try:
        shutil.rmtree(DB)
    except Exception:
        pass


if __name__ == "__main__":
    asyncio.run(main())
