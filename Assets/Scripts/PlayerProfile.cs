using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    [Serializable]
    public class PlayerProfileData
    {
        public string playerName = "";
        public List<CharacterPreset> characters = new List<CharacterPreset>();
        public int selected = 0;
        /// <summary>Dinheiro do jogador (GDD-024): feras abatidas e itens vendidos; a loja troca por XP.</summary>
        public int gold = 0;
    }

    /// <summary>
    /// Identidade do jogador: nome e personagens salvos, definidos ANTES de entrar no Coop ou no
    /// PvP. A sala não cria personagem: cada jogador entra com um dos seus e escolhe qual usar.
    /// Fica em PlayerPrefs (no navegador vai para o IndexedDB e sobrevive a fechar a aba).
    /// O orçamento de atributos é fixo, então um personagem salvo vale em qualquer sala.
    /// </summary>
    public static class PlayerProfile
    {
        /// <summary>Pontos de um personagem novo (GDD-024: eram 30). Cada nível soma PointsPerLevel.</summary>
        public const int AttributeBudget = 15;
        public const int PointsPerLevel = 5;
        /// <summary>XP para passar do nível N: XpPerLevel × N (linear; recompensas crescem com a camada).</summary>
        public const int XpPerLevel = 20;
        /// <summary>Loja: moedas por 1 de XP.</summary>
        public const int GoldPerXp = 2;

        public static int Level(CharacterPreset p) => p == null ? 1 : Mathf.Max(1, p.level);
        /// <summary>Pontos que este personagem tem para distribuir no nível dele.</summary>
        public static int BudgetFor(CharacterPreset p) => AttributeBudget + PointsPerLevel * (Level(p) - 1);
        public static int XpToNext(int level) => XpPerLevel * Mathf.Max(1, level);

        /// <summary>Soma XP e sobe de nível quantas vezes couber. Devolve quantos níveis subiu.</summary>
        public static int AddXp(CharacterPreset p, int amount)
        {
            if (p == null || amount <= 0) return 0;
            p.level = Level(p);
            p.xp += amount;
            int ups = 0;
            while (p.xp >= XpToNext(p.level)) { p.xp -= XpToNext(p.level); p.level++; ups++; }
            return ups;
        }

        public static int Gold => Data.gold;
        public static void AddGold(int amount) { Data.gold = Mathf.Max(0, Data.gold + amount); }

        /// <summary>Loja: gasta moedas e dá XP ao personagem (GoldPerXp moedas por 1 XP). Devolve os níveis ganhos.</summary>
        public static int BuyXp(CharacterPreset p, int xp)
        {
            int cost = xp * GoldPerXp;
            if (p == null || xp <= 0 || Data.gold < cost) return -1;
            Data.gold -= cost;
            return AddXp(p, xp);
        }

        /// <summary>Personagens de antes do GDD-024 (sem nível) entram no nível em que os pontos cabem: 30 → 4.</summary>
        public static void MigrateLevel(CharacterPreset c)
        {
            if (c == null || c.level > 0) return;
            int pts = Points(c);
            c.level = Mathf.Max(1, Mathf.CeilToInt((pts - AttributeBudget) / (float)PointsPerLevel) + 1);
        }
        public const int MaxCharacters = 12;
        public const int MaxNameLength = 20;
        private const string Key = "pangea.profile.v1";

        private static PlayerProfileData _data;

        public static PlayerProfileData Data
        {
            get { if (_data == null) Load(); return _data; }
        }

        public static string Name => Data.playerName ?? "";
        public static bool HasName => !string.IsNullOrWhiteSpace(Data.playerName);
        public static int Count => Data.characters.Count;
        public static bool IsReady => HasName && Selected != null;

        /// <summary>Personagem em uso (null se o perfil ainda não tem nenhum).</summary>
        public static CharacterPreset Selected
        {
            get
            {
                var d = Data;
                if (d.characters.Count == 0) return null;
                d.selected = Mathf.Clamp(d.selected, 0, d.characters.Count - 1);
                return d.characters[d.selected];
            }
        }

        /// <summary>O que falta para poder jogar, em texto para a tela (vazio = pronto).</summary>
        public static string MissingText =>
            !HasName ? "Escolha um nome de jogador."
            : Selected == null ? "Crie pelo menos um personagem."
            : "";

        public static void Load()
        {
            _data = null;
            string json = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { _data = JsonUtility.FromJson<PlayerProfileData>(json); }
                catch (Exception e) { Debug.LogWarning("[Perfil] perfil salvo ilegível, começando do zero: " + e.Message); }
            }
            if (_data == null)
            {
                _data = new PlayerProfileData();
                ImportLegacyPresets();
            }
            if (_data.characters == null) _data.characters = new List<CharacterPreset>();
            _data.characters.RemoveAll(c => c == null || c.stats == null);
            foreach (var c in _data.characters) { Equipment.EnsureKit(c); MigrateLevel(c); }   // GDD-021: personagens antigos ganham o kit da arma
            Debug.Log($"[Perfil] carregado: nome='{_data.playerName}', {_data.characters.Count} personagem(ns), em uso={_data.selected}");
        }

        /// <summary>Personagens da versão antiga (um arquivo por preset) entram no perfil na primeira vez.</summary>
        private static void ImportLegacyPresets()
        {
            try
            {
                foreach (var p in CharacterStorage.LoadAll())
                    if (IsValid(p) && _data.characters.Count < MaxCharacters) _data.characters.Add(p);
                if (_data.characters.Count > 0) Save();
            }
            catch (Exception e) { Debug.LogWarning("[Perfil] não foi possível importar personagens antigos: " + e.Message); }
        }

        public static void Save()
        {
            if (_data == null) return;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(_data));
            PlayerPrefs.Save();
        }

        public static void SetName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
            Data.playerName = name;
            Save();
        }

        public static void Select(int index)
        {
            if (Data.characters.Count == 0) return;
            Data.selected = Mathf.Clamp(index, 0, Data.characters.Count - 1);
            Save();
        }

        /// <summary>Grava um personagem: index &lt; 0 cria um novo; senão substitui. Devolve o índice final (−1 se recusado).</summary>
        /// <summary>Até este nível o personagem é editável à vontade; acima, só ganha pontos (2026-11-14).</summary>
        public const int FreeEditMaxLevel = 2;
        public static bool IsLocked(CharacterPreset p) => Level(p) > FreeEditMaxLevel;

        /// <summary>Personagem acima do nível 2: só pode SOMAR pontos; arma, itens, aparência, nome e
        /// atributos já gastos ficam. Devolve o motivo do bloqueio, ou "" se a troca vale.</summary>
        public static string WhyEditBlocked(CharacterPreset old, CharacterPreset neu)
        {
            if (old == null || neu == null || !IsLocked(old)) return "";
            if (Level(neu) != Level(old)) return "o nível não muda na edição";
            if (neu.weaponId != old.weaponId || (neu.offhandId ?? "") != (old.offhandId ?? "") || (neu.headId ?? "") != (old.headId ?? "")
                || (neu.torsoId ?? "") != (old.torsoId ?? "") || (neu.legsId ?? "") != (old.legsId ?? "")
                || (neu.modelId ?? "") != (old.modelId ?? "") || neu.presetName != old.presetName)
                return $"acima do nível {FreeEditMaxLevel} só dá para adicionar pontos";
            var a = old.stats; var b = neu.stats;
            if (b.STR < a.STR || b.VIT < a.VIT || b.DEX < a.DEX || b.AGI < a.AGI || b.INT < a.INT || b.WIS < a.WIS)
                return "pontos já gastos não voltam";
            return "";
        }

        public static int Put(int index, CharacterPreset preset)
        {
            if (!IsValid(preset)) return -1;
            var d = Data;
            if (index >= 0 && index < d.characters.Count && WhyEditBlocked(d.characters[index], preset) != "") return -1;
            if (index >= 0 && index < d.characters.Count) d.characters[index] = preset;
            else
            {
                if (d.characters.Count >= MaxCharacters) return -1;
                d.characters.Add(preset);
                index = d.characters.Count - 1;
            }
            d.selected = index;
            Save();
            return index;
        }

        public static void Remove(int index)
        {
            var d = Data;
            if (index < 0 || index >= d.characters.Count) return;
            d.characters.RemoveAt(index);
            d.selected = Mathf.Clamp(d.selected >= index ? d.selected - 1 : d.selected, 0, Mathf.Max(0, d.characters.Count - 1));
            Save();
        }

        public static int Points(CharacterPreset p)
        {
            if (p == null || p.stats == null) return 0;
            var s = p.stats;
            return Mathf.RoundToInt(s.STR + s.VIT + s.DEX + s.AGI + s.INT + s.WIS);
        }

        /// <summary>Regra única de personagem válido (perfil e servidor): orçamento exato e atributos nos limites.</summary>
        public static bool IsValid(CharacterPreset p) => string.IsNullOrEmpty(WhyInvalid(p));

        public static string WhyInvalid(CharacterPreset p)
        {
            if (p == null || p.stats == null) return "personagem vazio";
            int pts = Points(p);
            int budget = BudgetFor(p);
            if (pts < budget) return $"tem {budget - pts} ponto{(budget - pts == 1 ? "" : "s")} para distribuir";
            if (pts > budget) return $"usa {pts} pontos (o nível {Level(p)} tem {budget})";
            float min = CharacterConfig.AttrMin, max = CharacterConfig.AttrMax;
            var s = p.stats;
            foreach (float v in new[] { s.STR, s.VIT, s.DEX, s.AGI, s.INT, s.WIS })
                if (v < min || v > max) return "atributo fora dos limites";
            return "";
        }

        /// <summary>Nome livre para um personagem novo ("Personagem 2"...).</summary>
        public static string NextCharacterName()
        {
            for (int n = Data.characters.Count + 1; n < 100; n++)
            {
                string name = "Personagem " + n;
                if (!Data.characters.Exists(c => c.presetName == name)) return name;
            }
            return "Personagem";
        }

        /// <summary>Esquece o perfil em memória (testes) e, se pedido, o salvo.</summary>
        public static void ResetForTests(bool erase)
        {
            if (erase) PlayerPrefs.DeleteKey(Key);
            _data = erase ? new PlayerProfileData() : null;
        }

        public static CharacterPreset Clone(CharacterPreset p) =>
            p == null ? null : JsonUtility.FromJson<CharacterPreset>(JsonUtility.ToJson(p));
    }
}
