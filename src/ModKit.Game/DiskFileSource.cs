// =============================================================================
//  ModKit.Game · 磁盘文件源
//
//  实现 Core 定义的 IModFileSource。放在 Game 层而不是 Core，
//  是因为"读磁盘"是环境相关行为；Core 只认识接口，
//  因此它的 Loader 逻辑能在内存假文件系统上跑完整回归。
//
//  ★ 平台对游戏目录**只读**：本类只做目录枚举与文本读取，
//    不含任何写入/创建/删除路径。mod 内容住在
//    <游戏>\BepInEx\DiceomancerMods\ 下（独立命名空间，与 A 级资产无关）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using ModKit.Core;

namespace ModKit.Game
{
    /// <summary>从磁盘读 mod 文件。</summary>
    public sealed class DiskFileSource : IModFileSource
    {
        private readonly IGameLog _log;

        public DiskFileSource(IGameLog log)
        {
            _log = log;
        }

        /// <summary>
        /// mod 的存放位置：<c>&lt;游戏根&gt;\BepInEx\DiceomancerMods\</c>。
        /// </summary>
        /// <remarks>
        /// 刻意放在 BepInEx 命名空间下而不是 <c>Diceomancer_Data\</c> 下：
        /// 后者是 A 级受保护资产（第 6 步哈希闸门逐文件锁定），
        /// 往那里写会直接破坏"不动资产"这条根本约束。
        /// </remarks>
        public static string DefaultModsRoot
        {
            get
            {
                // Paths.GameRootPath 由 BepInEx 提供
                return Path.Combine(BepInEx.Paths.GameRootPath, "BepInEx", "DiceomancerMods");
            }
        }

        public IReadOnlyList<string> ListModDirectories(string root)
        {
            var result = new List<string>();

            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                if (_log != null) { _log.Info("mod 根目录不存在（尚未创建）：" + root); }
                return result;
            }

            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    // 跳过以下划线开头的目录：留给作者放草稿/禁用项，
                    // 与"平台不加载"的约定一致（作者把 mod 目录改名加 _ 即停用）。
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name) || name.StartsWith("_", StringComparison.Ordinal) || name.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    result.Add(name);
                }
                result.Sort(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                if (_log != null) { _log.Error("枚举 mod 目录失败：" + root, ex); }
            }

            return result;
        }

        public string ReadText(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { return null; }
                return File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                if (_log != null) { _log.Warn("读文件失败：" + path + " —— " + ex.GetType().Name); }
                return null;
            }
        }

        public bool Exists(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public string Combine(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) { return b; }
            if (string.IsNullOrEmpty(b)) { return a; }
            return Path.Combine(a, b);
        }
    }
}
