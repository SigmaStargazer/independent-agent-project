from graphiti_core.cross_encoder.openai_reranker_client import OpenAIRerankerClient

class SafeBatchOpenAIReranker(OpenAIRerankerClient):
    """
    限制单次上传至DashScope reranker模型的文档数量，防止报错
    """
    def __init__(self, config=None, max_batch_size: int = 10, **kwargs):
        # 初始化父类
        super().__init__(config=config, **kwargs)
        self._max_batch_size = max_batch_size

    async def rerank(self, query: str, documents: list[str], top_k: int = 5):
        if len(documents) <= self._max_batch_size:
            return await super().rerank(query, documents, top_k=top_k)
        
        all_results = []
        for i in range(0, len(documents), self._max_batch_size):
            chunk = documents[i : i + self._max_batch_size]
            chunk_results = await super().rerank(query, chunk, top_k=len(chunk))
            
            for res in chunk_results:
                try:
                    res.index += i
                except Exception:
                    pass
            all_results.extend(chunk_results)
            
        all_results.sort(key=lambda x: x.relevance_score, reverse=True)
        return all_results[:top_k]