import logging
from typing import Any

import numpy as np
import openai
from graphiti_core.cross_encoder.openai_reranker_client import OpenAIRerankerClient
from graphiti_core.helpers import semaphore_gather
from graphiti_core.llm_client import RateLimitError
from graphiti_core.prompts import Message

logger = logging.getLogger(__name__)


class SafeBatchOpenAIReranker(OpenAIRerankerClient):
    """
    限制单次上传至 DashScope reranker 模型的文档数量，防止报错。

    另：Graphiti 用 LLM chat 二分类（logprobs）做 rerank，DeepSeek V4 系
    Reasoning 模型默认开启 thinking 且不支持 logprobs/logit_bias/top_logprobs。
    rank() 统一关闭 thinking（extra_body），使 deepseek-v4-flash 等模型可用于
    Graphiti rerank；对不支持该字段的模型（qwen-turbo 等）会被安全忽略。
    """

    def __init__(self, config=None, max_batch_size: int = 10, **kwargs):
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

    async def rank(self, query: str, passages: list[str]) -> list[tuple[str, float]]:
        """同基类 OpenAIRerankerClient.rank，唯一差异：请求带 extra_body 关闭 thinking。

        Graphiti search.py 通过 cross_encoder.rank() 调用此处（运行时实际入口）。
        实现复制自 graphiti_core.cross_encoder.openai_reranker_client.OpenAIRerankerClient.rank，
        升级 graphiti_core 时需同步。返回类型与基类一致：list[tuple[str, float]]。
        """
        openai_messages_list: Any = [
            [
                Message(
                    role='system',
                    content='You are an expert tasked with determining whether the passage is relevant to the query',
                ),
                Message(
                    role='user',
                    content=f"""
                           Respond with "True" if PASSAGE is relevant to QUERY and "False" otherwise.
                           <PASSAGE>
                           {passage}
                           </PASSAGE>
                           <QUERY>
                           {query}
                           </QUERY>
                           """,
                ),
            ]
            for passage in passages
        ]
        try:
            responses = await semaphore_gather(
                *[
                    self.client.chat.completions.create(
                        model=self.config.model or 'gpt-4.1-nano',
                        messages=openai_messages,
                        temperature=0,
                        max_tokens=1,
                        logit_bias={'6432': 1, '7983': 1},
                        logprobs=True,
                        top_logprobs=2,
                        extra_body={'thinking': {'type': 'disabled'}},
                    )
                    for openai_messages in openai_messages_list
                ]
            )

            responses_top_logprobs = [
                response.choices[0].logprobs.content[0].top_logprobs
                if response.choices[0].logprobs is not None
                and response.choices[0].logprobs.content is not None
                else []
                for response in responses
            ]
            scores: list[float] = []
            for top_logprobs in responses_top_logprobs:
                if len(top_logprobs) == 0:
                    continue
                norm_logprobs = np.exp(top_logprobs[0].logprob)
                if top_logprobs[0].token.strip().split(' ')[0].lower() == 'true':
                    scores.append(norm_logprobs)
                else:
                    scores.append(1 - norm_logprobs)

            results = [(passage, score) for passage, score in zip(passages, scores, strict=True)]
            results.sort(reverse=True, key=lambda x: x[1])
            return results
        except openai.RateLimitError as e:
            raise RateLimitError from e
        except Exception as e:
            logger.error(f'Error in generating LLM response: {e}')
            raise
