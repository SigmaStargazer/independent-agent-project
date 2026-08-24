using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Services
{
    /// <summary>
    /// 与业务无关的 JSON 配置文件读写基础设施（v0.23.0）。
    /// 统一读写游戏根 <code>Data/Config/</code> 下的 JSON 文件，供
    /// ApiConfigStore（敏感凭证）、未来的 GameSettingsStore（普通偏好）等复用。
    ///
    /// 路径规则与 AgentService.GetPort() 一致：
    ///   Application.dataPath(=Assets) → 上级(工程根) → 上级(Src) → Data/Config
    /// 对应 PythonServer 侧的 Src/Data/Config（agent_server_port.txt 同目录体系）。
    /// </summary>
    public static class JsonConfigIO
    {
        /// <summary>配置文件目录。
        /// 编辑器：.../Src/Data/Config（Application.dataPath 上两级）；
        /// 打包：&lt;游戏根&gt;/Data/Config（Application.dataPath 上一级）。
        /// 与 AgentService.PortConfigDir() 同一规则，保证端口文件与 api_config.json 同目录（v0.23.2）。</summary>
        public static string ConfigDir()
        {
            DirectoryInfo assetsDir = new DirectoryInfo(Application.dataPath);
            DirectoryInfo projectRoot = assetsDir.Parent;
#if UNITY_EDITOR
            DirectoryInfo configRoot = projectRoot != null ? projectRoot.Parent : null;   // Src/
#else
            DirectoryInfo configRoot = projectRoot;                                       // 游戏根
#endif
            if (configRoot == null)
            {
                Debug.LogWarning("[JsonConfigIO] 无法定位配置目录（构建环境可能不支持外部路径）。");
                return Path.Combine(Application.persistentDataPath, "Data", "Config");
            }
            return Path.Combine(configRoot.FullName, "Data", "Config");
        }

        /// <summary>读取 JSON 文件；文件不存在 / 解析失败时返回 fallback。始终按 UTF-8 处理。</summary>
        public static T LoadJson<T>(string fileName, T fallback)
        {
            try
            {
                string path = Path.Combine(ConfigDir(), fileName);
                if (!File.Exists(path))
                {
                    return fallback;
                }
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return fallback;
                }
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[JsonConfigIO] 读取 {fileName} 失败，使用默认值：{e.Message}");
                return fallback;
            }
        }

        /// <summary>将对象序列化（缩进 JSON、UTF-8）写入配置文件；目录不存在时自动创建。</summary>
        public static void SaveJson<T>(string fileName, T data)
        {
            try
            {
                string dir = ConfigDir();
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string path = Path.Combine(dir, fileName);
                string json = JsonUtility.ToJson(data, prettyPrint: true);
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonConfigIO] 写入 {fileName} 失败：{e.Message}");
            }
        }
    }
}
