import asyncio

from memory_system.memory_manager import MemoryManager

async def main():
    await MemoryManager().initialize()

    cypher = f"""
MATCH (n: Entity{{group_id: '0'}}) 
RETURN n
""" 

    response = await MemoryManager().conn.execute(cypher)
    print(len(response.rows_as_dict()))
    for row in response.rows_as_dict():
        print(row)

if __name__ == "__main__":
    asyncio.run(main())