using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Services
{
    /// <summary>
    /// 游戏画面偏好数据模型（v0.23.4）。
    /// 显示模式用下标索引（0 窗口化 / 1 无边框 / 2 全屏），分辨率用预置列表下标。
    /// v0.23.5：新增 language 字段（0 简体中文 / 1 English），复用同一配置文件。
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        public int displayModeIndex;   // 0 窗口化 / 1 无边框 / 2 全屏
        public int resolutionIndex;    // 预置分辨率列表下标
        public int language;           // 0 简体中文（默认）/ 1 English
    }

    /// <summary>
    /// 游戏画面偏好存取（v0.23.4）。
    /// 读写 <code>Data/Config/game_settings.json</code>（UTF-8），复用 <see cref="JsonConfigIO"/>。
    /// 与 ApiConfigStore 同为「普通偏好」（非敏感），故不加密。
    /// </summary>
    public static class GameSettingsStore
    {
        private const string FileName = "game_settings.json";

        /// <summary>默认显示模式（全屏）/ 默认分辨率下标（1920x1080）/ 默认语言（简体中文）。仅在设置文件没有任何值时启用。</summary>
        private const int kDefaultMode = 2;   // FullScreenMode.ExclusiveFullScreen
        private const int kDefaultRes = 2;    // (1920, 1080)
        private const int kDefaultLanguage = 0;   // UITextLanguage.ChineseSimplified

        /// <summary>
        /// 读取画面设置。
        /// 返回 (hasValue, mode, res, language)：hasValue=false 表示文件不存在 / 为空 / 解析失败（即「没有任何值」），
        /// 此时应由调用方使用默认值（kDefaultMode/kDefaultRes/kDefaultLanguage，全屏 + 1920x1080 + 简体中文）。
        /// </summary>
        public static (bool hasValue, int mode, int res, int language) Load()
        {
            // 文件不存在 → 没有任何值，直接返回默认
            string path = Path.Combine(JsonConfigIO.ConfigDir(), FileName);
            if (!File.Exists(path))
            {
                return (false, kDefaultMode, kDefaultRes, kDefaultLanguage);
            }

            // 文件存在：内容为空 / 解析失败时 LoadJson 返回 fallback（new GameSettings()，全 0），
            // 无法与「文件值就是 0/0」区分。这里再校验文件内容非空白，空白视为无值。
            string content = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
            {
                return (false, kDefaultMode, kDefaultRes, kDefaultLanguage);
            }

            var settings = JsonConfigIO.LoadJson(FileName, new GameSettings());
            return (true, settings.displayModeIndex, settings.resolutionIndex, settings.language);
        }

        /// <summary>写入画面设置（明文，UTF-8，缩进 JSON）。</summary>
        public static void Save(int mode, int res, int language)
        {
            JsonConfigIO.SaveJson(FileName, new GameSettings { displayModeIndex = mode, resolutionIndex = res, language = language });
            Debug.Log($"[GameSettingsStore] 已保存画面设置到 {JsonConfigIO.ConfigDir()}/{FileName}（模式={mode} 分辨率下标={res} 语言={language}）");
        }
    }
}
