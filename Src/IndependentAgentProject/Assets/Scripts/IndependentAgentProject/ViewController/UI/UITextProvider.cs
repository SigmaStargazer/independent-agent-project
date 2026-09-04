using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>支持的语言（v0.23.5）。成员名 == 语言文件后缀（strings_ChineseSimplified / strings_English）。</summary>
    public enum UITextLanguage
    {
        ChineseSimplified = 0,   // 简体中文（默认）
        English = 1,             // English
    }

    /// <summary>
    /// 文案 key 枚举（v0.23.4）：Inspector 里用下拉框选择，避免策划手填字符串拼错。
    /// 枚举成员名 == 文案文件 strings_ChineseSimplified.json 中的 key（None 表示不取文案）。
    /// </summary>
    public enum UITextKey
    {
        None,                  // 不赋值（保持场景手动配置的文案）
        tab_model_config,      // 模型配置
        tab_display_settings,  // 画面
        tab_game_settings,     // 游戏（v0.23.5 新增：语言切换入口 Tab）
        mode_windowed,         // 窗口化
        mode_borderless,       // 无边框
        mode_fullscreen,       // 全屏
        resolution_format,     // {0} x {1}
    }

    /// <summary>
    /// 文案查表（v0.23.4 → v0.23.5 多语言化）。
    /// 从数据驱动文件读取 UI 文案，代码中不出现中文字面量。
    /// 文案文件：<code>Assets/Resources/UI/strings_ChineseSimplified.json</code>（简体中文）
    /// 与 <code>strings_English.json</code>（English），UTF-8，扁平 key-value。
    /// 三级回退：当前语言表 → 简体中文兜底表 → key 本身（切英文不漏字、便于排查）。
    /// 语言切换入口 <see cref="SetLanguage"/>：换表 + 广播 <see cref="RegisterLanguageChanged"/> 事件，
    /// 所有已打开的 UI 重新拉文案，做到「即时生效、无需重启」。
    /// </summary>
    public static class UITextProvider
    {
        private const string ResourcePrefix = "UI/strings_";

        private static Dictionary<string, string> sTable;        // 当前语言表
        private static Dictionary<string, string> sZhFallback;   // 简体中文兜底表（始终加载）
        private static UITextLanguage sCurrent = UITextLanguage.ChineseSimplified;
        private static bool sTablesLoaded;
        private static event Action sOnLanguageChanged;

        /// <summary>当前语言。</summary>
        public static UITextLanguage Current => sCurrent;

        /// <summary>语言名文案 key（如 language_name_zh / language_name_en）。</summary>
        public static string GetLanguageNameKey(UITextLanguage lang)
        {
            switch (lang)
            {
                case UITextLanguage.English: return "language_name_en";
                default: return "language_name_zh";
            }
        }

        /// <summary>
        /// 设置当前语言：加载对应语言表并广播刷新事件。
        /// 相同语言重复设置不重复广播（幂等）。
        /// </summary>
        public static void SetLanguage(UITextLanguage lang)
        {
            EnsureTablesLoaded();
            if (sCurrent == lang)
            {
                return;
            }
            sCurrent = lang;
            sTable = LoadTable(lang);
            sOnLanguageChanged?.Invoke();
        }

        /// <summary>订阅语言变更事件（UILocalizedText 等 UI 在 Awake 注册 / OnDestroy 注销）。</summary>
        public static void RegisterLanguageChanged(Action cb)
        {
            sOnLanguageChanged += cb;
        }

        /// <summary>注销语言变更事件。</summary>
        public static void UnregisterLanguageChanged(Action cb)
        {
            sOnLanguageChanged -= cb;
        }

        /// <summary>按枚举 key 取文案（None 返回空串）；支持 {0}/{1} 占位。</summary>
        public static string Get(UITextKey key, params object[] args)
        {
            if (key == UITextKey.None)
            {
                return "";
            }
            return Get(key.ToString(), args);
        }

        /// <summary>按 key 取文案；支持 {0}/{1} 占位（如分辨率格式）。三级回退：当前表 → 中文表 → key。</summary>
        public static string Get(string key, params object[] args)
        {
            EnsureTablesLoaded();
            string fmt = Resolve(key);
            if (fmt == null)
            {
                return key;   // 两张表都没有 → 回退 key 本身（便于排查）
            }
            if (args == null || args.Length == 0)
            {
                return fmt;
            }
            try
            {
                return string.Format(fmt, args);
            }
            catch (FormatException)
            {
                Debug.LogWarning($"[UITextProvider] 文案格式串错误 key={key}");
                return fmt;
            }
        }

        /// <summary>三级回退取原始文案；全部缺失返回 null。</summary>
        private static string Resolve(string key)
        {
            if (sTable != null && sTable.TryGetValue(key, out string v))
            {
                return v;
            }
            if (sZhFallback != null && sZhFallback.TryGetValue(key, out v))
            {
                return v;
            }
            return null;
        }

        /// <summary>懒加载两张语言表（当前语言表 + 简体中文兜底表）。</summary>
        private static void EnsureTablesLoaded()
        {
            if (sTablesLoaded)
            {
                return;
            }
            sTablesLoaded = true;
            sZhFallback = LoadTable(UITextLanguage.ChineseSimplified);   // 兜底表：简体中文
            sTable = LoadTable(sCurrent);
        }

        /// <summary>按语言加载文案表；文件缺失 / 解析失败返回空表（调用方继续回退）。</summary>
        private static Dictionary<string, string> LoadTable(UITextLanguage lang)
        {
            string path = ResourcePrefix + lang;   // UI/strings_ChineseSimplified / UI/strings_English
            var map = new Dictionary<string, string>();

            TextAsset asset = Resources.Load<TextAsset>(path);
            if (asset == null)
            {
                Debug.LogWarning($"[UITextProvider] 未找到文案文件 {path}.json，该语言文案将回退（中文或 key）");
                return map;
            }

            try
            {
                // 手写轻量 JSON 解析（避免 JsonUtility 对顶层的限制）：仅支持扁平 { "key": "value" }。
                ParseFlatJson(asset.text, map);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UITextProvider] 解析文案文件失败 {path}.json：{e.Message}");
                map.Clear();
            }
            return map;
        }

        /// <summary>
        /// 解析扁平 key-value JSON（值含转义引号/中文）。仅支持字符串值，不做嵌套。
        /// </summary>
        private static void ParseFlatJson(string text, Dictionary<string, string> map)
        {
            int i = 0;
            int n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c == '"')
                {
                    string key = ReadString(text, ref i);
                    // 跳过冒号
                    while (i < n && char.IsWhiteSpace(text[i])) i++;
                    if (i < n && text[i] == ':') i++;
                    while (i < n && char.IsWhiteSpace(text[i])) i++;
                    if (i < n && text[i] == '"')
                    {
                        string value = ReadString(text, ref i);
                        if (!string.IsNullOrEmpty(key))
                        {
                            map[key] = value;
                        }
                    }
                }
                else
                {
                    i++;
                }
            }
        }

        private static string ReadString(string text, ref int i)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            i++; // 跳过开头引号
            int n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < n)
                {
                    char nxt = text[i + 1];
                    switch (nxt)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(nxt); break;
                    }
                    i += 2;
                }
                else if (c == '"')
                {
                    i++;
                    break;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }
    }
}
