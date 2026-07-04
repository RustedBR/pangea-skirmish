using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    // Uma unidade posicionada no editor (sprite direto + time + posição + stats).
    [Serializable]
    public class UnitPlacement
    {
        public string spritePath = "";       // resourcePath do sprite (self-contained; fallback = CharacterSpriteCatalog.Default)
        public int    team    = 0;           // 0 = Player, 1 = Enemy
        public int    x       = 0;           // anchor.x
        public int    y       = 0;           // anchor.y
        public string displayName = "Unidade";
        public UnitStatBlock stats = new UnitStatBlock();
        public string weaponId = "";       // id no WeaponCatalog; vazio = sem arma
    }

    // Cenário completo: terreno (flatten) + unidades.
    [Serializable]
    public class MapData
    {
        public string mapName = "Novo Mapa";
        public int width  = 20;
        public int height = 20;
        public int[] tileIndices;  // idx = x * height + y → índice do atlas
        public int[] heights;      // idx = x * height + y → elevação
        public bool[] voidCells;   // idx = x * height + y → cell vazia (não renderiza)
        public List<UnitPlacement> units = new List<UnitPlacement>();

        public int Flat(int x, int y) => x * height + y;
        public int TileAt(int x, int y)   => tileIndices[Flat(x, y)];
        public int HeightAt(int x, int y) => heights[Flat(x, y)];
        public bool IsVoid(int x, int y)  => voidCells != null && voidCells[Flat(x, y)];

        // Mapa novo e vazio: tudo grama plana (tile 0, altura 0).
        public static MapData CreateEmpty(int w, int h)
        {
            var m = new MapData { width = w, height = h,
                tileIndices = new int[w * h], heights = new int[w * h],
                voidCells = new bool[w * h] };
            for (int i = 0; i < w * h; i++) { m.tileIndices[i] = 0; m.heights[i] = 0; m.voidCells[i] = false; }
            return m;
        }

        public void SetVoid(int x, int y, bool v)
        {
            if (voidCells == null) voidCells = new bool[width * height];
            voidCells[Flat(x, y)] = v;
        }
    }

    // Sobrevive ao LoadScene (igual RuntimeTuning). null = batalha padrão.
    public static class RuntimeMap { public static MapData Selected; }

    // Passa um mapa para o Sandbox EDITAR (null = criar do zero). Consumido e limpo no Start.
    public static class RuntimeSandbox { public static MapData MapToEdit; }

    // Personagem salvo pelo jogador — reutilizável entre mapas.
    [Serializable]
    public class CharacterPreset
    {
        public string presetName = "Personagem";
        public string spritePath = "";       // resourcePath do sprite escolhido (novo modelo)
        public string classId    = "";       // LEGADO — usado apenas para migração no load; não usar em código novo
        public string weaponId   = "Hatchet";
        public UnitStatBlock stats = new UnitStatBlock();
    }

    // Personagem selecionado no menu principal para usar na batalha default.
    public static class RuntimeSelectedCharacter
    {
        public static CharacterPreset Active;
    }

    // Catálogo de sprites disponíveis para personagens (substitui o conceito de "classe").
    [Serializable]
    public class CharacterSpriteDef
    {
        public string displayName;
        public string resourcePath;
    }

    public static class CharacterSpriteCatalog
    {
        public static readonly CharacterSpriteDef[] All =
        {
            new CharacterSpriteDef { displayName="Guerreiro", resourcePath="Sprites/TinyTactics/Characters/fighter"  },
            new CharacterSpriteDef { displayName="Mago",      resourcePath="Sprites/TinyTactics/Characters/mage"     },
            new CharacterSpriteDef { displayName="Clérigo",   resourcePath="Sprites/TinyTactics/Characters/cleric"   },
        };

        public static string Default => All[0].resourcePath;

        public static CharacterSpriteDef GetByPath(string path)
        {
            foreach (var s in All) if (s.resourcePath == path) return s;
            return All[0];
        }

        /// <summary>Converte classId legado (fighter/mage/cleric) para resourcePath.</summary>
        public static string LegacySprite(string classId)
        {
            if (classId == "mage")   return "Sprites/TinyTactics/Characters/mage";
            if (classId == "cleric") return "Sprites/TinyTactics/Characters/cleric";
            return "Sprites/TinyTactics/Characters/fighter"; // fighter ou qualquer valor desconhecido
        }
    }


    // Catálogo de armas.
    public static class WeaponCatalog
    {
        // Fallback hardcoded das 6 armas, caso GameTuning não carregue.
        private static readonly WeaponDef[] _fallback = {
            new WeaponDef{ id="Hatchet",     displayName="Machado",        damage=4, range=1 },
            new WeaponDef{ id="IronAxe",     displayName="Machado de Ferro",damage=7, range=1 },
            new WeaponDef{ id="WoodenSword", displayName="Espada de Madeira",damage=3, range=1 },
            new WeaponDef{ id="IronSword",   displayName="Espada de Ferro", damage=5, range=1 },
            new WeaponDef{ id="WoodenStaff", displayName="Cajado",          damage=2, range=2 },
            new WeaponDef{ id="Scepter",     displayName="Cetro",           damage=3, range=2 },
        };

        public static WeaponDef[] All()
        {
            var t = RuntimeTuning.Active ?? Resources.Load<GameTuning>("GameTuning");
            return (t != null && t.weapons != null && t.weapons.Length > 0) ? t.weapons : _fallback;
        }

        public static WeaponDef Get(string id) // null se id vazio/"none"; senão acha por id (fallback null)
        {
            if (string.IsNullOrEmpty(id) || id == "none") return null;
            var weapons = All();
            foreach (var w in weapons) if (w.id == id) return w;
            return null; // id não encontrado
        }
    }

    // "Pincéis" do editor de terreno: cada um aplica (tileIndex, height) a uma célula.
    [Serializable]
    public class TileBrush { public string name; public int tileIndex; public int height; }

    public static class TilePalette
    {
        // Paleta curada do atlas TinyTactics.
        // Tiles validados visualmente no jogo.
        // Altura agora é genérica — clicar em tile existente empilha (+1).
        public static readonly TileBrush[] Brushes =
        {
            new TileBrush{ name="Grama",      tileIndex=0,  height=0 },
            new TileBrush{ name="Terra",      tileIndex=4,  height=0 },
            new TileBrush{ name="Pedra",      tileIndex=8,  height=0 },
            new TileBrush{ name="Areia",      tileIndex=12, height=0 },
            new TileBrush{ name="Água",       tileIndex=16, height=0 },
            new TileBrush{ name="Grama escura",tileIndex=20, height=0 },
            new TileBrush{ name="Caminho",    tileIndex=24, height=0 },
            new TileBrush{ name="Apagar",     tileIndex=-1, height=0 },
        };

        public const int VOID_INDEX = -1;
    }
}
