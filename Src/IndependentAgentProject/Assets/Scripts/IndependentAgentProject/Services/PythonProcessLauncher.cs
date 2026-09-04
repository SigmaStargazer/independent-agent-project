using Cysharp.Threading.Tasks;
using System;
using System.IO;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ProcessWindowStyle = System.Diagnostics.ProcessWindowStyle;
using System.Net.Sockets;
using Network;
using UnityEngine;

namespace Services
{
    /// <summary>
    /// v0.23.3b：Python 进程托管器。
    ///
    /// 职责：
    /// - 打包版（#if !UNITY_EDITOR）：Bootstrap 启动时拉起 <游戏根>/PythonServer/agent_server.exe（无窗口），
    ///   启动前做多开探测（端口文件 + TCP 已通则复用，不重复拉起）。
    /// - 编辑器（#if UNITY_EDITOR）：不拉起 exe，由开发者手动 `uv run python main.py`（保持现状）。
    /// - Unity 进程退出时清理 Python 子进程：静态构造注册 Application.quitting（v0.23.3b 修复，
    ///   不再依赖会被场景切换销毁的 BootstrapEntry.OnApplicationQuit）。
    ///
    /// 与路径约定：打包版 Python exe 位于 <游戏根>/PythonServer/（与 <产品名>_Data 平级），
    /// 端口文件/ api_config.json 位于 <游戏根>/Data/Config/（见 JsonConfigIO.ConfigDir()），
    /// Python 单实例 PID 文件位于 <游戏根>/PythonServer/db/agent_server.pid。
    /// </summary>
    public static class PythonProcessLauncher
    {
        static Process _process;

        /// <summary>
        /// 运行时初始化即注册退出清理（官方推荐方式，不依赖首次访问静态类）。
        /// 修复：BootstrapEntry 挂在 Bootstrap 场景，LoadScene("Title") 后对象销毁，
        /// 其 OnApplicationQuit 不再触发，导致退出时 Python 残留（v0.23.3b 验收发现）。
        /// Shutdown 内部有 #if !UNITY_EDITOR 保护，编辑器模式注册后也是空操作，无副作用。
        /// </summary>
        [RuntimeInitializeOnLoadMethod]
        static void RegisterQuitHandler()
        {
            Application.quitting += Shutdown;
        }

        /// <summary>是否已由本启动器拉起过进程（避免重复 Launch）。</summary>
        public static bool IsLaunched { get; private set; }

        /// <summary>PythonServer 目录：打包 = <游戏根>/PythonServer；编辑器下不启用。</summary>
        static string PythonServerDir
        {
            get
            {
                // 打包版 Application.dataPath = <游戏根>/<产品名>_Data，上级即游戏根
                var gameRoot = Directory.GetParent(Application.dataPath)?.FullName;
                return Path.Combine(gameRoot ?? string.Empty, "PythonServer");
            }
        }

        /// <summary>Python 单实例 PID 文件（与 Python runtime.path_config.get_pid_file() 一致）。</summary>
        static string PidFile
        {
            get { return Path.Combine(PythonServerDir, "db", "agent_server.pid"); }
        }

        /// <summary>
        /// 启动 Python 服务端（仅打包版）。编辑器下不拉起，由开发者手动启动。
        /// 已在运行（端口文件 + TCP 可连）则复用，不重复拉起第二个实例。
        /// </summary>
        public static void Launch()
        {
#if UNITY_EDITOR
            Debug.Log("[PythonProcessLauncher] 编辑器模式：不自动拉起 Python，请开发者手动 `uv run python main.py` 启动。");
#else
            if (IsLaunched)
            {
                Debug.Log("[PythonProcessLauncher] 已启动过，跳过。");
                return;
            }

            // 多开互斥（Unity 侧第一道防线）：已有 Python 实例则直接复用
            if (IsPythonAlive())
            {
                Debug.Log("[PythonProcessLauncher] 检测到已有 Python 实例，直接复用，不重复拉起。");
                IsLaunched = true;
                return;
            }

            string exePath = Path.Combine(PythonServerDir, "agent_server.exe");
            if (!File.Exists(exePath))
            {
                Debug.LogError($"[PythonProcessLauncher] 未找到 {exePath}，请确认已执行 Tools/build_python_exe.cmd。");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = PythonServerDir,   // 保证 db/ 相对路径生效
                UseShellExecute = false,
                CreateNoWindow = true,                 // 无窗口
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            try
            {
                _process = Process.Start(psi);
                IsLaunched = true;
                Debug.Log($"[PythonProcessLauncher] 已启动 {exePath} (PID={_process?.Id})");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PythonProcessLauncher] 启动失败: {e.Message}");
            }
#endif
        }

