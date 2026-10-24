# -*- coding: utf-8 -*-
"""卡组定位表：按「定位 / 段数 / 是否吃乘区」列卡，**不打每费分**。

为什么不打分（2026-10-24 修订）：
  本作的伤害管线是「每段 flat 加值 → 检定翻倍/减半」，等于 `段数 × 乘区`，
  而防御卡、真伤卡、引擎卡根本不在这条管线里。把它们折成同一个"分"相加
  会得到错误结论（例如把易伤当伤害、把多段卡当成单段卡、把真伤卡当成普通 carry）。
  详见 docs/奶蛙卡组-强度平衡.md。

用法：
    python tools/balance_audit.py                      # 默认审计 examples/naifrog
    python tools/balance_audit.py --mod <mod目录>
    python tools/balance_audit.py --mod <dir> --out audit.txt

输出列：
    费     费用槽数（`["Green", ["Green","Black"]]` = 2 槽，中括号表示任选其一）
    段数   自身伤害段数（每张 deal_damage/lose_life 效果算一段）
    连锁   milk_frog 条数 = 额外结算实例数
    乘区   ✓ 吃力量/脆皮/暴击/易伤 ｜ ✗ 真伤不吃 ｜ ~ 只有延后伤害（流血）
    定位   多为组合标签，一张卡可能同时是「carry/乘区供给」
"""
import argparse
import glob
import io
import json
import os
import sys

# 造成"真伤"的内置语义（不进伤害管线，不受力量/易伤/暴击影响）
TRUE_DAMAGE = {"lose_life"}
# 直接伤害
DIRECT_DAMAGE = {"deal_damage"}
# 防御向能力
DEFENSE_ABILITIES = {"Tough", "Evade", "Weak", "DicePenalty"}
# 乘区/增益向能力
BUFF_ABILITIES = {"Power", "Excitement", "Vulnerable", "CriticalHit", "ThinSkin"}
# 资源/引擎向
ENGINE_BUILTINS = {"draw", "gain_mana", "seek_card", "improvise_card"}
ENGINE_KEYWORDS = {"Kick", "milk_frog", "Summon"}


def keywords_of(card):
    out = {}
    for k in card.get("keywords") or []:
        if isinstance(k, str):
            out[k] = out.get(k, 0) + 1
        else:
            out[k.get("name")] = k.get("number", 1)
    return out


def slots(cost):
    return len(cost or [])


def analyse(card):
    hits = 0
    true_hits = 0
    values = []
    tags = set()
    notes = []
    for e in card.get("effects") or []:
        n = (e.get("amounts") or [0])
        n = n[0] if n else 0
        builtin = e.get("builtin")
        custom = e.get("custom")
        if builtin in DIRECT_DAMAGE:
            hits += 1
            values.append("伤%d" % n)
            tags.add("carry")
            if e.get("target") in ("AllOpponents", "Opponents"):
                notes.append("群体")
        elif builtin in TRUE_DAMAGE:
            hits += 1
            true_hits += 1
            values.append("真伤%d" % n)
            tags.add("真伤")
        elif builtin == "block":
            values.append("格挡%d" % n)
            tags.add("防御")
        elif builtin == "gain_ability":
            name = e.get("name", "")
            values.append("%s%d" % (name, n))
            if name in DEFENSE_ABILITIES:
                tags.add("防御")
            elif name in BUFF_ABILITIES:
                tags.add("乘区供给")
            elif name in ("Bleed",):
                tags.add("防御/延后伤害")
            elif name == "milkfrog_choice":
                tags.add("引擎")
                notes.append("干预奶蛙结算顺序")
            elif name == "Tough":
                tags.add("防御")
        elif builtin in ENGINE_BUILTINS:
            values.append("%s%d" % (builtin, n))
            tags.add("引擎")
        elif custom:
            if custom == "mf_glare_cascade":
                values.append("被连锁·易伤%d" % n)
                tags.add("乘区供给")
            elif custom == "mf_swarm_surge_cascade":
                values.append("被连锁·群伤%d" % n)
                tags.add("carry")
            elif custom == "mf_bleed_if_cascade":
                values.append("被连锁·流血%d" % n)
                tags.add("防御/延后伤害")
            elif custom == "mf_bellow":
                amt = e.get("amounts") or []
                values.append("临时力量%d" % (amt[0] if amt else 0))
                tags.add("乘区供给")
                if len(amt) > 1:
                    values.append("被连锁·力量%d" % amt[1])
            elif custom == "mf_choir_tenacity":
                values.append("坚韧滚雪球+%d" % n)
                tags.add("防御")
            elif custom == "mf_grave_swell":
                hits += 1
                values.append("弃牌堆×%d" % n)
                tags.add("carry")
            elif custom == "mf_choose_discard":
                values.append("弃%d" % n)
                tags.add("引擎")
            else:
                values.append(custom)
    kws = keywords_of(card)
    cascade = kws.get("milk_frog", 0)
    if cascade:
        tags.add("段数供给")
    if "Kick" in kws:
        notes.append("增幅%d=检定加成" % kws["Kick"])
        tags.add("乘区供给")
    if "Summon" in kws:
        tags.add("段数供给")
        notes.append("召唤")
    if true_hits and not (hits - true_hits):
        scale = "✗ 真伤不吃乘区"
    elif hits:
        scale = "✓"
    elif any("Bleed" in v for v in values):
        scale = "~ 延后结算"
    else:
        scale = "—"
    return hits, cascade, scale, tags, values, notes, kws


