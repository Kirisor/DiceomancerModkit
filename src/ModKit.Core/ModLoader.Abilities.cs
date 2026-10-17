// =============================================================================
//  ModKit.Core · 能力（Ability）加载
//
//  与关键词同构的加载器，单独一个 partial 文件。
// =============================================================================

using System;
using ModKit.Api;

namespace ModKit.Core
{
    public partial class ModLoader
    {
        /// <summary>
        /// 加载能力声明。
        /// </summary>
        /// <remarks>
        /// 与关键词**同构**：这一步只做"读声明 + 校验 + 记下来"，
        /// 真正的装配（克隆原版能力作模板、写进 <c>AbilityDataManager</c> 的分类桶）
        /// 由 Game 层的 ContentAssembler 完成。
        /// <para>
        /// 一个差别：能力的"进哪个池"由 <c>type</c> × <c>owner</c> 决定，
        /// 而这两个值平台**校验为必填**（游戏在 <c>isInRandomPool</c> 为真时按它们分桶，
        /// 猜错会让能力静默不在玩家的池子里）。
        /// </para>
        /// </remarks>
        private void LoadAbilities(ModLoadResult result, string modPath, string modId, IContentCapabilities caps)
        {
            var files = result.Manifest.Abilities;
            if (files == null || files.Count == 0) { return; }

            foreach (var rel in files)
            {
                if (string.IsNullOrEmpty(rel)) { continue; }

                string full = ResolveContentPath(modPath, rel);
                string text = full == null ? null : _fs.ReadText(full);
                if (text == null)
                {
                    result.Report.Error(modId, "abilities", "清单声明了 " + rel + "，但文件读不到。");
                    continue;
                }

                var parsed = ContentParser.ParseAbility(text);
                var report = parsed.Report;
                if (parsed.Value != null)
                {
                    report.Merge(ContentValidator.ValidateAbility(text, caps));
                }

                result.Report.Merge(report);

                if (parsed.Value != null && !report.HasErrors)
                {
                    // 与其它内容同一口径：加 mod 前缀防跨 mod 撞名
                    if (!string.IsNullOrEmpty(parsed.Value.Id) &&
                        !parsed.Value.Id.StartsWith(modId + ".", StringComparison.Ordinal))
                    {
                        parsed.Value.Id = modId + "." + parsed.Value.Id;
                    }
                    result.Abilities.Add(parsed.Value);
                }
                else
                {
                    _warn("能力文件被拒：" + rel + "（" + report.ErrorCount + " 条错误）");
                }
            }
        }
    }
}
