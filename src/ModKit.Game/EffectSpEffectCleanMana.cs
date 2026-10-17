// =============================================================================
//  ModKit.Game · 平台自建的特殊效果：清空法力（内置语义 clean_mana 的落地）
//
//  ★ 为什么平台要自己写一个：
//    游戏里**根本没有**"只清空法力"的效果类。全库 grep `CleanMana` 零命中；
//    27 个普通效果类（Effects/Effect*.cs）里有 EffectAddMana / EffectChangeMaxManaCount，
//    但没有 EffectCleanMana。旧映射把 effectName 写成 "CleanMana" ⇒
//    EffectUtils.GenerateEffectFromEffectInfo 拼出 "Effects.EffectCleanMana" ⇒
//    Type.GetType 返回 null ⇒ Activator.CreateInstance(null) 抛
//    `ArgumentNullException: Value cannot be null. Parameter name: type`
//    （2026-10-13 实机日志：fx_clean_mana 发卡失败，正是这个异常）。
//
//  ★ 游戏自己怎么清空法力：
//    ManaManager.RemoveMana 是 **private async**，外部调不到；
//    ManaManager.CleanAllMana() 虽 public，但全项目**只有一个调用点** ——
//    BattleManager.cs:245（战斗结束结算里）。
//    卡牌效果级别的原版先例是 EffectSpEffectRemoveAllManaAndDamage（能量爆裂）：
//        new SubCommandRemoveMana(ManaManager.Instance.GetManaArray());
//    ⇒ 本类照抄同一口径，只用公开 API，不碰 private 成员。
//
//  ★ 为什么放在平台程序集里也能被游戏找到：
//    效果工厂里的 Type.GetType 已被 Transpiler 桥（ModKitBridge.Resolve）接管，
//    它搜索**全部已加载程序集**，所以
//    Effects.Special.CardEffect.EffectSpEffectCleanMana 放在 ModKit.Game.dll
//    里同样能被实例化（与 EffectSpAbilityBlockGainAddSub 同理）。
//
//  ★ 卡面文案：GetEffectDesKey 返回 "Key_CleanMana"，而游戏自带的 EffectDescription
//    表里没有这个键（那是游戏为自己的效果准备的键）。所以平台必须自己补一条 ——
//    见 PlatformRuntime.AddBuiltinEffectDescriptions。漏了它，卡面会显示
//    "No translation found for 'Key_CleanMana' in EffectDescription"。
// =============================================================================

using Cysharp.Threading.Tasks;
using Command;
using Combat;

namespace Effects.Special.CardEffect
{
    /// <summary>清空场上全部法力（平台内置语义 <c>clean_mana</c> 的实现）。</summary>
    /// <remarks>
    /// </remarks>
    public sealed class EffectSpEffectCleanMana : EffectBase
    {
        protected override UniTask ApplyEffectForTarget(Character targetCharacter)
        {
            // ★ 构造即入队 —— 这是 SubCommand 的既有契约
            //   （Command/SubCommandRemoveMana.cs:16，enqueue 默认 true）。
            //   语义与效果类名一致：把当前场上每一颗法力逐颗移除。
            new SubCommandRemoveMana(ManaManager.Instance.GetManaArray());
            return UniTask.CompletedTask;
        }

        public override string GetEffectDesKey()
        {
            return "Key_CleanMana";
        }
    }
}
