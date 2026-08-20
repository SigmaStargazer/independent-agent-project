using System;
using System.Collections.Generic;
using UnityEngine;

namespace Services
{
    /// <summary>
    /// API 配置数据模型（v0.23.0）。
    /// 与 PythonServer config/api_config_loader.API_CONFIG_KEYS 保持字段名一致（12 项）。
    /// 字段为 public 才能被 JsonUtility 序列化。
    /// </summary>
    [Serializable]
    public class ApiConfig
    {
        public string agentApiBase;
        public string agentApiKey;
        public string agentModel;

        public string memoryApiBase;
        public string memoryApiKey;
        public string memoryModel;

        public string embeddingApiBase;
        public string embeddingApiKey;
        public string embeddingModel;

        public string rerankerApiBase;
        public string rerankerApiKey;
        public string rerankerModel;

        /// <summary>键名与 Python API_CONFIG_KEYS 一致（大写，下划线分隔）。</summary>
        public Dictionary<string, string> ToDictionary()
        {
            return new Dictionary<string, string>
            {
                ["AGENT_API_BASE"] = agentApiBase,
                ["AGENT_API_KEY"] = agentApiKey,
                ["AGENT_MODEL"] = agentModel,
                ["MEMORY_API_BASE"] = memoryApiBase,
                ["MEMORY_API_KEY"] = memoryApiKey,
                ["MEMORY_MODEL"] = memoryModel,
                ["EMBEDDING_API_BASE"] = embeddingApiBase,
                ["EMBEDDING_API_KEY"] = embeddingApiKey,
                ["EMBEDDING_MODEL"] = embeddingModel,
                ["RERANKER_API_BASE"] = rerankerApiBase,
                ["RERANKER_API_KEY"] = rerankerApiKey,
                ["RERANKER_MODEL"] = rerankerModel,
            };
        }

        /// <summary>从键值字典填充（未知键忽略）。</summary>
        public void FromDictionary(Dictionary<string, string> dict)
        {
            if (dict == null) return;
            agentApiBase = Get(dict, "AGENT_API_BASE");
            agentApiKey = Get(dict, "AGENT_API_KEY");
            agentModel = Get(dict, "AGENT_MODEL");
            memoryApiBase = Get(dict, "MEMORY_API_BASE");
            memoryApiKey = Get(dict, "MEMORY_API_KEY");
            memoryModel = Get(dict, "MEMORY_MODEL");
            embeddingApiBase = Get(dict, "EMBEDDING_API_BASE");
            embeddingApiKey = Get(dict, "EMBEDDING_API_KEY");
            embeddingModel = Get(dict, "EMBEDDING_MODEL");
            rerankerApiBase = Get(dict, "RERANKER_API_BASE");
            rerankerApiKey = Get(dict, "RERANKER_API_KEY");
            rerankerModel = Get(dict, "RERANKER_MODEL");
        }

        private static string Get(Dictionary<string, string> dict, string key)
        {
            if (dict.TryGetValue(key, out var v)) return v;
            return null;
        }

        /// <summary>12 项字段是否全部非空（用于入口拦截）。</summary>
        public bool IsComplete()
        {
            return !string.IsNullOrWhiteSpace(agentApiBase)
                && !string.IsNullOrWhiteSpace(agentApiKey)
                && !string.IsNullOrWhiteSpace(agentModel)
                && !string.IsNullOrWhiteSpace(memoryApiBase)
                && !string.IsNullOrWhiteSpace(memoryApiKey)
                && !string.IsNullOrWhiteSpace(memoryModel)
                && !string.IsNullOrWhiteSpace(embeddingApiBase)
                && !string.IsNullOrWhiteSpace(embeddingApiKey)
                && !string.IsNullOrWhiteSpace(embeddingModel)
                && !string.IsNullOrWhiteSpace(rerankerApiBase)
                && !string.IsNullOrWhiteSpace(rerankerApiKey)
                && !string.IsNullOrWhiteSpace(rerankerModel);
        }

        /// <summary>12 项字段是否全部为空（判断是否从未配置过）。</summary>
        public bool IsEmpty()
        {
            return string.IsNullOrWhiteSpace(agentApiBase)
                && string.IsNullOrWhiteSpace(agentApiKey)
                && string.IsNullOrWhiteSpace(agentModel)
                && string.IsNullOrWhiteSpace(memoryApiBase)
                && string.IsNullOrWhiteSpace(memoryApiKey)
                && string.IsNullOrWhiteSpace(memoryModel)
                && string.IsNullOrWhiteSpace(embeddingApiBase)
                && string.IsNullOrWhiteSpace(embeddingApiKey)
                && string.IsNullOrWhiteSpace(embeddingModel)
                && string.IsNullOrWhiteSpace(rerankerApiBase)
                && string.IsNullOrWhiteSpace(rerankerApiKey)
                && string.IsNullOrWhiteSpace(rerankerModel);
        }
    }

    /// <summary>
    /// API 配置存取（v0.23.0）。
    /// 读写 <code>Data/Config/api_config.json</code>，供 UITitle 配置面板使用。
    /// </summary>
    public static class ApiConfigStore
    {
        private const string FileName = "api_config.json";

        /// <summary>读取配置；文件不存在 / 解析失败返回空配置。</summary>
        public static ApiConfig Load()
        {
            var empty = new ApiConfig();
            var config = JsonConfigIO.LoadJson(FileName, empty);
            return config ?? empty;
        }

        /// <summary>写入配置（明文，UTF-8，缩进 JSON）。</summary>
        public static void Save(ApiConfig config)
        {
            JsonConfigIO.SaveJson(FileName, config);
            Debug.Log($"[ApiConfigStore] 已保存 API 配置到 {JsonConfigIO.ConfigDir()}/{FileName}");
        }
    }
}
