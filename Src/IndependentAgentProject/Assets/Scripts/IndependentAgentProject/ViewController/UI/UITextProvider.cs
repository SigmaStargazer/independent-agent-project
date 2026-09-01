using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 文案 key 枚举（v0.23.4）：Inspector 里用下拉框选择，避免策划手填字符串拼错。
    /// 枚举成员名 == 文案文件 strings_zh_CN.json 中的 key（None 表示不取文案）。
    /// </summary>
    public enum UITextKey
    {
        None,                  // 不赋值（保持场景手动配置的文案）
        tab_model_config,      // 模型配置
        tab_display_settings,  // 画面
        mode_windowed,         // 窗口化
        mode_borderless,       // 无边框
        mode_fullscreen,       // 全屏
        resolution_format,     // {0} x {1}
    }

    /// <summary>
    /// 文案查表（v0.23.4）：从数据驱动文件读取 UI 文案，代码中不出现中文字面量。
    /// 文案文件：<code>Assets/Resources/UI/strings_zh_CN.json</code>（UTF-8，key-value）。
    /// 缺 key 时回退返回 key 本身，便于排查。
    /// 未来加语言 = 新增一个语言文件 + 运行时按当前语言加载，代码/场景零改动（本地化铺路）。
    /// </summary>
    public static class UITextProvider
    {
        private const string ResourcePath = "UI/strings_zh_CN";
        private static Dictionary<string, string> sTable;
        private static bool sLoaded;

        /// <summary>按枚举 key 取文案（None 返回空串）；支持 {0}/{1} 占位。</summary>
        public static string Get(UITextKey key, params object[] args)
        {
            if (key == UITextKey.None)
            {
                return "";
            }
            return Get(key.ToString(), args);
        }

        /// <summary>按 key 取文案；支持 {0}/{1} 占位（如分辨率格式）。</summary>
        public static string Get(string key, params object[] args)
        {
            EnsureLoaded();
            if (sTable != null && sTable.TryGetValue(key, out string fmt))
            {
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
            return key;
        }

        private static void EnsureLoaded()
        {
            if (sLoaded)
            {
                return;
            }
            sLoaded = true;
            sTable = new Dictionary<string, string>();

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[UITextProvider] 未找到文案文件 {ResourcePath}.json，UI 文案将回退为 key");
                return;
            }

            try
            {
                // 手写轻量 JSON 解析（避免 JsonUtility 对顶层的限制）：仅支持扁平 { "key": "value" }。
                ParseFlatJson(asset.text, sTable);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UITextProvider] 解析文案文件失败：{e.Message}");
                sTable.Clear();
            }
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
