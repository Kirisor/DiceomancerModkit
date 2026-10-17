// =============================================================================
//  ModKit.Core · 能力（Ability）解析
//
//  与其它内容同构的解析器，单独一个 partial 文件。
// =============================================================================

using Newtonsoft.Json.Linq;
using ModKit.Api;

namespace ModKit.Core
{
    public static partial class ContentParser
    {
        /// <summary>把 JSON 文本解析成 <see cref="AbilityDef"/>。</summary>
        public static ParseResult<AbilityDef> ParseAbility(string json)
        {
            var report = new ValidationReport();
            var root = TryObject(json, report, "能力");
            if (root == null) { return new ParseResult<AbilityDef>(null, report); }

            var def = new AbilityDef
            {
                Schema = Str(root, "$schema"),
                Id = Str(root, "id"),
                Name = LocalizedMap(root, "name"),
                Trigger = Str(root, "trigger"),
                Type = Str(root, "type"),
                Owner = Str(root, "owner"),
                ArtTemplate = Str(root, "artTemplate"),
                Effects = ParseEffects(root, report)
            };

            // addToPool / isUnique 默认值与游戏侧一致：
            //   addToPool 缺省 true（作者多半希望能力能拿到）
            //   isUnique  缺省 false
            var addToPool = root["addToPool"];
            def.AddToPool = addToPool == null
                || (addToPool.Type == JTokenType.Boolean && addToPool.Value<bool>());

            var isUnique = root["isUnique"];
            def.IsUnique = isUnique != null
                && isUnique.Type == JTokenType.Boolean && isUnique.Value<bool>();

            return new ParseResult<AbilityDef>(def, report);
        }
    }
}
