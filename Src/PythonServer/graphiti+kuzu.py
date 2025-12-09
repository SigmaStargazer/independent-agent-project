import asyncio
import json
import logging
import os
from datetime import datetime, timezone
from logging import INFO

from dotenv import load_dotenv

import kuzu

from graphiti_core import Graphiti
from graphiti_core.driver.kuzu_driver import KuzuDriver # kuzu配置
from graphiti_core.nodes import EpisodeType
from graphiti_core.search.search_config import SearchConfig
from graphiti_core.search import search_config_recipes
# from graphiti_core.search.search_config_recipes import NODE_HYBRID_SEARCH_RRF

from graphiti_core.llm_client.openai_generic_client import OpenAIGenericClient
from graphiti_core.llm_client.config import LLMConfig
from graphiti_core.embedder.openai import OpenAIEmbedder, OpenAIEmbedderConfig
from graphiti_core.cross_encoder.openai_reranker_client import OpenAIRerankerClient

NAMESPACE = "customer_team"

# # 千问
small_model_api_base = "https://dashscope.aliyuncs.com/compatible-mode/v1"
small_model_api_key = "sk-7a3958e0fdf840e49a2edd83b25dd228"
small_model_name = "qwen-max"

# 千问
model_api_base = "https://dashscope.aliyuncs.com/compatible-mode/v1"
model_api_key = "sk-7a3958e0fdf840e49a2edd83b25dd228"
model_name = "qwen-max"

# # moonshot
# model_api_base = "https://api.moonshot.cn/v1"
# model_api_key = "sk-0cYUM2FsdWqmyJeth1He0FXlCVlcxScjNb3YPYHjl78vyEgY"
# model_name = "kimi-k2-0711-preview"

# Configure OpenAI-compatible service
llm_config = LLMConfig(
    api_key=model_api_key,
    model=model_name,        # e.g., "mistral-large-latest"
    small_model=model_name, # e.g., "mistral-small-latest"
    base_url=model_api_base,       # e.g., "https://api.mistral.ai/v1"
)



# kuzu配置
kuzu_driver = KuzuDriver(
    db='db/graphiti.kuzu' # 设置数据库路径。无需提前生成数据库文件
)
graphiti = Graphiti(
    graph_driver=kuzu_driver,
    llm_client=OpenAIGenericClient(config=llm_config),
    embedder=OpenAIEmbedder(
        config=OpenAIEmbedderConfig(
            api_key=small_model_api_key,
            embedding_model="text-embedding-v4", # e.g., "mistral-embed"
            base_url=small_model_api_base,
        )
    ),
    cross_encoder=OpenAIRerankerClient(
        config=LLMConfig(
            api_key=small_model_api_key,
            model="gte-rerank-v2",  # Use smaller model for reranking
            base_url=small_model_api_base
        )
    )
)
# def init_kuzu():
#     """
#     (没有用)
#     """
#     db = kuzu.Database("db/graphiti.kuzu")
#     conn = kuzu.Connection(db)

#     # 👇 关键：安装并加载 FTS 扩展
#     try:
#         conn.execute("INSTALL FTS;")
#     except Exception as e:
#         print("Kuzu FTS may already be installed:", e)

#     try:
#         conn.execute("LOAD EXTENSION FTS;")
#     except Exception as e:
#         print("Failed to load Kuzu FTS extension:", e)

async def init_graphiti():
    """
    连接到 Neo4j 并设置 Graphiti 索引。使用其他 Graphiti 功能之前，必须完成此操作：
    """
    try:
        await graphiti.build_indices_and_constraints()
        print("✅ 索引和约束已构建完成")
    except Exception as e:
        print(f"❌ 初始化失败: {e}")
        raise