        /// <summary>
        /// Unity 进程退出时清理 Python 子进程（Application.quitting 触发；BootstrapEntry.OnApplicationQuit 双保险）。
        /// 优雅关闭：发 CloseRequest（Python 停止 Agent、flush 记忆、释放 Kuzu 文件锁），
        /// 等 ≤2s，仍存活则 Kill 兜底。
        /// 兼容两种场景：自己拉起的（_process 非空）与复用的已有实例（_process 为空，靠 PID 文件定位）。
        /// </summary>
        public static void Shutdown()
        {
#if !UNITY_EDITOR
            try
            {
                // 1. 发 CloseRequest（连接可用才发；Python 收到后自行清理并退出）
                TrySendCloseRequest();

                // 2. 定位进程：优先用自己拉起的引用；为空则读 PID 文件（覆盖复用已有实例的情况）
                var target = _process ?? ResolveRunningProcess();
                if (target == null)
                {
                    Debug.Log("[PythonProcessLauncher] 未找到需要清理的 Python 进程（可能未启动或已退出）。");
                    return;
                }

                // 3. 等 ≤2s 优雅退出，超时则 Kill 兜底
                if (!target.WaitForExit(2000))
                {
                    Debug.LogWarning("[PythonProcessLauncher] Python 未在 2s 内退出，强制 Kill。");
                    target.Kill();
                    target.WaitForExit(1000);
                }
                else
                {
                    Debug.Log("[PythonProcessLauncher] Python 已优雅退出。");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PythonProcessLauncher] 清理 Python 进程时出错: {e.Message}");
            }
            finally
            {
                _process?.Dispose();
                _process = null;
            }
#endif
        }

        /// <summary>读取 Python 单实例 PID 文件，返回对应进程引用；文件缺失或进程不存在则返回 null。</summary>
        static Process ResolveRunningProcess()
        {
            try
            {
                if (!File.Exists(PidFile))
                {
                    return null;
                }
                string content = File.ReadAllText(PidFile).Trim();
                if (!int.TryParse(content, out int pid) || pid <= 0)
                {
                    return null;
                }
                return Process.GetProcessById(pid); // 进程不存在会抛 ArgumentException，由外层 catch 兜底
            }
            catch
            {
                return null;
            }
        }

        /// <summary>向 Python 发送 CloseRequest（尽力而为；连接未就绪则静默跳过）。</summary>
        static void TrySendCloseRequest()
        {
            try
            {
                if (AgentClient.Instance != null && AgentClient.Instance.Connected)
                {
                    // 同步发送即可（不等待响应）；Python 收到后自行清理并退出
                    AgentService.Instance.SendClose();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PythonProcessLauncher] 发送 CloseRequest 失败: {e.Message}");
            }
        }

        /// <summary>多开探测：读端口文件 + TCP 连接，已有 Python 实例返回 true。</summary>
        static bool IsPythonAlive()
        {
            try
            {
                int port = ReadPortFile();
                if (port <= 0)
                {
                    return false;
                }
                using var client = new TcpClient();
                client.Connect("127.0.0.1", port);
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>读取端口文件（与 JsonConfigIO.ConfigDir() 同目录规则：编辑器=src/Data/Config，打包=游戏根/Data/Config）。</summary>
        static int ReadPortFile()
        {
            try
            {
                string dir = JsonConfigIO.ConfigDir();
                string file = Path.Combine(dir, "agent_server_port.txt");
                if (!File.Exists(file))
                {
                    return 0;
                }
                string content = File.ReadAllText(file).Trim();
                return int.TryParse(content, out int port) ? port : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
