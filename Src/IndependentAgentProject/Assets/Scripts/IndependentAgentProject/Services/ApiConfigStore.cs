using System;
using UnityEngine;

namespace Services
{
    /// <summary>
    /// API 配置数据模型（v0.23.0）。
    /// 字段名与 PythonServer config/api_config_loader.API_CONFIG_KEYS 完全一致（大写，下划线分隔），
    /// 保证 JsonUtility 序列化出的 api_config.json 就是 Python 可直接读取的格式。
    /// 字段为 public 才能被 JsonUtility 序列化。
    /// </summary>
    [Serializable]
    public class ApiConfig
    {
        public string AGENT_API_BASE;
        public string AGENT_API_KEY;
        public string AGENT_MODEL;

        public string MEMORY_API_BASE;
        public string MEMORY_API_KEY;
        public string MEMORY_MODEL;

        public string EMBEDDING_API_BASE;
        public string EMBEDDING_API_KEY;
        public string EMBEDDING_MODEL;

        public string RERANKER_API_BASE;
        public string RERANKER_API_KEY;
        public string RERANKER_MODEL;

        /// <summary>12 项字段是否全部非空（用于入口拦截）。</summary>
        public bool IsComplete()
        {
            return !string.IsNullOrWhiteSpace(AGENT_API_BASE)
                && !string.IsNullOrWhiteSpace(AGENT_API_KEY)
                && !string.IsNullOrWhiteSpace(AGENT_MODEL)
                && !string.IsNullOrWhiteSpace(MEMORY_API_BASE)
                && !string.IsNullOrWhiteSpace(MEMORY_API_KEY)
                && !string.IsNullOrWhiteSpace(MEMORY_MODEL)
                && !string.IsNullOrWhiteSpace(EMBEDDING_API_BASE)
                && !string.IsNullOrWhiteSpace(EMBEDDING_API_KEY)
                && !string.IsNullOrWhiteSpace(EMBEDDING_MODEL)
                && !string.IsNullOrWhiteSpace(RERANKER_API_BASE)
                && !string.IsNullOrWhiteSpace(RERANKER_API_KEY)
                && !string.IsNullOrWhiteSpace(RERANKER_MODEL);
        }

        /// <summary>12 项字段是否全部为空（判断是否从未配置过）。</summary>
        public bool IsEmpty()
        {
            return string.IsNullOrWhiteSpace(AGENT_API_BASE)
                && string.IsNullOrWhiteSpace(AGENT_API_KEY)
                && string.IsNullOrWhiteSpace(AGENT_MODEL)
                && string.IsNullOrWhiteSpace(MEMORY_API_BASE)
                && string.IsNullOrWhiteSpace(MEMORY_API_KEY)
                && string.IsNullOrWhiteSpace(MEMORY_MODEL)
                && string.IsNullOrWhiteSpace(EMBEDDING_API_BASE)
                && string.IsNullOrWhiteSpace(EMBEDDING_API_KEY)
                && string.IsNullOrWhiteSpace(EMBEDDING_MODEL)
                && string.IsNullOrWhiteSpace(RERANKER_API_BASE)
                && string.IsNullOrWhiteSpace(RERANKER_API_KEY)
                && string.IsNullOrWhiteSpace(RERANKER_MODEL);
        }
    }

    /// <summary>
    /// API 配置存取（v0.23.0）。
    /// 读写 <code>Data/Config/api_config.json</code>，供 UITitle 配置面板使用。
    /// 存储格式为 12 个大写键（与 Python API_CONFIG_KEYS 一致），JsonUtility 原生序列化。
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

        /// <summary>写入配置（明文，UTF-8，缩进 JSON，大写键）。</summary>
        public static void Save(ApiConfig config)
        {
            JsonConfigIO.SaveJson(FileName, config);
            Debug.Log($"[ApiConfigStore] 已保存 API 配置到 {JsonConfigIO.ConfigDir()}/{FileName}");
        }
    }
}
