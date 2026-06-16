# -*- coding: utf-8 -*-
"""验证 backtick 转义在 Kuzu Cypher 中是否能让 description 这种特殊字段正常工作。"""
import asyncio
import os
import shutil
import time

import kuzu

DB = "db_kuzu_backtick_test"


async def main():
    if os.path.exists(DB):
        for _ in range(5):
            try:
                shutil.rmtree(DB); break
            except Exception:
                time.sleep(0.5)

    db = kuzu.Database(DB)
    conn = kuzu.AsyncConnection(db)

    # 1) 试试 schema 用 backtick：能不能保留原字段名 description
    print("=== schema with backtick description ===")
    try:
        await conn.execute("""
        CREATE NODE TABLE T (
            uuid STRING,
            `description` STRING,
            `description_embedding` DOUBLE[],
            updated_at STRING,
            PRIMARY KEY (uuid)
        )
        """)
        print("✓ schema OK")
    except Exception as e:
        print(f"✗ schema: {e}")
        return

    # 2) CREATE 节点用 backtick
    print("\n=== CREATE with backtick ===")
    try:
        await conn.execute(
            "CREATE (t:T {uuid: $u, `description`: $d, `description_embedding`: $e, updated_at: $ut})",
            {"u": "u1", "d": "old desc", "e": [0.1] * 4, "ut": "2026-06-13"},
        )
        print("✓ CREATE OK")
    except Exception as e:
        print(f"✗ CREATE: {e}")
        return

    # 3) RETURN 用 backtick
    print("\n=== RETURN with backtick ===")
    try:
        result = await conn.execute(
            "MATCH (t:T) WHERE t.uuid = $u RETURN t.`description` AS description, t.`description_embedding` AS emb",
            {"u": "u1"},
        )
        for row in result.rows_as_dict():
            print(f"  rows: {row}")
        print("✓ RETURN OK")
    except Exception as e:
        print(f"✗ RETURN: {e}")

    # 4) SET 多属性用 backtick
    print("\n=== SET multi-prop with backtick ===")
    try:
        await conn.execute(
            "MATCH (t:T) WHERE t.uuid = $u "
            "SET t.`description` = $d, t.`description_embedding` = $e, t.updated_at = $ut",
            {"u": "u1", "d": "new desc", "e": [0.2] * 4, "ut": "2026-06-14"},
        )
        print("✓ SET multi-prop OK")
    except Exception as e:
        print(f"✗ SET: {e}")

    del conn; del db
    import gc; gc.collect()
    time.sleep(0.5)
    try: shutil.rmtree(DB)
    except Exception: pass


if __name__ == "__main__":
    asyncio.run(main())
