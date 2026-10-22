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
using Ability;
using Card;
using Combat;
using Command;
using Effects;
using RollNumber;
using HarmonyLib;
using GameData;
using Keyword;
using Utilities;
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
    /// 作者效果的运行时上下文（**已接通**：读写都落到游戏真实命令/字段上）。
    /// </summary>
    /// <remarks>
    /// ★ 2026-10-21 实机暴露：上一版只有 Amount/AmountAt/Block(Self)/Log 是真实现，
    ///   其余成员 WarnOnce 后**静默跳过** —— 表现就是"自定义效果没触发"（蛙群合唱/疗泥/蛙王轰鸣）。
    ///   本版按游戏原版 <c>Effects/EffectXxx.cs</c> 的写法逐个接通：
    ///   伤害/失去生命走 <c>SubCommandDealDamage</c>，治疗走 <c>SubCommandHeal</c>，
    ///   抽牌走 <c>CardManager.DrawCards</c>，弃牌走 <c>SubCommandDiscardRandomCard</c>，
    ///   能量走 <c>SubCommandAddManaS</c>，能力走 <c>SubCommandApplyAbility</c>，
    ///   状态走 <c>Character.SetSpecialStateToCertainNumber</c>，
    ///   生成卡走 <c>SubCommandAddCardToPlayer</c>。
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

        // ---------------- 读 ----------------

        public override int Amount { get { return AmountAt(0); } }

        public override int AmountAt(int index)
        {
            var nums = _self == null ? null : _self.rNumbers;
            if (nums == null || index < 0 || index >= nums.Length || nums[index] == null) { return 0; }
            return nums[index].GetCollapsedNumber();
        }

        private Character OwnerCharacter
        {
            get
            {
                try { return _self == null || _self.EffectOwner == null ? null : _self.EffectOwner.GetOwnerCharacter(); }
                catch { return null; }
            }
        }

        public override ICombatant Self { get { return Wrap(OwnerCharacter); } }
        public override ICombatant Opponent { get { return Wrap(FirstOpponent()); } }

        public override IReadOnlyList<ICombatant> Targets
        {
            get
            {
                var list = new List<ICombatant>();
                foreach (var ch in Resolve(CombatantSelector.Opponent)) { list.Add(Wrap(ch)); }
                return list;
            }
        }

        public override string OwnerName
        {
            get { var c = OwnerCharacter; return c == null ? "Player" : c.name; }
        }

        public override int HandCount
        {
            get { var cm = CardManager.Instance; return cm == null || cm.cardHand == null ? 0 : cm.cardHand.Count; }
        }

        public override int LibraryCount
        {
            get { var cm = CardManager.Instance; return cm == null ? 0 : cm.GetCardLibrary().Length; }
        }

        public override int GraveCount
        {
            get { var cm = CardManager.Instance; return cm == null ? 0 : cm.GetCardGrave().Length; }
        }

        public override IReadOnlyList<ICardRef> HandCards
        {
            get
            {
                var list = new List<ICardRef>();
                var cm = CardManager.Instance;
                if (cm == null || cm.cardHand == null) { return list; }
                foreach (var c in cm.cardHand) { list.Add(new GameCardRef(c)); }
                return list;
            }
        }

        public override int Mana
        {
            get { var mm = ManaManager.Instance; return mm == null ? 0 : mm.GetManaArray().Length; }
        }

        public override int MaxMana
        {
            get { var mm = ManaManager.Instance; return mm == null ? 0 : mm.GetMaxCount(); }
        }

        public override System.Random Random { get { return _random; } }

        public override void Log(string message) { ModKitLog.Info("[custom] " + message); }

        // ---------------- 写（照原版 Effects/*.cs 的写法） ----------------

        public override void DealDamage(int amount, DamageMode mode, CombatantSelector target = CombatantSelector.Opponent)
        {
            if (amount <= 0) { return; }
            var from = OwnerCharacter;
            if (from == null) { WarnOnce("DealDamage(no owner)"); return; }

            foreach (var ch in Resolve(target))
            {
                var dmgType = mode == DamageMode.Direct
                    ? DamageSourceType.DirectDamage
                    : DamageSourceType.IndirectDamage;
                var special = mode == DamageMode.LoseLife
                    ? DamageSpecialType.DirectLoseLife
                    : DamageSpecialType.Standard;
                new SubCommandDealDamage(new Damage(amount, from, ch, dmgType, special));
            }
        }

        public override void Block(int amount, CombatantSelector target = CombatantSelector.Self)
        {
            if (amount <= 0) { return; }
            if (target != CombatantSelector.Self)
            {
                WarnOnce("Block(target=" + target + ")");
                return;
            }
            var player = CharacterPlayer.Instance;
            if (player == null) { ModKitLog.Error("自定义效果：CharacterPlayer.Instance 为 null"); return; }
            player.ApplyBlock(amount);
        }

        public override void Heal(int amount, CombatantSelector target = CombatantSelector.Self)
        {
            if (amount <= 0) { return; }
            foreach (var ch in Resolve(target)) { new SubCommandHeal(ch, amount); }
        }

        public override void LoseLife(int amount, CombatantSelector target = CombatantSelector.Opponent)
        {
            if (amount <= 0) { return; }
            DealDamage(amount, DamageMode.LoseLife, target);
        }

        public override void Draw(int amount)
        {
            if (amount <= 0) { return; }
            var cm = CardManager.Instance;
            if (cm == null) { WarnOnce("Draw(no CardManager)"); return; }
            cm.DrawCards(amount);
        }

        public override void DiscardRandom(int amount)
        {
            if (amount <= 0) { return; }
            for (int i = 0; i < amount; i++) { new SubCommandDiscardRandomCard(true); }
        }

        public override void GainMana(ManaColor color, int amount)
        {
            if (amount <= 0) { return; }
            new SubCommandAddManaS(ToColorE(color), amount, isInsert: true);
        }

        public override void SetMaxMana(int amount)
        {
            var mm = ManaManager.Instance;
            if (mm == null) { WarnOnce("SetMaxMana(no ManaManager)"); return; }
            mm.SetMaxManaCount(amount);
        }

        public override void CleanMana()
        {
            // 原版 EffectCleanMana 走 Effects.Special.CardEffect.EffectSpEffectCleanMana，
            // 内部是 SubCommandRemoveMana；这里按"把所有当前能量移除"实现。
            var mm = ManaManager.Instance;
            if (mm == null) { WarnOnce("CleanMana(no ManaManager)"); return; }
            var mana = mm.GetManaArray();
            if (mana != null && mana.Length > 0) { new SubCommandRemoveMana(mana); }
        }

        public override void AddState(string stateName, int amount, CombatantSelector target = CombatantSelector.Self)
        {
            if (string.IsNullOrEmpty(stateName) || amount == 0) { return; }
            foreach (var ch in Resolve(target))
            {
                int cur = ch.GetSpecialStateNumber(stateName);
                ch.SetSpecialStateToCertainNumber(stateName, cur + amount);
            }
        }

        public override void ReduceState(string stateName, int amount, CombatantSelector target = CombatantSelector.Self)
        {
            if (string.IsNullOrEmpty(stateName) || amount == 0) { return; }
            foreach (var ch in Resolve(target))
            {
                int cur = ch.GetSpecialStateNumber(stateName);
                int next = cur - amount;
                ch.SetSpecialStateToCertainNumber(stateName, next < 0 ? 0 : next);
            }
        }

        public override void GiveAbility(string abilityName, int level, CombatantSelector target = CombatantSelector.Self)
        {
            if (string.IsNullOrEmpty(abilityName)) { return; }
            var dm = DataManager.Instance;
            var adm = dm == null ? null : dm.GetAbilityDataManager();
            var so = adm == null ? null : adm.GetAbilitySo(abilityName);
            if (so == null)
            {
                ModKitLog.Error("自定义效果：找不到能力 '" + abilityName + "'（gain_ability 的名字必须是原版能力名）");
                return;
            }
            foreach (var ch in Resolve(target))
            {
                new SubCommandApplyAbility(ch, so.abilityInfo, level <= 0 ? 1 : level);
            }
        }

        public override void GiveCard(string cardId, CardDestination destination, int amount = 1)
        {
            GiveCardCore(cardId, destination, amount, temporary: false);
        }

        public override void GiveTemporaryCard(string cardId, CardDestination destination, int amount = 1)
        {
            GiveCardCore(cardId, destination, amount, temporary: true);
        }

        private void GiveCardCore(string cardId, CardDestination destination, int amount, bool temporary)
        {
            if (string.IsNullOrEmpty(cardId) || amount <= 0) { return; }
            var so = FindCardSo(cardId);
            if (so == null)
            {
                ModKitLog.Error("自定义效果：找不到卡 '" + cardId + "'（give_card / give_temporary_card 的 id 必须存在）");
                return;
            }

            var info = so.CardInfo.CreateDeepCopy();
            if (temporary) { info.AddKeywordToKeywordInfo(new CardKeyWordInfo("Temporary")); }

            CardPlace place;
            switch (destination)
            {
                case CardDestination.Library: place = CardPlace.Library; break;
                case CardDestination.Grave: place = CardPlace.Grave; break;
                default: place = CardPlace.Hand; break;
            }
            new SubCommandAddCardToPlayer(info, place, amount);
            if (place == CardPlace.Library && CardManager.Instance != null) { CardManager.Instance.ShuffleLibrary(); }
        }

        /// <summary>
        /// 按 id 找卡 SO：先按原名查，再在游戏卡字典里找 **以 ".&lt;id&gt;" 结尾** 的键。
        /// </summary>
        /// <remarks>
        /// mod 卡在游戏里的 cardName 带 mod 前缀（如 <c>manual_demo.mf_tadpole_token</c>），
        /// 而作者在 JSON 里写裸 id —— 两种都接受。
        /// <para>
        /// ★ 不能用 <c>null</c> 判断"没找到"：游戏 <c>GetCardSoByName</c> 查不到时返回
        ///   EmptyCard（"空白卡牌"）而不是 null。判 null 会让这里的"按后缀找 mod 卡"
        ///   永远不执行，把空白卡当结果发放（症状：give_card 给的是一张空白卡）。
        /// </para>
        /// </remarks>
        private static CardSo FindCardSo(string id)
        {
            var cdm = CardDataManager.Instance;
            if (cdm == null) { return null; }

            var direct = cdm.GetCardSoByName(id);
            if (IsNamedCard(direct, id)) { return direct; }

            try
            {
                var f = cdm.GetType().GetField("_allInGameCardSoDictionary",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                var dict = f == null ? null : f.GetValue(cdm) as System.Collections.IDictionary;
                if (dict == null) { return null; }

                var suffix = "." + id;
                foreach (System.Collections.DictionaryEntry e in dict)
                {
                    var key = e.Key as string;
                    var card = e.Value as CardSo;
                    if (card == null || key == null) { continue; }
                    if (key == id || key.EndsWith(suffix, StringComparison.Ordinal)) { return card; }
                }
            }
            catch { }
            return null;
        }

        /// <summary>按名查到的卡是否真的是这张（空白卡的 cardName 也非空，所以必须比名字）。</summary>
        private static bool IsNamedCard(CardSo so, string expectedName)
        {
            return so != null
                && !string.IsNullOrEmpty(so.cardName)
                && so.cardName != "EmptyCard"
                && string.Equals(so.cardName, expectedName, StringComparison.Ordinal);
        }

        public override void UpgradeCard(ICardRef card) { WarnOnce("UpgradeCard"); }
        public override void EndTurn() { WarnOnce("EndTurn"); }

        public override void SubscribeTrigger(string triggerName, Action<TriggerEvent> handler)
        { WarnOnce("SubscribeTrigger"); }

        // ---------------- 内部 ----------------

        private Character FirstOpponent()
        {
            var arr = Resolve(CombatantSelector.Opponent);
            return arr.Count > 0 ? arr[0] : null;
        }

        private List<Character> Resolve(CombatantSelector selector)
        {
            var list = new List<Character>();
            var owner = OwnerCharacter;
            try
            {
                if (selector == CombatantSelector.Self)
                {
                    if (owner != null) { list.Add(owner); }
                    return list;
                }
                if (owner == null) { return list; }

                // 选择器 → 游戏的 EffectTargetEnum（群攻/全体/随机都用游戏自己的解析）
                EffectTargetEnum targetEnum;
                switch (selector)
                {
                    case CombatantSelector.AllOpponents: targetEnum = EffectTargetEnum.AllOpponents; break;
                    case CombatantSelector.All: targetEnum = EffectTargetEnum.All; break;
                    case CombatantSelector.RandomOpponent: targetEnum = EffectTargetEnum.RandomOpponent; break;
                    default: targetEnum = EffectTargetEnum.TargetOpponent; break;
                }
                var arr = owner.GetDefaultCharactersByTarget(targetEnum);
                if (arr != null) { foreach (var c in arr) { if (c != null) { list.Add(c); } } }
            }
            catch (Exception ex) { WarnOnce("Resolve(" + selector + "): " + ex.GetType().Name); }
            return list;
        }

        private static ICombatant Wrap(Character c) { return c == null ? null : new GameCombatant(c); }

        private static ColorE ToColorE(ManaColor color)
        {
            switch (color)
            {
                case ManaColor.Red: return ColorE.Red;
                case ManaColor.Green: return ColorE.Green;
                case ManaColor.Blue: return ColorE.Blue;
                case ManaColor.Purple: return ColorE.Purple;
                case ManaColor.Black: return ColorE.Black;
                default: return ColorE.Void;
            }
        }

        private void WarnOnce(string member)
        {
            if (_warned.Add(member))
            {
                ModKitLog.Warn("自定义效果的上下文成员未实现/失败：" + member);
            }
        }
    }

    /// <summary>`ModKit.Api.ICombatant` 在游戏侧的实现类（包住 <see cref="Character"/>）。</summary>
    internal sealed class GameCombatant : ICombatant
    {
        private static readonly System.Reflection.PropertyInfo MaxHpProperty =
            typeof(Character).GetProperty("MaxHealthPoint",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

        private readonly Character _c;

        public GameCombatant(Character c) { _c = c; }

        public string Name { get { return _c == null ? "?" : _c.name; } }
        public int Health { get { return _c == null ? 0 : _c.GetHealthPoint(); } }

        public int MaxHealth
        {
            get
            {
                if (_c == null || MaxHpProperty == null) { return 0; }
                try { return (int)MaxHpProperty.GetValue(_c, null); } catch { return 0; }
            }
        }

        public bool IsDead { get { return _c == null || Health <= 0; } }

        public int GetState(string stateName)
        {
            if (_c == null || string.IsNullOrEmpty(stateName)) { return 0; }
            try { return _c.GetSpecialStateNumber(stateName); } catch { return 0; }
        }

        public IReadOnlyList<string> GetStateNames() { return new List<string>(); }
    }

    /// <summary>`ModKit.Api.ICardRef` 在游戏侧的实现类（包住 <see cref="CardBase"/>）。</summary>
    internal sealed class GameCardRef : ICardRef
    {
        private readonly CardBase _card;
        public GameCardRef(CardBase card) { _card = card; }

        public string Id
        {
            get
            {
                try { return _card == null ? "" : _card.CardName; } catch { return ""; }
            }
        }

        public string Name
        {
            get
            {
                try { return _card == null ? "" : _card.CardName; } catch { return ""; }
            }
        }

        public bool IsUpgraded
        {
            get
            {
                try { return _card != null && _card.cardUpgradeInfo.isUpgraded; } catch { return false; }
            }
        }
    }

    /// <summary>
    /// 对齐"效果数字槽数量"与生成效果时传入的 <c>RNumber[]</c>。
    /// </summary>
    /// <remarks>
    /// ★ 游戏 `EffectUtils.GenerateEffectFromEffectInfo` 里有
    /// <c>if (numbers.Length == count)</c> 才赋值；不等时 rNumbers 留空
    /// ⇒ 描述模板里的 <c>{Number}</c> 全部取不到值 ⇒ SmartFormat 弹框。
    /// 自定义效果（SpecialEffect 家族）的"应有多少个数字"没有登记处，
    /// 因此这里按 **effectInfo.effectNumberInfos.Count** 补齐/截断，
    /// 补齐值沿用最后一个数字（与作者在 amounts 里写的顺序一致）。
    /// </remarks>
    [HarmonyPatch(typeof(EffectUtils), "GenerateEffectFromEffectInfo")]
    internal static class EffectNumberCountGuard
    {
        private static void Prefix(ref RNumber[] numbers, EffectInfo effectInfo)
        {
            try
            {
                int want = effectInfo == null || effectInfo.effectNumberInfos == null
                    ? 0 : effectInfo.effectNumberInfos.Count;
                if (numbers == null)
                {
                    numbers = new RNumber[want];
                    return;
                }
                if (numbers.Length == want) { return; }

                var fixedArr = new RNumber[want];
                int copy = numbers.Length < want ? numbers.Length : want;
                for (int i = 0; i < copy; i++) { fixedArr[i] = numbers[i]; }
                for (int i = copy; i < want; i++)
                {
                    fixedArr[i] = numbers.Length > 0 ? numbers[numbers.Length - 1] : null;
                }
                numbers = fixedArr;
            }
            catch { }
        }
    }

}