async def add_episodes(): 
    """
    剧集是 Graphiti 中的主要信息单位。
    它们可以是文本或结构化 JSON，并会自动处理以提取实体和关系。
    有关剧集和批量加载的更多详细信息，请参阅“添加剧集”页面：
    """
    await graphiti.add_episode(
        name="小明episode_1",
        episode_body="""[2016-01-01 00:00:00]
用户 to 小明: 和小红说，让她闹个每天8点的起床铃，9点的上班铃声，然后每天闹铃响时让她直接通知我。
[2016-01-01 05:37:03]
(小明向[小红]发送信息: 请为用户设置每天8点的起床铃和9点的上班铃。当闹钟响的时候，请通知用户。)
小明 to 用户: 已经通知小红设置了每天8点的起床铃和9点的上班铃。当闹钟响的时候，我会直接通知她。""",
        source=EpisodeType.text,
        source_description="小明episode_1", 
        reference_time=datetime.now(),
        group_id = "Agent_1"
    )

    await graphiti.add_episode(
        name="小红episode_1",
        episode_body="""[2016-01-01 05:37:03]
小明 to 小红: 请为用户设置每天8点的起床铃和9点的上班铃。当闹钟响的时候，请通知用户。
[2016-01-01 08:00:19]
(小红使用了[定闹钟]工具: 
- 起床铃：每天 08:00
- 上班铃：每天 09:00)
小红 to 小明:小明，我已经帮您设置了每天早上8点的起床铃和9点的上班铃。当闹钟响的时候，我会直接通知您的。""",
        source=EpisodeType.text,
        source_description="小红episode_1", 
        reference_time=datetime.now(),
        group_id = "Agent_2"
    )


async def add_episodes_1(): 
    """
    某些事实因发展而产生变化。测试此时rag出来的结果是怎样的。
    """
    await graphiti.add_episode(
        name="小红episode_1",
        episode_body="""[2016-01-01 05:37:03]
小明 to 小红: 请为用户设置每天8点的起床铃和9点的上班铃。当闹钟响的时候，请通知用户。
[2016-01-01 08:00:19]
(小红设置了闹钟:
- 起床铃：每天 08:00
- 上班铃：每天 09:00)
小红 to 小明:小明，我已经帮您设置了每天早上8点的起床铃和9点的上班铃。当闹钟响的时候，我会直接通知您的。""",
        source=EpisodeType.text,
        source_description="小红episode_1", 
        reference_time=datetime.now(),
        group_id = "Agent_2"
    )

    await graphiti.add_episode(
        name="小红episode_2",
        episode_body="""[2016-02-02 00:07:03]
小明 to 小红: 把起床铃删了吧
[2016-02-02 02:00:19]
(小红删除了闹钟: 
- 起床铃：每天 08:00
小红 to 小明:小明，我已经把起床铃删除了""",
        source=EpisodeType.text,
        source_description="小红episode_2", 
        reference_time=datetime.now(),
        group_id = "Agent_2"
    )

async def add_episodes_2(): 
    """
    测试一句一句存记忆的结果
    """
    episode_list = [
        """[2016-01-01 05:37:03]
小明 to 小红: 请为用户设置每天8点的起床铃和9点的上班铃。当闹钟响的时候，请通知用户。""",
        """[2016-01-01 08:00:19]
(小红设置了闹钟:
- 起床铃：每天 08:00
- 上班铃：每天 09:00)""",
        """[2016-01-01 08:00:19]
小红 to 小明:小明，我已经帮您设置了每天早上8点的起床铃和9点的上班铃。当闹钟响的时候，我会直接通知您的。""",
        """[2016-02-02 00:07:03]
小明 to 小红: 把起床铃删了吧""",
        """[2016-02-02 02:00:19]
(小红删除了闹钟: 
- 起床铃：每天 08:00)""",
        """[2016-02-02 02:00:19]
小红 to 小明:小明，我已经把起床铃删除了""",
    ]

    for index, episode in enumerate(episode_list):
        max_retries = 3
        for attempt in range(max_retries):
            try:
                result = await graphiti.add_episode(
                    name=f"小红episode_{index}",
                    episode_body=episode,
                    source=EpisodeType.text,
                    source_description=f"小红episode_{index}", 
                    reference_time=datetime.now(),
                    group_id="Agent_3"
                )
                print(f"✅ 成功存储第 {index} 条记忆")
                break  # 成功则跳出重试循环
            except Exception as e:
                # 判断是否是 graphiti 的 schema 返回 bug
                if "Field required" in str(e) and ("$defs" in str(e) or "properties" in str(e)):
                    print(f"⚠️ 检测到 LLM 返回了 schema（第 {attempt + 1} 次尝试）: {e}")
                else:
                    print(f"❌ 存储第 {index} 条记忆失败（非 schema 错误）: {e}")
                    # break  # 非 schema 错误，直接放弃（可能是其他问题）

                # 重试前等待一下，避免速率限制
                if attempt < max_retries - 1:
                    wait_time = (attempt + 1) * 2  # 指数退避
                    print(f"🔁 {wait_time} 秒后重试第 {index} 条记忆...")
                    await asyncio.sleep(wait_time)
                else:
                    print(f"❌ 达到最大重试次数，跳过第 {index} 条记忆")

        # result = await graphiti.add_episode(
        #     name=f"小红episode_{index}",
        #     episode_body=episode,
        #     source=EpisodeType.text,
        #     source_description=f"小红episode_{index}", 
        #     reference_time=datetime.now(),
        #     group_id = "Agent_3"
        # )

