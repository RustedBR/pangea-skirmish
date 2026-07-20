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
        public string[] terrainNames; // idx = x * height + y → nome do sprite de terreno (atlas)
        public int[] heights;      // idx = x * height + y → elevação do terreno
        public bool[] voidCells;   // idx = x * height + y → cell vazia (não renderiza)
        public string[] objectNames; // idx = x * height + y → nome do objeto (1 por célula) ou "" se vazio
        public List<UnitPlacement> units = new List<UnitPlacement>();

        public int Flat(int x, int y) => x * height + y;
        public string TileAt(int x, int y)  => terrainNames[Flat(x, y)] ?? "";
        public string TerrainAt(int x, int y) => terrainNames[Flat(x, y)] ?? "";
        public int HeightAt(int x, int y) => heights[Flat(x, y)];
        public bool IsVoid(int x, int y)  => voidCells != null && voidCells[Flat(x, y)];
        public string ObjectAt(int x, int y) => (objectNames != null && Flat(x, y) < objectNames.Length) ? objectNames[Flat(x, y)] : "";
        public void SetObject(int x, int y, string name)
        {
            if (objectNames == null) objectNames = new string[width * height];
            objectNames[Flat(x, y)] = name ?? "";
        }

        // Mapa novo e vazio: tudo grama plana (tile 0, altura 0, sem objetos).
        public static MapData CreateEmpty(int w, int h)
        {
            var m = new MapData { width = w, height = h,
                terrainNames = new string[w * h],
                heights = new int[w * h],
                voidCells = new bool[w * h], objectNames = new string[w * h] };
            for (int i = 0; i < w * h; i++) { m.terrainNames[i] = "grass_tile_full"; m.heights[i] = 0; m.voidCells[i] = false; m.objectNames[i] = ""; }
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
    // "Pincéis" do editor de terreno — referenciados pelo NOME do sprite no atlas.
    [Serializable]
    public class TileBrush
    {
        public string name;        // rótulo na UI
        public string spriteName;  // nome exato do sprite no .meta (ex.: "grass_tile_full")
        public TileKind kind;      // Terrain / Ramp / Object
        public int height;         // full=+2, half=+1, água=-2/-1; objeto=0
        public bool isWall;        // objeto sólido bloqueia a célula
        public string dir;         // direção da rampa (NO/NE/SO/SE)
    }
    public static class TilePalette
    {
        // Paleta curada — 22 sprites nomeados no atlas (Mareus revisou no Sprite Editor).
        // Ignora tileset_8 / tileset_20 (não revisados).
        public static readonly TileBrush[] Brushes =
        {
            // ── Terreno ──
            new TileBrush{ name="Grama full",  spriteName="grass_tile_full",  kind=TileKind.Terrain, height=2,  isWall=false },
            new TileBrush{ name="Grama half",  spriteName="grass_tile_half",  kind=TileKind.Terrain, height=1,  isWall=false },
            new TileBrush{ name="Água full",   spriteName="water_full",       kind=TileKind.Terrain, height=-2, isWall=false },
            new TileBrush{ name="Água half",   spriteName="water_half",       kind=TileKind.Terrain, height=-1, isWall=false },
            // Rampas direcionais (ponte de altura)
            new TileBrush{ name="Rampa NO",    spriteName="grass_tile_ramp_NO", kind=TileKind.Ramp, height=0, isWall=false, dir="NO" },
            new TileBrush{ name="Rampa NE",    spriteName="grass_tile_ramp_NE", kind=TileKind.Ramp, height=0, isWall=false, dir="NE" },
            new TileBrush{ name="Rampa SO",    spriteName="grass_tile_ramp_SO", kind=TileKind.Ramp, height=0, isWall=false, dir="SO" },
            new TileBrush{ name="Rampa SE",    spriteName="grass_tile_ramp_SE", kind=TileKind.Ramp, height=0, isWall=false, dir="SE" },
            // Penhascos / taludes (terreno decorativo de borda)
            new TileBrush{ name="Penhasco SO", spriteName="grass_cliff_SO",   kind=TileKind.Terrain, height=0, isWall=false },
            new TileBrush{ name="Penhasco SE", spriteName="grass_cliff_SE",   kind=TileKind.Terrain, height=0, isWall=false },
            new TileBrush{ name="Talude NO",   spriteName="grass_slide_NO",   kind=TileKind.Terrain, height=0, isWall=false },
            new TileBrush{ name="Talude NE",   spriteName="grass_slide_NE",   kind=TileKind.Terrain, height=0, isWall=false },
            new TileBrush{ name="Talude SO",   spriteName="grass_slide_SO",   kind=TileKind.Terrain, height=0, isWall=false },
            new TileBrush{ name="Talude SE",   spriteName="grass_slide_SE",   kind=TileKind.Terrain, height=0, isWall=false },
            new TileBrush{ name="Escada NE",   spriteName="stairs_NE",        kind=TileKind.Ramp, height=0, isWall=false, dir="NE" },
            new TileBrush{ name="Escada NO",   spriteName="stairs_NO",        kind=TileKind.Ramp, height=0, isWall=false, dir="NO" },
            // ── Objetos (1 por célula) ──
            new TileBrush{ name="Árvore",      spriteName="tree",    kind=TileKind.Object, height=0, isWall=true  },
            new TileBrush{ name="Pedra 1",     spriteName="stone_1", kind=TileKind.Object, height=0, isWall=true  },
            new TileBrush{ name="Pedra 2",     spriteName="stone_2", kind=TileKind.Object, height=0, isWall=true  },
            new TileBrush{ name="Arbusto",     spriteName="bush",    kind=TileKind.Object, height=0, isWall=false },
            // Utilitário
            new TileBrush{ name="Apagar (void)", spriteName="",      kind=TileKind.Terrain, height=0, isWall=false },
        };
    }
}
