using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ModKit.Studio
{
    /// <summary>
    /// 关键词「行为类」的内嵌编辑器：生成 / 载入 / 保存 <c>&lt;mod&gt;/src/CardKeywordXxx.cs</c>。
    /// 平台规定关键词本身只是标签，真实行为必须写在游戏的 CardKeyword 子类里，所以这里提供直接编辑入口。
    /// </summary>
    public partial class MainWindow
    {
        private string _keywordClassPath;

        /// <summary>关键词 id → 行为类名：milk_frog → CardKeywordMilkFrog。</summary>
        private static string KeywordClassName(string id)
        {
            var parts = (id ?? "").Split(new[] { '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder("CardKeyword");
            foreach (var q in parts)
            {
                if (q.Length == 0) { continue; }
                sb.Append(char.ToUpperInvariant(q[0]));
                if (q.Length > 1) { sb.Append(q.Substring(1)); }
            }
            return sb.ToString();
        }

        /// <summary>把当前关键词的行为类读进编辑框（没有则留空并提示）。</summary>
        private void ShowKeywordClass(string id)
        {
            _keywordClassPath = null;
            KeywordClassBox.Text = "";
            KeywordClassPath.Text = "";
            if (_mod == null || string.IsNullOrEmpty(id)) { return; }

            var name = KeywordClassName(id);
            var file = System.IO.Path.Combine(_mod.Dir, "src", name + ".cs");
            if (!File.Exists(file))
            {
                KeywordClassPath.Text = "（还没有行为类；点「生成 / 载入行为类」创建）";
                return;
            }
            try
            {
                _keywordClassPath = file;
                KeywordClassBox.Text = File.ReadAllText(file, Encoding.UTF8);
                KeywordClassPath.Text = "src/" + name + ".cs";
            }
            catch (Exception ex) { KeywordClassPath.Text = "读取失败：" + ex.Message; }
        }

        private void OnLoadKeywordClassClick(object sender, RoutedEventArgs e)
        {
            if (_mod == null || _current == null) { Status("先在关键词列表里选一个"); return; }
            var id = (string)_current.Root["id"] ?? "";
            var name = KeywordClassName(id);
            var file = System.IO.Path.Combine(_mod.Dir, "src", name + ".cs");
            if (File.Exists(file)) { ShowKeywordClass(id); Status("已载入 " + name + ".cs"); return; }

            OnGenerateKeywordClassClick(sender, e);
            ShowKeywordClass(id);
        }

        private void OnSaveKeywordClassClick(object sender, RoutedEventArgs e)
        {
            if (_mod == null) { Status("先选一个模组"); return; }
            if (string.IsNullOrWhiteSpace(KeywordClassBox.Text)) { Status("行为类内容是空的"); return; }
            var id = _current == null ? null : (string)_current.Root["id"];
            if (string.IsNullOrEmpty(id)) { Status("先在关键词列表里选一个"); return; }

            var name = KeywordClassName(id);
            var srcDir = System.IO.Path.Combine(_mod.Dir, "src");
            Directory.CreateDirectory(srcDir);
            var file = System.IO.Path.Combine(srcDir, name + ".cs");
            try
            {
                if (File.Exists(file))
                {
                    File.Copy(file, file + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", true);
                }
                File.WriteAllText(file, KeywordClassBox.Text, new UTF8Encoding(false));
                _keywordClassPath = file;
                KeywordClassPath.Text = "src/" + name + ".cs";
                Status("已保存 " + name + ".cs（需编译成 dll 后才生效）");
            }
            catch (Exception ex) { Status("保存失败：" + ex.Message); }
        }
    }
}