async def add_episodes_3(): 
    """
    验证批量存储
    """
    await graphiti.add_episode(
        name="小红episode_1",
#         episode_body="""[2016-01-01 05:37:03]
# 小明 to 小红: 请为用户设置每天8点的起床铃和9点的上班铃。当闹钟响的时候，请通知用户。
# [2016-01-01 08:00:19]
# (小红设置了闹钟:
# - 起床铃：每天 08:00
# - 上班铃：每天 09:00)
# 小红 to 小明:小明，我已经帮您设置了每天早上8点的起床铃和9点的上班铃。当闹钟响的时候，我会直接通知您的。""",
        episode_body=(
            "小明 to 小红: 请为用户设置每天8点的起床铃和9点的上班铃。当闹钟响的时候，请通知用户。"
            """
            小红设置了闹钟:
# - 起床铃：每天 08:00
# - 上班铃：每天 09:00
            """
            "小红 to 小明:小明，我已经帮您设置了每天早上8点的起床铃和9点的上班铃。当闹钟响的时候，我会直接通知您的。"
        ),
        source=EpisodeType.text,
        source_description="小红episode_1", 
        reference_time=datetime.now(),
        group_id = "Agent_2"
    )

    await graphiti.add_episode(
        name="小红episode_2",
        episode_body="""[2016-02-02 00:07:03]
小明 to 小红: 把起床铃删了吧
[2016-02-02 02:00:19]
(小红删除了闹钟: 
- 起床铃：每天 08:00
小红 to 小明:小明，我已经把起床铃删除了""",
        source=EpisodeType.text,
        source_description="小红episode_2", 
        reference_time=datetime.now(),
        group_id = "Agent_2"
    )

async def basic_search(query,group_id):
    """
    从 Graphiti 检索关系（边）的最简单方法是使用搜索方法，
    该方法执行结合语义相似度和 BM25 文本检索的混合搜索。
    有关搜索功能的更多详细信息，请参阅“搜索图”页面：
    """
    # Perform a hybrid search combining semantic similarity and BM25 retrieval
    print(f"\nSearching for: {query}")
    results = await graphiti.search(query, group_ids=[group_id])
    # Print search results
    print('\nSearch Results:')
    for result in results:
        print(f'UUID: {result.uuid}')
        print(f'Fact: {result.fact}')
        if hasattr(result, 'valid_at') and result.valid_at:
            print(f'Valid from: {result.valid_at}')
        if hasattr(result, 'invalid_at') and result.invalid_at:
            print(f'Valid until: {result.invalid_at}')
        print('---')

