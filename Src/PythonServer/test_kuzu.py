import asyncio

from memory_system.memory_manager import MemoryManager

async def main():
    await MemoryManager().initialize()

#     cypher = f"""
# MATCH (n: Entity{{group_id: '0'}}) 
# RETURN n
# """ 

# 匹配group_id
#     cypher = f"""
# MATCH (n: Entity{{group_id: 'e5b08fe6988e'}}) 
# RETURN n
# """ 

    # result = await MemoryManager().conn.execute(cypher)
    # if result.has_next():
    #     while result.has_next():
    #         row = result.get_next()
    #         print(row)
    # else:
    #     print("No results found")

# 找所有group_id
    cypher = f"""
MATCH (n: Entity)
WHERE n.group_id IS NOT NULL
RETURN DISTINCT n.group_id
"""

    result = await MemoryManager().conn.execute(cypher)
    if result.has_next():
        print(result.get_next())

if __name__ == "__main__":
    asyncio.run(main())