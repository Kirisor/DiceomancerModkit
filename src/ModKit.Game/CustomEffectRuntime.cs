// =============================================================================
//  自定义效果（作者自写 C#）的运行时 —— 平台侧适配层
//
//  ★ 为什么需要这一层（2026-10-13 实机定位）：
//    游戏按**字面拼名**找类：`Utilities/EffectUtils.cs:27`
//        empty = "EffectSpEffect" + effectInfo.specialEffectString;   // 命名空间 Effects.Special.CardEffect.
//    然后 `:40` `Activator.CreateInstance(Type.GetType(name)) as EffectBase`、`:41` 立刻用 `effectBase.EffectOwner`。
//    ⇒ 作者按手册继承的是 `ModKit.Api.SyncCardEffect`（**不是** `Effects.EffectBase`）⇒ `as EffectBase` 转成 null
//      ⇒ `NullReferenceException`（症状是"卡发不出去/打出即崩"）。
//    所以平台必须**按需生成一个真的 `EffectBase` 子类**（本文件的 Emitter），
//    把 `ApplyEffectForTarget` 转发给作者的 `ICardEffect`（本文件的 Forwarder）。
//
//  ★ 本文件是"最小打通"版：
//    · 真实现：Amount / AmountAt / Block(Self) / Log
//    · 其余成员**必须存在**（否则编译不过），但调用时记一条 Warn 并跳过 —— 宁可吵闹，也不要静默无动作。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Combat;
using Effects;
using ModKit.Api;
using ModKit.Core;

namespace ModKit.Game
{
    /// <summary>
    /// 作者效果类在游戏侧的**真身**：`Effects.Special.CardEffect.EffectSpEffect&lt;Id&gt; : EffectBase`。
    /// </summary>
    /// <remarks>
    /// 生成出来的类只有一个薄壳：`ApplyEffectForTarget` 与 `GetEffectDesKey`，两个方法体都是"转调"。
    /// 之所以能这么薄：游戏里 `EffectSpEffectBiggerDice` 这种**空壳类**（99 B，只有 `: EffectBase`）都能正常工作。
    /// </remarks>
    internal static class EffectBaseAdapterEmitter
    {
        private const string Ns = "Effects.Special.CardEffect.";
        private const string Prefix = "EffectSpEffect";

        /// <summary>生成的程序集（只建一次）。</summary>
        private static readonly ModuleBuilder Module = CreateModule();

        /// <summary>id → 已生成的类型（避免重复 Emit）。</summary>
        private static readonly Dictionary<string, Type> Cache = new Dictionary<string, Type>();

        /// <summary>生成出来的类型 → 作者效果 id（`Forwarder` 用它反查作者类型）。</summary>
        private static readonly Dictionary<Type, string> IdByType = new Dictionary<Type, string>();

        private static readonly object Gate = new object();

        private static ModuleBuilder CreateModule()
        {
            var asm = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("ModKitEmittedEffects"), AssemblyBuilderAccess.Run);
            return asm.DefineDynamicModule("ModKitEmittedEffects");
        }

        /// <summary>
        /// 桥的兜底入口：给定的名字是不是"平台该生成的特殊效果类"，是就生成并返回。
        /// </summary>
        /// <remarks>
        /// 由 <see cref="ContentRegistry.EmitFallback"/> 调用（Core 层不引用游戏类型，所以用委托挂钩）。
        /// 只处理 `Effects.Special.CardEffect.EffectSpEffect&lt;X&gt;` 这一族；其它名字一律返回 null（保持原版语义）。
        /// </remarks>
        public static Type TryEmit(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) { return null; }
            if (!typeName.StartsWith(Ns + Prefix, StringComparison.Ordinal)) { return null; }

            var id = typeName.Substring(Ns.Length + Prefix.Length);
            if (id.Length == 0) { return null; }

