using System;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    // Uma unidade posicionada no editor (classe + time + posição + stats custom).
    [Serializable]
    public class UnitPlacement
    {
        public string classId = "fighter";  // id no ClassCatalog
        public int    team    = 0;           // 0 = Player, 1 = Enemy
        public int    x       = 0;           // anchor.x
        public int    y       = 0;           // anchor.y
        public string displayName = "Unidade";
        public UnitStatBlock stats = new UnitStatBlock();
        public string weaponId = "";       // id no WeaponCatalog; vazio = usa default da classe
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
        public string classId    = "fighter";
        public string weaponId   = "Hatchet";
        public UnitStatBlock stats = new UnitStatBlock();
    }

    // Personagem selecionado no menu principal para usar na batalha default.
    public static class RuntimeSelectedCharacter
    {
        public static CharacterPreset Active;
    }

    // Catálogo de classes jogáveis (sprite + stats default).
    [Serializable]
    public class UnitClassDef
    {
        public string id, displayName, resourcePath;
        public UnitStatBlock defaultStats;
        public string defaultWeaponId; // id da arma padrão para a classe
    }

    public static class ClassCatalog
    {
        public static readonly UnitClassDef[] All =
        {
            new UnitClassDef { id="fighter", displayName="Guerreiro",
                resourcePath="Sprites/TinyTactics/Characters/fighter",
                defaultStats=new UnitStatBlock{ STR=8,VIT=10,DEX=2,AGI=3,INT=1,WIS=1,Footprint=3,AttackRange=1 },
                defaultWeaponId="Hatchet" },
            new UnitClassDef { id="mage", displayName="Mago",
                resourcePath="Sprites/TinyTactics/Characters/mage",
                defaultStats=new UnitStatBlock{ STR=3,VIT=5,DEX=8,AGI=10,INT=1,WIS=1,Footprint=3,AttackRange=1 },
                defaultWeaponId="WoodenStaff" },
            new UnitClassDef { id="cleric", displayName="Clérigo",
                resourcePath="Sprites/TinyTactics/Characters/cleric",
                defaultStats=new UnitStatBlock{ STR=4,VIT=7,DEX=4,AGI=5,INT=4,WIS=8,Footprint=3,AttackRange=1 },
                defaultWeaponId="Scepter" },
        };
        public static UnitClassDef Get(string id)
        {
            foreach (var c in All) if (c.id == id) return c;
            return All[0];
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