async def center_node_search(query, group_id):
    """
    为了获得更具上下文相关性的结果，您可以使用中心节点，根据搜索结果与特定节点的图距离对其进行重新排序。
    这对于特定于实体的查询尤其有用，如“搜索图”页面中所述：
    """
    # Perform a hybrid search combining semantic similarity and BM25 retrieval
    print(f"\nSearching for: {query}")
    results = await graphiti.search(query, group_ids=[group_id])
    # Use the top search result's UUID as the center node for reranking
    if results and len(results) > 0:
        # Get the source node UUID from the top result
        center_node_uuid = results[0].source_node_uuid

        print('\nReranking search results based on graph distance:')
        print(f'Using center node UUID: {center_node_uuid}')

        reranked_results = await graphiti.search(
            query, center_node_uuid=center_node_uuid, group_ids=[group_id]
        )

        # Print reranked search results
        print('\nReranked Search Results:')
        for result in reranked_results:
            print(f'UUID: {result.uuid}')
            print(f'Fact: {result.fact}')
            if hasattr(result, 'valid_at') and result.valid_at:
                print(f'Valid from: {result.valid_at}')
            if hasattr(result, 'invalid_at') and result.invalid_at:
                print(f'Valid until: {result.invalid_at}')
            print('---')
    else:
        print('No results found in the initial search to use as center node.')

async def node_search_using_search_recipes(query, group_id, search_config: SearchConfig):
    """
    Graphiti 提供了针对不同搜索场景优化的预定义搜索方案。
    这里我们使用 NODE_HYBRID_SEARCH_RRF 直接检索节点而非边。
    有关可用搜索方案和重排序方法的完整列表，请参阅搜索文档中的“可配置搜索策略”部分：
    """
    # Example: Perform a node search using _search method with standard recipes
    print(
        '\nPerforming node search using _search method with standard recipe NODE_HYBRID_SEARCH_RRF:'
    )

    # Use a predefined search configuration recipe and modify its limit
    node_search_config = search_config.model_copy(deep=True)
    node_search_config.limit = 5  # Limit to 5 results

    # Execute the node search
    node_search_results = await graphiti._search(
        query=query,
        config=node_search_config,
        group_ids=[group_id]
    )

    print(node_search_results)

    # Print node search results
    if hasattr(node_search_results, 'nodes') and node_search_results.nodes:
        print('\nNode Search Results:')
        for node in node_search_results.nodes:
            print(f'Node UUID: {node.uuid}')
            print(f'Node Name: {node.name}')
            node_summary = node.summary[:100] + '...' if len(node.summary) > 100 else node.summary
            print(f'Content Summary: {node_summary}')
            print(f"Node Labels: {', '.join(node.labels)}")
            print(f'Created At: {node.created_at}')
            if hasattr(node, 'attributes') and node.attributes:
                print('Attributes:')
                for key, value in node.attributes.items():
                    print(f'  {key}: {value}')
            print('---')

    if hasattr(node_search_results, 'edges') and node_search_results.edges:
        print('\nEdge Search Results:')
        for edge in node_search_results.edges:
            print(f'Edge UUID: {edge.uuid}')
            print(f'Edge Fact: {edge.fact}')
            if hasattr(edge, 'valid_at') and edge.valid_at:
                    print(f'Valid from: {edge.valid_at}')
            if hasattr(edge, 'invalid_at') and edge.invalid_at:
                print(f'Valid until: {edge.invalid_at}')
            print('---')

    

async def main():
    try:
        # 先初始化（仅需一次，可注释掉后续运行）
        # init_kuzu()
        await init_graphiti()

        # 再添加 episode
        # await add_episodes_3()
        # await basic_search('小红', group_id="langgraph_test_1")
        # await center_node_search('现在有什么闹钟？', group_id="Agent_3")
        
        await node_search_using_search_recipes('agent', 
        group_id="langgraph_test_1", 
        search_config=search_config_recipes.COMBINED_HYBRID_SEARCH_RRF)

    finally:
        await graphiti.close()  # ✅ 统一在这里关闭
        print('\nConnection closed')


if __name__ == '__main__':
    asyncio.run(main())
    # print(kuzu.__version__)