            lock (Gate)
            {
                Type cached;
                if (Cache.TryGetValue(id, out cached)) { return cached; }

                // 作者必须真的登记过这个 id；否则不生成（让游戏走原本的 null 路径，保持原版报错语义）
                var authorType = PlatformRuntime.Registry == null ? null : PlatformRuntime.Registry.ResolveEffect(id);
                if (authorType == null) { return null; }

                var emitted = Emit(id);
                if (emitted == null) { return null; }

                Cache[id] = emitted;
                IdByType[emitted] = id;

                ModKitLog.Info("自定义效果：已生成游戏侧适配类 " + typeName
                    + "（作者类 " + authorType.FullName + "）");
                return emitted;
            }
        }

        /// <summary>给 <see cref="CustomEffectForwarder"/> 用：由生成类型反查作者效果 id。</summary>
        public static string IdOf(Type emittedType)
        {
            string id;
            return emittedType != null && IdByType.TryGetValue(emittedType, out id) ? id : null;
        }

        private static Type Emit(string id)
        {
            var tb = Module.DefineType(Ns + Prefix + id,
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
                typeof(EffectBase));

            // protected override UniTask ApplyEffectForTarget(Character targetCharacter)
            //   => CustomEffectForwarder.Run(this, targetCharacter);
            var apply = tb.DefineMethod("ApplyEffectForTarget",
                MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(UniTask), new[] { typeof(Character) });
            var run = typeof(CustomEffectForwarder).GetMethod("Run",
                BindingFlags.Public | BindingFlags.Static);
            var il = apply.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, run);
            il.Emit(OpCodes.Ret);

            // public override string GetEffectDesKey() => "<id>";
            // ★ 不重写它会落到基类逻辑上，卡面很可能显示成 key 名/缺失文案。
            var desKey = tb.DefineMethod("GetEffectDesKey",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(string), Type.EmptyTypes);
            var il2 = desKey.GetILGenerator();
            il2.Emit(OpCodes.Ldstr, "Key_" + id);
            il2.Emit(OpCodes.Ret);

            return tb.CreateType();
        }
    }

    /// <summary>把游戏侧的 <see cref="EffectBase"/> 实例转成作者 <c>ICardEffect</c> 的调用。</summary>
    public static class CustomEffectForwarder
    {
        /// <summary>游戏回调入口（由生成类调用）。</summary>
        public static UniTask Run(EffectBase self, Character target)
        {
            try
            {
                var id = EffectBaseAdapterEmitter.IdOf(self == null ? null : self.GetType());
                var authorType = id == null || PlatformRuntime.Registry == null
                    ? null : PlatformRuntime.Registry.ResolveEffect(id);
                if (authorType == null)
                {
                    ModKitLog.Error("自定义效果：找不到作者类（id=" + (id ?? "?") + "）");
                    return UniTask.CompletedTask;
                }

                var instance = Activator.CreateInstance(authorType) as ICardEffect;
                if (instance == null)
                {
                    ModKitLog.Error("自定义效果：作者类不是 ICardEffect（" + authorType.FullName + "）");
                    return UniTask.CompletedTask;
                }

                var ctx = new GameEffectContext(self, target);
                var sync = instance as SyncCardEffect;
                if (sync != null) { sync.Apply(ctx); return UniTask.CompletedTask; }

                // ★ 异步作者效果：本版**不接通**（诚实标注）。
                //   原因：`ICardEffect.ApplyAsync` 返回 `System.Threading.Tasks.Task`，而这里必须返回 `UniTask`；
                //   把 Task 转 UniTask 需要 net472 里没有的 `ValueTask`（编译期 CS0518）。
                //   样例走的是 `SyncCardEffect`（同步），所以这条路先记 Warn 并跳过，
                //   等有异步作者需求时再用 `UniTask.Create(async () => await task)` 之类的方式接。
                ModKitLog.Warn("自定义效果：作者类实现了异步的 ApplyAsync，本版适配层尚未接通（" + authorType.FullName + "）");
                return UniTask.CompletedTask;
            }
            catch (Exception ex)
            {
                ModKitLog.Error("自定义效果执行失败：" + ex.GetType().Name + ": " + ex.Message);
                return UniTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// 作者效果的运行时上下文（最小打通版）。
    /// </summary>
    /// <remarks>
    /// ★ 已**真实现**：<see cref="Amount"/> / <see cref="AmountAt"/> / <see cref="Block"/>（Self）/ <see cref="Log"/>。
    /// ★ 其余成员**故意**先记 Warn 再跳过：编译期必须齐全，运行期宁可吵闹也不静默。
    ///   逐个补齐时，请照着平台已验证的内置效果写法翻（`EffectSpEffectCleanMana` 用 `SubCommandRemoveMana`、
    ///   `EffectChangeMaxHealth` 用 `SubCommandChangeMaxHealth`；格挡用 `CharacterPlayer.ApplyBlock`，本文件已在用）。
    /// </remarks>
    internal sealed class GameEffectContext : EffectContext
    {
        private readonly EffectBase _self;
        private readonly Character _target;
        private readonly System.Random _random = new System.Random();
        private readonly HashSet<string> _warned = new HashSet<string>();

        public GameEffectContext(EffectBase self, Character target)
        {
            _self = self;
            _target = target;
        }

        // ---------------- 读：真实现 ----------------

        public override int Amount { get { return AmountAt(0); } }

        public override int AmountAt(int index)
        {
            var nums = _self == null ? null : _self.rNumbers;
            if (nums == null || index < 0 || index >= nums.Length || nums[index] == null) { return 0; }
            return nums[index].GetCollapsedNumber();
        }

        public override ICombatant Self
        {
            get
            {
                // ★ 已知缺口（2026-10-13 编译期暴露）：`Combat.CharacterPlayer` **不是** `ModKit.Api.ICombatant`
                //   —— 也就是说 `ICombatant` 在游戏侧**还没有实现类**（这是"执行层从未写出来"的又一处证据）。
                //   本版先返回 null 并 Warn；补齐时要写一个 `GameCombatant : ICombatant` 包住 `Combat.Character`。
                WarnOnce("get_Self");
                return null;
            }
        }

        public override string OwnerName { get { return "Player"; } }

        public override int HandCount
        {
            get
            {
                var cm = CardManager.Instance;
                return cm == null || cm.cardHand == null ? 0 : cm.cardHand.Count;
            }
        }

        public override int Mana
        {
            get
            {
                var mm = ManaManager.Instance;
                return mm == null ? 0 : (int)mm.GetManaArray().Length;
            }
        }

        public override System.Random Random { get { return _random; } }

        public override void Log(string message)
        {
            ModKitLog.Info("[custom] " + message);
        }

        // ---------------- 写：真实现（只做已实证的那一个） ----------------

        public override void Block(int amount, CombatantSelector target = CombatantSelector.Self)
        {
            if (target != CombatantSelector.Self)
            {
                WarnOnce("Block(target=" + target + ")");
                return;
            }
            var player = CharacterPlayer.Instance;
            if (player == null) { ModKitLog.Error("自定义效果：CharacterPlayer.Instance 为 null"); return; }
            player.ApplyBlock(amount);
        }

        // ---------------- 其余：编译期齐全 + 运行期 Warn ----------------

        public override void DealDamage(int amount, DamageMode mode, CombatantSelector target = CombatantSelector.Opponent)
        { WarnOnce("DealDamage"); }

        public override void Heal(int amount, CombatantSelector target = CombatantSelector.Self)
        { WarnOnce("Heal"); }

        public override void LoseLife(int amount, CombatantSelector target = CombatantSelector.Opponent)
        { WarnOnce("LoseLife"); }

        public override void Draw(int amount) { WarnOnce("Draw"); }
        public override void DiscardRandom(int amount) { WarnOnce("DiscardRandom"); }

        public override void GainMana(ManaColor color, int amount) { WarnOnce("GainMana"); }
        public override void SetMaxMana(int amount) { WarnOnce("SetMaxMana"); }
        public override void CleanMana() { WarnOnce("CleanMana"); }

        public override void AddState(string stateName, int amount, CombatantSelector target = CombatantSelector.Self)
        { WarnOnce("AddState"); }

        public override void ReduceState(string stateName, int amount, CombatantSelector target = CombatantSelector.Self)
        { WarnOnce("ReduceState"); }

        public override void GiveAbility(string abilityName, int level, CombatantSelector target = CombatantSelector.Self)
        { WarnOnce("GiveAbility"); }

        public override void GiveCard(string cardId, CardDestination destination, int amount = 1)
        { WarnOnce("GiveCard"); }

        public override void GiveTemporaryCard(string cardId, CardDestination destination, int amount = 1)
        { WarnOnce("GiveTemporaryCard"); }

        public override void UpgradeCard(ICardRef card) { WarnOnce("UpgradeCard"); }
        public override void EndTurn() { WarnOnce("EndTurn"); }

        public override void SubscribeTrigger(string triggerName, Action<TriggerEvent> handler)
        { WarnOnce("SubscribeTrigger"); }

        public override ICombatant Opponent { get { WarnOnce("get_Opponent"); return null; } }

        public override IReadOnlyList<ICombatant> Targets
        {
            get
            {
                WarnOnce("get_Targets");
                return new List<ICombatant>();
            }
        }

        public override int LibraryCount { get { WarnOnce("get_LibraryCount"); return 0; } }
        public override int GraveCount { get { WarnOnce("get_GraveCount"); return 0; } }

        public override IReadOnlyList<ICardRef> HandCards
        {
            get
            {
                WarnOnce("get_HandCards");
                return new List<ICardRef>();
            }
        }

        public override int MaxMana { get { WarnOnce("get_MaxMana"); return 0; } }

        private void WarnOnce(string member)
        {
            if (_warned.Add(member))
            {
                ModKitLog.Warn("自定义效果的上下文成员尚未实现：" + member
                    + "（作者调用了它，但平台这版适配层还没接上）");
            }
        }
    }
}
