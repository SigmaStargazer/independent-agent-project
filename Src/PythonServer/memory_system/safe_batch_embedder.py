from graphiti_core.embedder.openai import OpenAIEmbedder

class SafeBatchOpenAIEmbedder(OpenAIEmbedder):
    """
    限制单次上传至DashScope Embedding模型的文档数量，防止报错
    """
    def __init__(self, config=None, max_batch_size: int = 10, **kwargs):
            # 初始化父类
            super().__init__(config=config, **kwargs)
            self._max_batch_size = max_batch_size

    async def create_batch(self, input_data_list: list[str]) -> list[list[float]]:
        if not input_data_list:
            return []

        results = []
        total = len(input_data_list)

        if total > self._max_batch_size:
            print(f"[Embedder] batch={total} → chunking by {self._max_batch_size}")

        for i in range(0, total, self._max_batch_size):
            chunk = input_data_list[i : i + self._max_batch_size]
            # 调用父类（Graphiti 原生）的 create_batch 发送真实请求
            r = await super().create_batch(chunk)
            results.extend(r)

        return results