def sheet(mod_dir):
    files = sorted(glob.glob(os.path.join(mod_dir, "cards", "*.json")))
    if not files:
        sys.exit("没有找到 %s/cards/*.json" % mod_dir)
    rows = []
    for path in files:
        with io.open(path, encoding="utf-8-sig") as fh:
            card = json.load(fh)
        if str(card.get("id", "")).startswith("mf_essence_"):
            continue                        # 衍生物不进卡池
        hits, cascade, scale, tags, values, notes, kws = analyse(card)
        rows.append(dict(id=card["id"], name=(card.get("name") or {}).get("zh-Hans", ""),
                         cost=slots(card.get("cost")), rarity=card.get("rarity"),
                         hits=hits, cascade=cascade, scale=scale,
                         tags="/".join(sorted(tags)) or "—",
                         values="+".join(values), notes="/".join(notes)))
    rows.sort(key=lambda r: (r["cost"], -r["hits"], -r["cascade"]))
    lines = ["# 卡组定位表 —— %s" % os.path.abspath(mod_dir), ""]
    lines.append("%-16s %-12s %2s %-8s %-14s %4s %4s  %-9s %s" % (
        "id", "名字", "费", "稀有", "定位", "段数", "连锁", "乘区", "效果/备注"))
    for r in rows:
        lines.append("%-16s %-12s %2d %-8s %-14s %4d %4d  %-9s %s%s" % (
            r["id"], r["name"], r["cost"], r["rarity"], r["tags"],
            r["hits"], r["cascade"], r["scale"], r["values"],
            (" | " + r["notes"]) if r["notes"] else ""))
    lines.append("")
    lines.append("段数 = 自身伤害段数；连锁 = 额外结算实例数（milk_frog 条数）。")
    lines.append("段数是本作的主乘区：flat 加值（力量/脆皮）按段结算，检定翻倍也按段判定。")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="卡组定位表（不打每费分）")
    default_mod = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                               "examples", "naifrog")
    parser.add_argument("--mod", default=default_mod, help="mod 目录（含 cards/*.json）")
    parser.add_argument("--out", help="把结果写成 UTF-8 文本文件")
    args = parser.parse_args()
    text = sheet(args.mod)
    if args.out:
        io.open(args.out, "w", encoding="utf-8").write(text + "\n")
        print("已写入 %s" % args.out)
    else:
        try:
            print(text)
        except UnicodeEncodeError:
            print(text.encode("utf-8", "replace").decode("utf-8", "replace"))


if __name__ == "__main__":
    main()
