using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Os 5 slots do GDD-021. Mãos guardam armas (ou escudo na esquerda); o resto é armadura.</summary>
    public enum ItemSlot { Head, Torso, Legs, RightHand, LeftHand }

    public enum Rarity { Common, Rare, Epic }

    /// <summary>Efeito especial dos itens épicos (GDD-021 §5.1).</summary>
    public enum ItemEffect { None, FireResist, Dry, SharpCounter, LightFeet, Conduit }

    /// <summary>Armadura ou escudo (as armas continuam em WeaponDef, com forma e dano do GDD-020).</summary>
    public class ItemDef
    {
        public string id, displayName;
        public ItemSlot slot;
        public bool isShield;
        public int weight;
        public Rarity rarity;
        public int physDef, magDef;
        public Attr attr; public int attrBonus;
        public ItemEffect effect;
        public int shieldHardness;

        public string Icon => isShield ? "shield" : slot == ItemSlot.Head ? "helm" : slot == ItemSlot.Torso ? "chest" : "boots";

        /// <summary>Uma linha curta com o que o item dá (cartas, ficha, criação).</summary>
        public string Summary()
        {
            var p = new List<string>();
            if (isShield) p.Add($"Bloquear +{shieldHardness}");
            if (physDef > 0) p.Add($"defesa {physDef}");
            if (magDef > 0) p.Add($"defesa mágica {magDef}");
            if (attrBonus > 0) p.Add($"+{attrBonus} {attr}");
            if (effect != ItemEffect.None) p.Add(Equipment.EffectName(effect));
            p.Add(weight == 0 ? "leve" : $"peso {weight}");
            return string.Join(" · ", p);
        }
    }

    public static class ItemCatalog
    {
        static ItemDef A(string id, string name, ItemSlot s, int w, Rarity r, int pd, int md, Attr at = Attr.STR, int ab = 0, ItemEffect fx = ItemEffect.None) =>
            new ItemDef { id = id, displayName = name, slot = s, weight = w, rarity = r, physDef = pd, magDef = md, attr = at, attrBonus = ab, effect = fx };
        static ItemDef S(string id, string name, int w, Rarity r, int hard, ItemEffect fx = ItemEffect.None) =>
            new ItemDef { id = id, displayName = name, slot = ItemSlot.LeftHand, isShield = true, weight = w, rarity = r, shieldHardness = hard, effect = fx };

        // GDD-021 §2.3 e §3.1 (números [PROPOSTA]).
        public static readonly ItemDef[] All =
        {
            A("ClothHood",   "Capuz de Pano",  ItemSlot.Head, 0, Rarity.Common, 0, 1),
            A("LeatherHelm", "Elmo de Couro",  ItemSlot.Head, 1, Rarity.Common, 1, 0),
            A("IronHelm",    "Elmo de Ferro",  ItemSlot.Head, 2, Rarity.Rare,   2, 0, Attr.VIT, 1),
            A("SageHat",     "Chapéu do Sábio",ItemSlot.Head, 0, Rarity.Rare,   0, 2, Attr.WIS, 1),
            A("EmberCrown",  "Coroa de Brasa", ItemSlot.Head, 1, Rarity.Epic,   1, 2, Attr.INT, 1, ItemEffect.FireResist),

            A("Tunic",       "Túnica",         ItemSlot.Torso, 0, Rarity.Common, 0, 1),
            A("LeatherVest", "Gibão de Couro", ItemSlot.Torso, 1, Rarity.Common, 2, 0),
            A("ChainMail",   "Cota de Malha",  ItemSlot.Torso, 3, Rarity.Rare,   4, 1, Attr.VIT, 1),
            A("ArcaneMantle","Manto Arcano",   ItemSlot.Torso, 1, Rarity.Rare,   0, 3, Attr.INT, 1),
            A("TideMantle",  "Manto da Maré",  ItemSlot.Torso, 1, Rarity.Epic,   1, 3, Attr.WIS, 1, ItemEffect.Dry),
            A("ConduitRobe", "Veste Conduíte", ItemSlot.Torso, 1, Rarity.Epic,   1, 2, Attr.INT, 2, ItemEffect.Conduit),

            A("Sandals",     "Sandálias",      ItemSlot.Legs, 0, Rarity.Common, 0, 0),
            A("LeatherBoots","Botas de Couro", ItemSlot.Legs, 1, Rarity.Common, 1, 0),
            A("SwiftBoots",  "Botas Ágeis",    ItemSlot.Legs, 0, Rarity.Rare,   0, 0, Attr.AGI, 1),
            A("IronGreaves", "Grevas de Ferro",ItemSlot.Legs, 2, Rarity.Rare,   2, 0, Attr.VIT, 1),
            A("WindStriders","Passos do Vento",ItemSlot.Legs, 0, Rarity.Epic,   1, 0, Attr.AGI, 1, ItemEffect.LightFeet),

            S("Buckler",      "Broquel",            1, Rarity.Common, 2),
            S("WoodenShield", "Escudo de Madeira",  2, Rarity.Common, 3),
            S("IronShield",   "Escudo de Ferro",    3, Rarity.Rare,   5),
            S("SpikedShield", "Escudo de Cravos",   2, Rarity.Epic,   4, ItemEffect.SharpCounter),
        };

        public static ItemDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var i in All) if (i.id == id) return i;
            return null;
        }

        public static List<ItemDef> ForSlot(ItemSlot s, Rarity? only = null)
        {
            var l = new List<ItemDef>();
            foreach (var i in All) if (i.slot == s && (only == null || i.rarity == only.Value)) l.Add(i);
            return l;
        }
    }

    /// <summary>
    /// Regras de equipamento (GDD-021). Fonte única: batalha (Unit), criação e ficha usam ApplyTo.
    /// </summary>
    public static class Equipment
    {
        static readonly HashSet<string> TwoHandedIds = new HashSet<string> { "IronAxe", "WoodenStaff", "ArcaneStaff", "ShortBow", "LongBow" };

        /// <summary>Arma de duas mãos (arcos, cajados, machado grande).</summary>
        public static bool IsTwoHanded(WeaponDef w) => w != null && TwoHandedIds.Contains(w.id);
        public static bool IsTwoHanded(string weaponId) => TwoHandedIds.Contains(weaponId ?? "");

        /// <summary>Peso de uma arma: 2 mãos pesa 2, 1 mão pesa 1.</summary>
        public static int WeaponWeight(WeaponDef w) => w == null ? 0 : IsTwoHanded(w) ? 2 : 1;

        /// <summary>A mão esquerda guarda uma arma de 1 mão, um escudo, ou nada.</summary>
        public static WeaponDef OffhandWeapon(string offhandId)
        {
            var w = WeaponCatalog.Get(offhandId);
            return w != null && !IsTwoHanded(w) ? w : null;
        }

        public static string EffectName(ItemEffect e) => e switch
        {
            ItemEffect.FireResist   => "resistente a fogo",
            ItemEffect.Dry          => "imune a Encharcado",
            ItemEffect.SharpCounter => "contra-ataque +30%",
            ItemEffect.LightFeet    => "Esquivar anda +1",
            ItemEffect.Conduit      => "+1 alcance de magia",
            _ => "",
        };

        public static string RarityName(Rarity r) => r == Rarity.Epic ? "Épico" : r == Rarity.Rare ? "Raro" : "Comum";
        public static Color RarityColor(Rarity r) => r == Rarity.Epic ? new Color(0.70f, 0.45f, 1f) : r == Rarity.Rare ? new Color(0.35f, 0.62f, 1f) : new Color(0.62f, 0.66f, 0.74f);

        /// <summary>Os ids dos 5 slots (cabeça, torso, pernas, mão direita, mão esquerda).</summary>
        public struct Loadout
        {
            public string head, torso, legs, right, left;
            public Loadout(string head, string torso, string legs, string right, string left)
            { this.head = head; this.torso = torso; this.legs = legs; this.right = right; this.left = left; }
            public string this[ItemSlot s] => s switch
            {
                ItemSlot.Head => head, ItemSlot.Torso => torso, ItemSlot.Legs => legs, ItemSlot.RightHand => right, _ => left,
            };
            public IEnumerable<ItemDef> Armor()
            {
                foreach (var id in new[] { head, torso, legs, left })
                {
                    var it = ItemCatalog.Get(id);
                    if (it != null) yield return it;
                }
            }
        }

        public static Loadout Of(Unit u) => new Loadout(u.headId, u.torsoId, u.legsId, u.weaponId, u.offhandId);
        public static Loadout Of(CharacterPreset p) => new Loadout(p.headId, p.torsoId, p.legsId, p.weaponId, p.offhandId);

        public static int TotalWeight(Loadout l)
        {
            int w = WeaponWeight(WeaponCatalog.Get(l.right)) + WeaponWeight(OffhandWeapon(l.left));
            foreach (var it in l.Armor()) w += it.weight;
            return w;
        }

        /// <summary>Peso que se carrega sem penalidade: 2 + VIT/2 (GDD-021 §4).</summary>
        public static int WeightLimit(float vit) => 2 + Mathf.FloorToInt(vit / 2f);

        public static bool HasEffect(Unit u, ItemEffect e)
        {
            if (u == null) return false;
            foreach (var it in Of(u).Armor()) if (it.effect == e) return true;
            return false;
        }

        /// <summary>
        /// Aplica a mão direita (dano, forma, alcance, com o corte de 1 mão), a armadura (defesa,
        /// peso) e os bônus de atributo dos itens. <paramref name="appliedBonus"/> guarda o que já
        /// foi somado aos atributos, para trocar de item sem acumular (descidas do Coop).
        /// </summary>
        public static void ApplyTo(AttributeStats stats, Loadout l, GameTuning tuning, int[] appliedBonus = null)
        {
            if (stats == null) return;
            var w = WeaponCatalog.Get(l.right);
            WeaponDef.ApplyTo(stats, w, tuning);
            // Arma de uma mão dá menos dano (GDD-021 §2.1).
            if (w != null && !IsTwoHanded(w) && w.id != "Claws" && w.id != "ClawsLarge")
                stats.WeaponDamage = Mathf.Max(1, Mathf.RoundToInt(w.damage * (tuning != null ? tuning.oneHandDamageMult : 0.8f)));

            // Bônus de atributo: tira o que já estava, soma o novo.
            var bonus = new int[6];
            foreach (var it in l.Armor()) if (it.attrBonus != 0) bonus[(int)it.attr] += it.attrBonus;
            for (int i = 0; i < 6; i++)
            {
                int delta = bonus[i] - (appliedBonus != null ? appliedBonus[i] : 0);
                if (delta != 0) AddAttr(stats, (Attr)i, delta);
                if (appliedBonus != null) appliedBonus[i] = bonus[i];
            }

            int pd = 0, md = 0;
            foreach (var it in l.Armor()) { pd += it.physDef; md += it.magDef; }
            stats.ArmorPhys = pd; stats.ArmorMag = md;
            stats.OverWeight = Mathf.Max(0, TotalWeight(l) - WeightLimit(stats.VIT));
        }

        static void AddAttr(AttributeStats s, Attr a, float d)
        {
            switch (a)
            {
                case Attr.STR: s.STR += d; break; case Attr.VIT: s.VIT += d; break; case Attr.DEX: s.DEX += d; break;
                case Attr.AGI: s.AGI += d; break; case Attr.INT: s.INT += d; break; default: s.WIS += d; break;
            }
        }

        /// <summary>Personagem sem armadura nenhuma (salvo antes do GDD-021) ganha o kit da arma.</summary>
        public static void EnsureKit(CharacterPreset p)
        {
            if (p == null) return;
            if (!string.IsNullOrEmpty(p.headId) || !string.IsNullOrEmpty(p.torsoId) || !string.IsNullOrEmpty(p.legsId) || !string.IsNullOrEmpty(p.offhandId)) return;
            var k = StarterKit(p.weaponId);
            p.headId = k.head; p.torsoId = k.torso; p.legsId = k.legs; p.offhandId = k.left;
        }

        /// <summary>Kit comum sugerido para a arma escolhida (GDD-021 §6.2).</summary>
        public static Loadout StarterKit(string weaponId)
        {
            switch (weaponId)
            {
                case "LongBow": case "ShortBow":   return new Loadout("ClothHood", "LeatherVest", "LeatherBoots", weaponId, "");
                case "WoodenStaff": case "ArcaneStaff": case "Scepter": case "ApprenticeWand":
                                                   return new Loadout("ClothHood", "Tunic", "Sandals", weaponId, "");
                case "Hatchet":                    return new Loadout("LeatherHelm", "LeatherVest", "LeatherBoots", weaponId, "Hatchet");
                case "IronAxe":                    return new Loadout("LeatherHelm", "LeatherVest", "LeatherBoots", weaponId, "");
                default:                           return new Loadout("LeatherHelm", "LeatherVest", "LeatherBoots", weaponId, "WoodenShield");
            }
        }

        // ── Cartas da descida (GDD-021 §7) ──
        public class Card { public ItemDef item; public WeaponDef weapon; public Attr attr; public int points; public bool IsAttr => item == null && weapon == null; }

        /// <summary>
        /// 3 cartas para a unidade na camada: sorteio igual em todas as máquinas (semente = camada e id).
        /// Item de qualquer slot (raro/épico ficam mais comuns lá embaixo) ou pontos de atributo.
        /// </summary>
        public static List<Card> DrawCards(int layer, uint unitId, Unit u)
        {
            var rng = new System.Random(unchecked(layer * 7919 + (int)unitId * 104729 + 17));
            float rareP = layer <= 2 ? 0.30f : layer <= 4 ? 0.55f : 0.60f;
            float epicP = layer <= 2 ? 0f : layer <= 4 ? 0.10f : 0.30f;
            int pts = layer <= 2 ? 2 : layer <= 4 ? 3 : 4;
            var cards = new List<Card>();
            var used = new HashSet<string>();
            int guard = 0;
            while (cards.Count < 3 && guard++ < 50)
            {
                if (rng.NextDouble() < 0.3)
                {
                    var a = (Attr)rng.Next(6);
                    if (used.Add("attr" + a)) cards.Add(new Card { attr = a, points = pts });
                    continue;
                }
                double roll = rng.NextDouble();
                var rar = roll < epicP ? Rarity.Epic : roll < epicP + rareP ? Rarity.Rare : Rarity.Common;
                var pool = new List<ItemDef>();
                foreach (var it in ItemCatalog.All)
                {
                    if (it.rarity != rar) continue;
                    if (it.isShield && u != null && IsTwoHanded(u.weaponId)) continue;   // escudo não cabe com 2 mãos
                    if (u != null && Of(u)[it.slot] == it.id) continue;                    // já tem
                    pool.Add(it);
                }
                if (pool.Count == 0) continue;
                var pick = pool[rng.Next(pool.Count)];
                if (used.Add(pick.id)) cards.Add(new Card { item = pick });
            }
            return cards;
        }

        public static string CardText(Card c) => c.IsAttr
            ? $"<b>+{c.points} {c.attr}</b>\n<size=12><color=#9fb3d9>pontos de atributo</color></size>"
            : $"<b>{c.item.displayName}</b>  <size=12><color=#{ColorUtility.ToHtmlStringRGB(RarityColor(c.item.rarity))}>{RarityName(c.item.rarity)} · {SlotName(c.item.slot, c.item.isShield)}</color></size>\n<size=12><color=#9fb3d9>{c.item.Summary()}</color></size>";

        public static string SlotName(ItemSlot s, bool shield = false) => shield ? "escudo" : s switch
        {
            ItemSlot.Head => "cabeça", ItemSlot.Torso => "torso", ItemSlot.Legs => "pernas",
            ItemSlot.RightHand => "mão direita", _ => "mão esquerda",
        };

        /// <summary>Aplica a carta escolhida na unidade (item troca o do slot, o antigo some).</summary>
        public static void ApplyCard(Unit u, Card c)
        {
            if (u == null || c == null) return;
            if (c.IsAttr) { AddAttr(u.stats, c.attr, c.points); }
            else
            {
                switch (c.item.slot)
                {
                    case ItemSlot.Head: u.headId = c.item.id; break;
                    case ItemSlot.Torso: u.torsoId = c.item.id; break;
                    case ItemSlot.Legs: u.legsId = c.item.id; break;
                    default: u.offhandId = c.item.id; break;
                }
            }
            u.ReapplyEquipment();
        }
    }
}
