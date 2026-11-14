using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Loot do Coop (GDD-024 etapa B): drops das feras, baú no fim da camada e o baú do grupo,
    /// onde cada um pega (troca com o que tinha), vende ou deixa itens. Itens valem durante a
    /// descida, como as cartas (GDD-021 §7). Regras puras e determinísticas; a rede fica no PlacementSync.
    /// </summary>
    public static class Loot
    {
        public const float BeastDropChance = 0.08f;

        public static int SellPrice(Rarity r) => r == Rarity.Epic ? 25 : r == Rarity.Rare ? 12 : 5;
        public static int SellPrice(ItemDef it) => it == null ? 0 : SellPrice(it.rarity);

        /// <summary>Raridade pela profundidade (mesma curva das cartas).</summary>
        public static Rarity RollRarity(System.Random rng, int layer)
        {
            float rareP = layer <= 2 ? 0.30f : layer <= 4 ? 0.55f : 0.60f;
            float epicP = layer <= 2 ? 0f : layer <= 4 ? 0.10f : 0.30f;
            double roll = rng.NextDouble();
            return roll < epicP ? Rarity.Epic : roll < epicP + rareP ? Rarity.Rare : Rarity.Common;
        }

        public static ItemDef RollItem(System.Random rng, int layer)
        {
            var rar = RollRarity(rng, layer);
            var pool = new List<ItemDef>();
            foreach (var it in ItemCatalog.All) if (it.rarity == rar) pool.Add(it);
            if (pool.Count == 0) foreach (var it in ItemCatalog.All) pool.Add(it);
            return pool[rng.Next(pool.Count)];
        }

        /// <summary>Itens da camada vencida: cada fera abatida tem BeastDropChance de deixar um item;
        /// o baú traz 1 por personagem + 1. Mesma semente → mesmos itens.</summary>
        public static List<string> Generate(int layer, int beastsKilled, int players, int seed)
        {
            var rng = new System.Random(unchecked(seed * 31 + layer * 7919 + 101));
            var items = new List<string>();
            for (int i = 0; i < beastsKilled; i++)
                if (rng.NextDouble() < BeastDropChance) items.Add(RollItem(rng, layer).id);
            int chest = Mathf.Max(1, players) + 1;
            for (int i = 0; i < chest; i++) items.Add(RollItem(rng, layer).id);
            return items;
        }

        /// <summary>O item cabe nesta unidade? (escudo não cabe com arma de 2 mãos)</summary>
        public static bool CanEquip(Unit u, ItemDef it, out string why)
        {
            why = "";
            if (u == null || it == null) { why = "item sumiu"; return false; }
            if (it.isShield && Equipment.IsTwoHanded(u.weaponId)) { why = "arma de 2 mãos não deixa usar escudo"; return false; }
            return true;
        }

        private static string Get(Unit u, ItemSlot s) => s switch
        {
            ItemSlot.Head => u.headId, ItemSlot.Torso => u.torsoId, ItemSlot.Legs => u.legsId,
            ItemSlot.LeftHand => u.offhandId, _ => u.weaponId
        };

        private static void Set(Unit u, ItemSlot s, string id)
        {
            switch (s)
            {
                case ItemSlot.Head: u.headId = id; break;
                case ItemSlot.Torso: u.torsoId = id; break;
                case ItemSlot.Legs: u.legsId = id; break;
                case ItemSlot.LeftHand: u.offhandId = id; break;
            }
        }

        /// <summary>O que a unidade usa no slot do item (id vazio = nada).</summary>
        public static string Current(Unit u, ItemSlot s) => u == null ? "" : Get(u, s) ?? "";

        /// <summary>Equipa o item do baú; o que a unidade usava no slot volta para o baú (a troca do grupo).</summary>
        public static bool Take(Unit u, List<string> stash, int index)
        {
            if (stash == null || index < 0 || index >= stash.Count) return false;
            var it = ItemCatalog.Get(stash[index]);
            if (!CanEquip(u, it, out _)) return false;
            string old = Current(u, it.slot);
            stash.RemoveAt(index);
            Set(u, it.slot, it.id);
            // Só volta para o baú o que é item do catálogo (uma arma de 1 mão na mão esquerda some, como nas cartas).
            if (ItemCatalog.Get(old) != null) stash.Add(old);
            u.ReapplyEquipment();
            return true;
        }

        /// <summary>Tira o item da unidade e deixa no baú para outro pegar.</summary>
        public static bool Leave(Unit u, ItemSlot s, List<string> stash)
        {
            if (u == null || stash == null || s == ItemSlot.RightHand) return false;
            string cur = Current(u, s);
            if (ItemCatalog.Get(cur) == null) return false;
            Set(u, s, "");
            stash.Add(cur);
            u.ReapplyEquipment();
            return true;
        }

        /// <summary>Vende o item do baú: sai do baú e devolve o preço.</summary>
        public static int Sell(List<string> stash, int index)
        {
            if (stash == null || index < 0 || index >= stash.Count) return 0;
            int price = SellPrice(ItemCatalog.Get(stash[index]));
            stash.RemoveAt(index);
            return price;
        }

        public static string Describe(ItemDef it) =>
            it == null ? "—" : $"{it.displayName} <size=12><color=#{ColorUtility.ToHtmlStringRGB(Equipment.RarityColor(it.rarity))}>{Equipment.RarityName(it.rarity)} · {Equipment.SlotName(it.slot, it.isShield)}</color></size>";
    }
}
