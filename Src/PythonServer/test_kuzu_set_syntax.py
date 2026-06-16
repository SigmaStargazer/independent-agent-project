# -*- coding: utf-8 -*-
"""验证 Kuzu MATCH/WHERE/SET 语法到底什么写法能通过。"""
import asyncio
import os
import shutil
import time

import kuzu

DB = "db_kuzu_set_test"


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

    print("=== schema ===")
    await conn.execute("""
    CREATE NODE TABLE T (
        uuid STRING,
        description STRING,
        emb DOUBLE[],
        PRIMARY KEY (uuid)
    )
    """)

    print("=== insert ===")
    await conn.execute(
        "CREATE (t:T {uuid: $u, description: $d, emb: $e})",
        {"u": "u1", "d": "old", "e": [0.1, 0.2]},
    )

    queries = [
        ("simple SET single prop", "MATCH (t:T) WHERE t.uuid = $u SET t.description = $d"),
        ("simple SET emb prop", "MATCH (t:T) WHERE t.uuid = $u SET t.emb = $e"),
        ("multi SET", "MATCH (t:T) WHERE t.uuid = $u SET t.description = $d, t.emb = $e"),
        ("inline match SET", "MATCH (t:T {uuid: $u}) SET t.description = $d"),
    ]
    for label, q in queries:
        try:
            await conn.execute(q, {"u": "u1", "d": "new", "e": [0.3, 0.4]})
            print(f"✓ {label}: OK")
        except Exception as e:
            print(f"✗ {label}: {type(e).__name__}: {e}")

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
