using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Grid isométrico 2D (sprites em z=0, câmera ortográfica 2D).
    /// Conversões grid↔mundo isométricas com elevação por célula. A LÓGICA de
    /// footprint/reachable/overlap é independente do render (coords col,row).
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        public int width = 20;
        public int height = 20;
        public float tileSize = 1f;

        [Header("Isometrico")]
        public IsoConfig iso;
        public TileDatabase tileDatabase;

        [Header("Cores do tile (highlight)")]
        public Color highlightColor = new Color(0.35f, 0.85f, 0.45f);
        public Color bonusHighlightColor = new Color(0.90f, 0.75f, 0.30f);

        [Header("Tile Rendering")]
        public bool useAtlasSprites = true; // false = bloco gerado; true = atlas TinyTactics
        // Nome do sprite de grama plana (fallback quando não há terreno definido)
        private const string DEFAULT_TERRAIN = "grass_tile_full";

        [Header("Tile Alignment (debug)")]
        public Vector2 spritePivot = new Vector2(0.5f, 0.75f);  // Pivot do sprite no losango
        public Vector3 positionOffset = Vector3.zero;  // Offset de posição dos tiles
        public float spriteScale = 1f;  // Scale do sprite
        public bool useDebugGridSprite = false;  // Ativar sprite de debug com grid

        [Header("Grid Lines")]
        public bool showGridLines = true;
        public Color gridLineColor = new Color(1f, 1f, 1f, 0.25f);

        private bool _lastDebugState = false;

        // ── RIG DE ROTAÇÃO DO MAPA (Caminho 3, 2026-07-14) ──
        // Pivot no centro do mapa. _tilesRoot + _objectsRoot viram filhos dele.
        // A rotação de vista é aplicada AQUI, em torno do eixo Z do MUNDO
        // (perpendicular ao chão XY deste projeto = "up do mundo" aqui).
        // A câmera fica PARADA (sem tilt), logo a percepção isométrica não
        // se perde e o plano de jogo NÃO sobe (era o bug de girar a câmera).
        private Transform _gridRig;
        private float _gridRot;        // rotação Z atual (lerp)
        private float _gridRotTarget;  // alvo (múltiplo de 90, acumula p/ sentido)
        private const float GRID_ROT_SPEED = 8f;

        public MapData sourceMap;        // se != null, Build() usa este mapa em vez do platô hardcoded
        private string[,] _terrainNames;  // nome do sprite de terreno por célula (atlas)
        private string[,] _objectNames;    // nome do objeto por célula (1 por célula) ou "" vazio
        private bool[,] _voidCells;      // cells vazias (não renderiza, sem colisão)
        private Transform _tilesRoot;      // raiz dos tiles de terreno
        private Transform _objectsRoot;    // raiz dos objetos overlay

        private SpriteRenderer[,] _tiles;
        private SpriteRenderer[,] _objectSprites;
        private Color[,] _baseColors;
        private int[,] _heights;
        private Sprite _blockSprite;
        private static Sprite _gridLineSprite;

        private float _halfW = 1.0f;   // meia-largura do losango (unidades)
        private float _halfH = 0.5f;   // meia-altura do losango (unidades)

        // Rotação do MAPA: aplicada no _gridRig (eixo Z do mundo), NÃO na câmera.
        // Assim o losango 2:1 não deforma, o plano não sobe e os tiles giram
        // de fato no world space (Caminho 3, 2026-07-14).
        private static GridManager _instance;
        public static GridManager Instance => _instance;
        /// <summary>
        /// Pivot de rotação do mapa (Caminho 3, 2026-07-14). As unidades e labels
        /// devem ser filhos DESTE transform p/ acompanhar a posição do tile ao girar
        /// o grid, mas seus sprites cancelam a rotação do pai (ficam em pé).
        /// </summary>
        public Transform GridRig => _gridRig;
        private float _heightStep = 0.5f;

        // Camada unificada de Colisão & Ocupação (Parte A). Data-only, alimentada aqui.
        private CollisionGrid _collision;
        public CollisionGrid Collision => _collision;

        private readonly List<Vector2Int> _highlighted = new List<Vector2Int>();
        private readonly Dictionary<Vector2Int, SpriteRenderer> _highlightBorders = new();

        public float HalfW => _halfW;
        public float HalfH => _halfH;
        public Sprite BlockSprite => _blockSprite;

        private void Awake()
        {
            _instance = this;
            _collision = new CollisionGrid();
            // Cores vindas do GameTuning (o componente é criado em runtime — os defaults
            // dos campos serializados nunca são editados via Inspector).
            var T = Tuning.Get();
            highlightColor = T.reachHighlightColor;
            bonusHighlightColor = T.bonusHighlightColor;
            gridLineColor = T.gridLineColor;

            if (iso == null) iso = ScriptableObject.CreateInstance<IsoConfig>();
            if (tileDatabase == null) tileDatabase = GetComponent<TileDatabase>();
            if (tileDatabase == null) tileDatabase = gameObject.AddComponent<TileDatabase>();

            _halfW = tileSize * iso.TileUnitsW * 0.5f;
            _halfH = tileSize * iso.TileUnitsH * 0.5f;
            _heightStep = iso.heightStep;
            Debug.Log($"[Grid:Awake] tileSize={tileSize} TileUnitsW={iso.TileUnitsW} TileUnitsH={iso.TileUnitsH} -> _halfW={_halfW} _halfH={_halfH} _heightStep={_heightStep} useAtlasSprites={useAtlasSprites} useDebugGridSprite={useDebugGridSprite}");

            // Gerar sprite fallback enquanto TileDatabase está em debug
            BuildBlockSprite();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---- Grid Lines: contorno losango isométrico ----
        private static Sprite GetGridLineSprite()
        {
            if (_gridLineSprite != null) return _gridLineSprite;

            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

            // Transparente por padrão
            for (int i = 0; i < S * S; i++) tex.SetPixel(i % S, i / S, Color.clear);

            Color line = Color.white;

            // Bordas do losango do topo (32x32): (0,24)→(16,32)→(32,24)→(16,16)→(0,24)
            DrawEdge(tex, 0, 24, 16, 32, line);
            DrawEdge(tex, 16, 32, 32, 24, line);
            DrawEdge(tex, 32, 24, 16, 16, line);
            DrawEdge(tex, 16, 16, 0, 24, line);

            tex.Apply();
            _gridLineSprite = Sprite.Create(tex, new Rect(0, 0, S, S),
                new Vector2(0.5f, 0.75f), 16);
            _gridLineSprite.name = "GridLineOverlay";
            return _gridLineSprite;
        }

        private static void DrawEdge(Texture2D tex, int x0, int y0, int x1, int y1, Color c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int steps = Mathf.Max(dx, -dy) + 1;
            for (int i = 0; i <= steps; i++)
            {
                if (x0 >= 0 && x0 < tex.width && y0 >= 0 && y0 < tex.height)
                    tex.SetPixel(x0, y0, c);
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public bool IsVoid(int x, int y)
        {
            if (_voidCells == null) return false;
            if (x < 0 || y < 0 || x >= width || y >= height) return true;
            return _voidCells[x, y];
        }

        public void SetVoid(int x, int y, bool v)
        {
            if (_voidCells == null || x < 0 || y < 0 || x >= width || y >= height) return;
            _voidCells[x, y] = v;
        }

        // ---- Bloco gerado: tile isométrico com topo de grama verde ----
        private void BuildBlockSprite()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < S * S; i++) tex.SetPixel(i % S, i / S, Color.clear);

            Vector2[] top   = { new(0,48), new(32,64), new(64,48), new(32,32) };
            Vector2[] left  = { new(0,48), new(32,32), new(32,16), new(0,32) };
            Vector2[] right = { new(64,48), new(32,32), new(32,16), new(64,32) };

            Color cTop   = new Color(0.30f, 0.62f, 0.22f); // verde grama
            Color cLeft  = new Color(0.42f, 0.28f, 0.14f); // terra média
            Color cRight = new Color(0.28f, 0.17f, 0.08f); // terra escura

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (InConvex(p, top))        tex.SetPixel(x, y, cTop);
                else if (InConvex(p, left))  tex.SetPixel(x, y, cLeft);
                else if (InConvex(p, right)) tex.SetPixel(x, y, cRight);
            }
            tex.Apply();
            _blockSprite = Sprite.Create(tex, new Rect(0, 0, S, S),
                spritePivot, iso.pixelsPerUnit);
        }

        private static bool InConvex(Vector2 p, Vector2[] poly)
        {
            bool pos = false, neg = false;
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Length];
                float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                if (cross > 0.0001f) pos = true;
                else if (cross < -0.0001f) neg = true;
                if (pos && neg) return false;
            }
            return true;
        }

        public int HeightAt(int x, int y)
        {
            if (_heights == null) return 0;
            x = Mathf.Clamp(x, 0, width - 1);
            y = Mathf.Clamp(y, 0, height - 1);
            return _heights[x, y];
        }

        public string GetTerrainName(int x, int y)
        {
            if (_terrainNames == null) return DEFAULT_TERRAIN;
            x = Mathf.Clamp(x, 0, width - 1); y = Mathf.Clamp(y, 0, height - 1);
            var n = _terrainNames[x, y];
            return string.IsNullOrEmpty(n) ? DEFAULT_TERRAIN : n;
        }

        public string GetObjectName(int x, int y)
        {
            if (_objectNames == null) return "";
            x = Mathf.Clamp(x, 0, width - 1); y = Mathf.Clamp(y, 0, height - 1);
            return _objectNames[x, y] ?? "";
        }

        public int GetHeight(int x, int y)
        {
            if (_heights == null) return 0;
            x = Mathf.Clamp(x, 0, width - 1); y = Mathf.Clamp(y, 0, height - 1);
            return _heights[x, y];
        }

        // Edita uma célula em runtime (usado pelo editor de terreno do Sandbox).
        // Terreno (nome) + objeto (nome) são camadas independentes.
        // objectName == "" ou null → remove o objeto da célula.
        public void SetCell(int x, int y, string terrainName, int cellHeight, string objectName, bool isVoid = false)
        {
            Debug.Log($"[Grid] SetCell ENTER ({x},{y}) width={width} height={height} terrain={terrainName} h={cellHeight} isVoid={isVoid}");
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int oldHeight = _heights[x, y];

            if (isVoid)
            {
                _voidCells[x, y] = true;
                _terrainNames[x, y] = "";
                _objectNames[x, y] = "";
                DestroyObjectAt(x, y);
                var tileSr = _tiles != null ? _tiles[x, y] : null;
                if (tileSr != null) { Destroy(tileSr.gameObject); _tiles[x, y] = null; }
                if (_collision != null) _collision.SetWalkable(x, y, false);
                return;
            }

            _voidCells[x, y] = false;
            _heights[x, y] = cellHeight;
            _terrainNames[x, y] = string.IsNullOrEmpty(terrainName) ? DEFAULT_TERRAIN : terrainName;
            _objectNames[x, y] = objectName ?? "";
            if (cellHeight != oldHeight)
                Debug.Log($"[Grid] SetCell ({x},{y}) terrain={_terrainNames[x,y]} oldH={oldHeight} newH={cellHeight} worldY_apos={CellToWorld(new Vector2Int(x, y)).y:F2} (heightStep={_heightStep})");
            else
                Debug.Log($"[Grid] SetCell ({x},{y}) SEM MUDANÇA oldH={oldHeight} newH={cellHeight}");

            // Parte A: manter a camada de colisão sincronizada
            if (_collision != null)
            {
                bool walkable = string.IsNullOrEmpty(_objectNames[x, y]) || !IsObjectWall(_objectNames[x, y]);
                _collision.SetWalkable(x, y, walkable);
                _collision.SetZLevel(x, y, cellHeight);
            }

            // Recriar/atualizar tile de terreno
            var sr = _tiles != null ? _tiles[x, y] : null;
            if (sr == null)
            {
                BuildSingleTile(x, y);
            }
            else
            {
                sr.transform.position = CellToWorld(new Vector2Int(x, y)) + positionOffset;
                var sprite = tileDatabase != null ? tileDatabase.GetTile(_terrainNames[x, y]) : null;
                sr.sprite = sprite ?? _blockSprite;
                if (cellHeight != oldHeight)
                    UpdateSideFaces(x, y, cellHeight);
            }

            // (Re)construir objeto overlay
            RefreshObjectAt(x, y);
        }

        // Retorna se o objeto nomeado é parede (bloqueia a célula).
        private bool IsObjectWall(string objectName)
        {
            var def = tileDatabase != null ? tileDatabase.GetDef(objectName) : null;
            return def != null && def.isWall;
        }

        // Cria/atualiza o sprite de objeto acima do terreno (z = HeightAt).
        private void RefreshObjectAt(int x, int y)
        {
            var name = GetObjectName(x, y);
            DestroyObjectAt(x, y);
            if (string.IsNullOrEmpty(name) || tileDatabase == null) return;

            if (_objectsRoot == null)
            {
                _objectsRoot = new GameObject("Objects").transform;
                _objectsRoot.SetParent(_gridRig != null ? _gridRig : transform, false);
            }

            var go = new GameObject($"Obj_{x}_{y}");
            go.transform.SetParent(_objectsRoot, false);
            // CellToWorld já inclui a altura (HeightAt * _heightStep) → está no topo do tile.
            // Não somar _heights de novo (senão a árvore sobe o dobro).
            var top = CellToWorld(new Vector2Int(x, y)) + positionOffset;
            go.transform.position = top;
            go.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = tileDatabase.GetTile(name) ?? _blockSprite;
            sr.sortingOrder = (x + y) * 4 + 2; // acima do terreno
            sr.color = Color.white;
            _objectSprites[x, y] = sr;
        }

        private void DestroyObjectAt(int x, int y)
        {
            var sr = _objectSprites != null ? _objectSprites[x, y] : null;
            if (sr != null) { Destroy(sr.gameObject); _objectSprites[x, y] = null; }
        }

        /// <summary>
        /// Atualiza as faces laterais de um tile empilhado.
        /// Usa a "face lateral" recortada do sprite do tile (metade inferior do
        /// losango) empilhada N vezes conforme a altura — efeito de cubo com
        /// textura real, sem buraco entre o chão e o topo.
        /// </summary>
        private void UpdateSideFaces(int x, int y, int height)
        {
            var tileGo = _tiles[x, y]?.gameObject;
            if (tileGo == null) return;

            for (int i = tileGo.transform.childCount - 1; i >= 0; i--)
            {
                var child = tileGo.transform.GetChild(i).gameObject;
                if (child.name.StartsWith("SideFace"))
                    Destroy(child);
            }

            if (height <= 0) return;

            float sideH = height * _heightStep;               // altura total (world)
            int levels = Mathf.Max(1, Mathf.RoundToInt(sideH / 0.5f)); // cópias de 0.5
            float levelH = sideH / levels;

            Sprite sideSprite = (tileDatabase != null) ? tileDatabase.GetSideFace(_terrainNames[x, y]) : null;
            if (sideSprite == null) sideSprite = _blockSprite;

            // Altura world natural do sprite de face (PPU 16)
            float sideSpriteWorldH = sideSprite.rect.height / TileDatabase.BLOCK_PPU;
            int order = _tiles[x, y].sortingOrder - 10;

            // Duas faces (esquerda escura + direita média), estilo iso, empilhadas.
            for (int k = 0; k < levels; k++)
            {
                float yc = -levelH * (k + 0.5f);          // centro vertical do nível k
                float sy = levelH / sideSpriteWorldH;      // escala Y p/ caber em levelH

                // Face esquerda (mais escura)
                var goL = new GameObject($"SideFace_L{k}");
                goL.transform.SetParent(tileGo.transform, false);
                // Migração XY→XZ (2026-07-20): altura sai no Y (em pé saindo do chão).
                goL.transform.localPosition = new Vector3(-_halfW * 0.5f, yc, 0f);
                goL.transform.localScale = new Vector3(_halfW * 2f, sy, 1f); // largura = 1 tile iso
                var srL = goL.AddComponent<SpriteRenderer>();
                srL.sprite = sideSprite;
                srL.color = new Color(0.55f, 0.45f, 0.35f);  // terra escura
                srL.sortingOrder = order + k;

                // Face direita (média)
                var goR = new GameObject($"SideFace_R{k}");
                goR.transform.SetParent(tileGo.transform, false);
                goR.transform.localPosition = new Vector3(_halfW * 0.5f, yc, 0f);
                goR.transform.localScale = new Vector3(_halfW * 2f, sy, 1f);
                var srR = goR.AddComponent<SpriteRenderer>();
                srR.sprite = sideSprite;
                srR.color = new Color(0.75f, 0.6f, 0.45f);   // terra média
                srR.sortingOrder = order + k;
            }
        }

        private void BuildSingleTile(int x, int y)
        {
            if (_tilesRoot == null)
            {
                _tilesRoot = new GameObject("Tiles").transform;
                if (_gridRig == null)
            {
                var center = CellToWorld(new Vector2Int(width / 2, height / 2));
                var rigGo = new GameObject("GridRig");
                rigGo.transform.position = center;
                _gridRig = rigGo.transform;
            }
            _tilesRoot.SetParent(_gridRig, false);
            }

            var go = new GameObject($"Tile_{x}_{y}");
            go.transform.SetParent(_tilesRoot, false);
            go.transform.position = CellToWorld(new Vector2Int(x, y)) + positionOffset;

            // Migração XY→XZ (2026-07-20): o sprite do tile é o TOPO do chão, então
            // precisa ficar DEITADO no plano XZ (rotação -90° em X). O `go` vira um
            // container neutro no chão; o SpriteRenderer vive num filho "Top" deitado.
            // As SideFaces (filhas do `go`, em pé no Y) formam as laterais 3D.
            var topGo = new GameObject("Top");
            topGo.transform.SetParent(go.transform, false);
            topGo.transform.localPosition = Vector3.zero;
            topGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // deita no XZ (topo p/ câmera)
            topGo.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);

            var sr = topGo.AddComponent<SpriteRenderer>();
            Sprite tileSprite = null;
            if (useDebugGridSprite)
                tileSprite = DebugGridSprite.CreateGridDebugSprite();
            else if (useAtlasSprites && tileDatabase != null)
                tileSprite = tileDatabase.GetTile(_terrainNames[x, y]);
            if (tileSprite == null) tileSprite = _blockSprite;

            sr.sprite = tileSprite;
            sr.sortingOrder = (x + y) * 4;
            sr.color = Color.white;
            _tiles[x, y] = sr;
            _baseColors[x, y] = Color.white;

            if (showGridLines)
            {
                // Contorno do losango do topo: vive no topGo (já deitado no XZ).
                var lineGo = new GameObject("GridLine");
                lineGo.transform.SetParent(topGo.transform, false);
                var lineSr = lineGo.AddComponent<SpriteRenderer>();
                lineSr.sprite = GetGridLineSprite();
                lineSr.sortingOrder = sr.sortingOrder + 2;
                lineSr.color = gridLineColor;
            }

            // Adicionar faces laterais se tile já está empilhado
            int h = _heights[x, y];
            if (h > 0)
                UpdateSideFaces(x, y, h);

            // Objeto overlay (se houver)
            RefreshObjectAt(x, y);
        }

        // Copia o terreno atual para um MapData (flatten). units NÃO é tocado aqui.
        public void ExportTerrain(MapData map)
        {
            map.width = width; map.height = height;
            map.terrainNames = new string[width * height];
            map.heights     = new int[width * height];
            map.voidCells   = new bool[width * height];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int f = x * height + y;             // MESMA fórmula de MapData.Flat
                map.terrainNames[f] = _terrainNames[x, y];
                map.heights[f]     = _heights[x, y];
                map.voidCells[f]   = _voidCells[x, y];
            }
        }

        /// <summary>
        /// Exporta o estado atual do grid para um novo MapData (sem tocar na lista de units).
        /// </summary>
        public MapData ExportToMapData()
        {
            var map = new MapData();
            map.width = width;
            map.height = height;
            ExportTerrain(map);
            return map;
        }

        /// <summary>
        /// Expande o grid numa direção (dirX/dirY = -1, 0 ou 1).
        /// Cria novo MapData maior, copia dados existentes com offset, reconstrói tudo.
        /// Retorna o novo MapData ou null se falhar.
        /// </summary>
        public MapData Expand(int dirX, int dirY)
        {
            int newW = width + (dirX != 0 ? 1 : 0);
            int newH = height + (dirY != 0 ? 1 : 0);

            Debug.Log($"[GridManager] Expand: {width}x{height} → {newW}x{newH} (dir={dirX},{dirY})");

            // Calcular offset: se dirX=-1, dados existentes deslocam +1 no novo array
            int offX = dirX < 0 ? 1 : 0;
            int offY = dirY < 0 ? 1 : 0;

            // Criar novo MapData
            var newMap = new MapData();
            newMap.width = newW;
            newMap.height = newH;
            newMap.terrainNames = new string[newW * newH];
            newMap.heights = new int[newW * newH];
            newMap.voidCells = new bool[newW * newH];

            // Preencher tudo como void
            for (int i = 0; i < newW * newH; i++)
            {
                newMap.voidCells[i] = true;
                newMap.heights[i] = 0;
                newMap.terrainNames[i] = "";
            }

            // Copiar dados existentes com offset
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int nx = x + offX;
                int ny = y + offY;
                int nf = nx * newH + ny;
                int of = x * height + y;
                newMap.terrainNames[nf] = _terrainNames[x, y];
                newMap.heights[nf] = _heights[x, y];
                newMap.voidCells[nf] = _voidCells[x, y];
            }

            Debug.Log($"[GridManager] Expand: dados copiados com offset ({offX},{offY})");

            // Reconstruir grid
            RebuildFromMap(newMap);

            return newMap;
        }

        /// <summary>
        /// Reconstrói o grid a partir de um MapData (destrói sprites antigos, recria arrays).
        /// </summary>
        public void RebuildFromMap(MapData map)
        {
            Debug.Log($"[GridManager] RebuildFromMap: {map.width}x{map.height}");

            // Destruir tiles existentes
            var tilesRoot = transform.Find("Tiles");
            if (tilesRoot != null) Destroy(tilesRoot.gameObject);

            // Atualizar dimensões
            width = map.width;
            height = map.height;

            // Recriar arrays
            _tiles = new SpriteRenderer[width, height];
            _objectSprites = new SpriteRenderer[width, height];
            _baseColors = new Color[width, height];
            _heights = new int[width, height];
            _terrainNames = new string[width, height];
            _objectNames = new string[width, height];
            _voidCells = new bool[width, height];

            // Copiar dados do MapData
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                _heights[x, y] = map.HeightAt(x, y);
                _terrainNames[x, y] = map.TerrainAt(x, y);
                _objectNames[x, y] = map.ObjectAt(x, y);
                _voidCells[x, y] = sourceMap.IsVoid(x, y);
            }

            // Parte A: sincronizar camada de colisão/ocupação com o terreno
            if (_collision != null) _collision.SyncFrom(this);

            // Reconstruir sprites
            _tilesRoot = new GameObject("Tiles").transform;
            if (_gridRig == null)
            {
                var center = CellToWorld(new Vector2Int(width / 2, height / 2));
                var rigGo = new GameObject("GridRig");
                rigGo.transform.position = center;
                _gridRig = rigGo.transform;
            }
            _tilesRoot.SetParent(_gridRig, false);
            var gridLineSprite = showGridLines ? GetGridLineSprite() : null;

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (_voidCells[x, y]) continue;

                var go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(_tilesRoot, false);
                go.transform.position = CellToWorld(new Vector2Int(x, y)) + positionOffset;

                // Migração XY→XZ (2026-07-20): topo deitado no XZ (ver BuildSingleTile).
                var topGo = new GameObject("Top");
                topGo.transform.SetParent(go.transform, false);
                topGo.transform.localPosition = Vector3.zero;
                topGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // deita no XZ (topo p/ câmera)
                topGo.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);

                var sr = topGo.AddComponent<SpriteRenderer>();
                Sprite tileSprite = null;
                if (useDebugGridSprite)
                    tileSprite = DebugGridSprite.CreateGridDebugSprite();
                else if (useAtlasSprites && tileDatabase != null)
                    tileSprite = tileDatabase.GetTile(_terrainNames[x, y]);
                if (tileSprite == null) tileSprite = _blockSprite;

                sr.sprite = tileSprite;
                sr.sortingOrder = (x + y) * 4;
                sr.color = Color.white;
                _tiles[x, y] = sr;
                _baseColors[x, y] = Color.white;

                if (gridLineSprite != null)
                {
                    var lineGo = new GameObject("GridLine");
                    lineGo.transform.SetParent(go.transform, false);
                    var lineSr = lineGo.AddComponent<SpriteRenderer>();
                    lineSr.sprite = gridLineSprite;
                    lineSr.sortingOrder = sr.sortingOrder + 2;
                    lineSr.color = gridLineColor;
                }

                // Adicionar faces laterais se tile está empilhado
                int h = _heights[x, y];
                if (h > 0)
                    UpdateSideFaces(x, y, h);

            }

        }

        public void Build()
        {
            if (sourceMap != null) { width = sourceMap.width; height = sourceMap.height; }

            _tiles       = new SpriteRenderer[width, height];
            _objectSprites = new SpriteRenderer[width, height];
            _baseColors  = new Color[width, height];
            _heights     = new int[width, height];
            _terrainNames = new string[width, height];
            _objectNames  = new string[width, height];
            _voidCells   = new bool[width, height];

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (sourceMap != null)
                {
                    _heights[x, y]     = sourceMap.HeightAt(x, y);
                    _terrainNames[x, y] = sourceMap.TerrainAt(x, y);
                    _objectNames[x, y]  = sourceMap.ObjectAt(x, y);
                    _voidCells[x, y]   = sourceMap.IsVoid(x, y);
                }
                else
                {
                    _heights[x, y]     = (x >= 7 && x <= 11 && y >= 7 && y <= 11) ? 1 : 0;
                    _terrainNames[x, y] = PickTileVariant(x, y, _heights[x, y]);
                    _voidCells[x, y]   = false;
                }
            }

            _tilesRoot = new GameObject("Tiles").transform;
            if (_gridRig == null)
            {
                var center = CellToWorld(new Vector2Int(width / 2, height / 2));
                var rigGo = new GameObject("GridRig");
                rigGo.transform.position = center;
                _gridRig = rigGo.transform;
            }
            _tilesRoot.SetParent(_gridRig, false);
            var gridLineSprite = showGridLines ? GetGridLineSprite() : null;

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (_voidCells[x, y]) continue;

                var go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(_tilesRoot, false);
                go.transform.position = CellToWorld(new Vector2Int(x, y)) + positionOffset;

                // Migração XY→XZ (2026-07-20): topo deitado no XZ (ver BuildSingleTile).
                var topGo = new GameObject("Top");
                topGo.transform.SetParent(go.transform, false);
                topGo.transform.localPosition = Vector3.zero;
                topGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // deita no XZ (topo p/ câmera)
                topGo.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);

                var sr = topGo.AddComponent<SpriteRenderer>();
                Sprite tileSprite = null;
                if (useDebugGridSprite)
                    tileSprite = DebugGridSprite.CreateGridDebugSprite();
                else if (useAtlasSprites && tileDatabase != null)
                    tileSprite = tileDatabase.GetTile(_terrainNames[x, y]);
                if (tileSprite == null) tileSprite = _blockSprite;

                sr.sprite = tileSprite;
                sr.sortingOrder = (x + y) * 4;
                sr.color = Color.white;
                _tiles[x, y] = sr;
                _baseColors[x, y] = Color.white;

                // Grid lines overlay
                if (gridLineSprite != null)
                {
                    var lineGo = new GameObject("GridLine");
                    lineGo.transform.SetParent(go.transform, false);
                    var lineSr = lineGo.AddComponent<SpriteRenderer>();
                    lineSr.sprite = gridLineSprite;
                    lineSr.sortingOrder = sr.sortingOrder + 2;
                    lineSr.color = gridLineColor;
                }

                // Adicionar faces laterais se tile está empilhado
                int h = _heights[x, y];
                if (h > 0)
                    UpdateSideFaces(x, y, h);
            }

            // Parte A: sincronizar camada de colisão/ocupação com o terreno recém-construído
            if (_collision != null) _collision.SyncFrom(this);

            int raised = 0;
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (_heights[x, y] > 0) raised++;
            Debug.Log($"[Grid:Build] width={width} height={height} célulasComAltura={raised}");
        }

        // ---- Cliff: faces de penhasco nas bordas do platô elevado ----
        private void PlaceCliffFaces(Transform parent)
        {
            if (tileDatabase == null) return;
            var cliffSW = tileDatabase.GetTile("grass_cliff_SO"); // BL — borda sul
            var cliffSE = tileDatabase.GetTile("grass_cliff_SE"); // BR — borda leste

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int h = HeightAt(x, y);
                if (h <= 0) continue;

                // Borda SUL: (x,y) elevado → (x, y+1) plano
                if (y + 1 < height && HeightAt(x, y + 1) < h && cliffSW != null)
                    SpawnCliff(parent, x, y, cliffSW, (x + y + 1) * 4 + 1);

                // Borda LESTE: (x,y) elevado → (x+1, y) plano
                if (x + 1 < width && HeightAt(x + 1, y) < h && cliffSE != null)
                    SpawnCliff(parent, x, y, cliffSE, (x + 1 + y) * 4 + 1);
            }
        }

        private void SpawnCliff(Transform parent, int x, int y, Sprite sprite, int sortOrder)
        {
            var go = new GameObject($"Cliff_{x}_{y}_{sprite.name}");
            go.transform.SetParent(parent, false);
            go.transform.position = CellToWorld(new Vector2Int(x, y)) + positionOffset;
            // Migração XY→XZ (2026-07-20): cliff é topo de tile → deita no XZ.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortOrder;
        }

        // ---- Debug: troca sprites em tempo real sem reconstruir ----
        private void Update()
        {
            if (useDebugGridSprite != _lastDebugState)
            {
                _lastDebugState = useDebugGridSprite;
                ApplyDebugSprites();
            }

            // Caminho 3 (2026-07-14) + Migração XY→XZ (2026-07-20): lerp da rotação
            // do GRID em torno do eixo Y do MUNDO (perpendicular ao chão XZ), snap de
            // 90° suave. A câmera fica parada inclinada → "chão rodando" em 2.5D.
            if (_gridRig != null)
            {
                float k = 1f - Mathf.Exp(-GRID_ROT_SPEED * Time.deltaTime);
                _gridRot = Mathf.Lerp(_gridRot, _gridRotTarget, k);
                _gridRig.rotation = Quaternion.Euler(0f, _gridRot, 0f);
            }
        }

        /// <summary>
        /// Vira o MAPA em um passo de 90° (clockwise=true → +90, false → -90).
        /// Caminho 3: a rotação é aplicada no _gridRig (eixo Z do mundo), NÃO na
        /// câmera. As unidades devem re-derivar o facing via UnitRegistry.
        /// </summary>
        public void SetGridRotation(bool clockwise)
        {
            _gridRotTarget += clockwise ? 90f : -90f;
        }

        private void ApplyDebugSprites()
        {
            if (_tiles == null) return;

            Sprite debugSprite = null;
            if (useDebugGridSprite)
                debugSprite = DebugGridSprite.CreateGridDebugSprite();

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (_tiles[x, y] == null) continue;

                Sprite newSprite = useDebugGridSprite ? debugSprite : null;
                if (newSprite == null && useAtlasSprites && tileDatabase != null)
                    newSprite = tileDatabase.GetTile(_terrainNames[x, y]);
                _tiles[x, y].sprite = newSprite != null ? newSprite : _blockSprite;
                _tiles[x, y].color = Color.white;
            }
        }

        // Seleciona variante de grama baseado em posição (determinístico, distribuição uniforme).
        // Retorna o NOME do sprite (full=plano, half=empilhado).
        private string PickTileVariant(int x, int y, int tileHeight)
        {
            string[] variants = tileHeight > 0 ? new[]{ "grass_tile_half" } : new[]{ "grass_tile_full" };
            int hash = x * 1543 ^ y * 9547 ^ (x * y) * 71;
            hash ^= hash >> 16;
            int idx = ((hash % variants.Length) + variants.Length) % variants.Length;
            return variants[idx];
        }

        // ---- Conversões isométricas ----
        // Helper: rotação do mapa NÃO é aplicada no grid (Plano A: a câmera gira).
        // Mantido apenas como identidade para não quebrar CellToWorld/WorldToCell.
        private Vector2 RotateWorld(float x, float y)
        {
            return new Vector2(x, y);
        }

        public Vector3 CellToWorld(Vector2Int cell)
        {
            // Migração XY→XZ (2026-07-20): chão no plano XZ, altura no Y.
            // bx = eixo X (largura do losango), bz = eixo Z (profundidade).
            float bx = (cell.x - cell.y) * _halfW;
            float bz = -(cell.x + cell.y) * _halfH;
            return new Vector3(bx, HeightAt(cell.x, cell.y) * _heightStep, bz);
        }

        public Vector3 AnchorToWorldCenter(Vector2Int anchor, int footprint = AttributeStats.DefaultFootprint)
        {
            float cx = anchor.x + (footprint - 1) * 0.5f;
            float cy = anchor.y + (footprint - 1) * 0.5f;
            // Unidade grande (3x3) fica empilhada sobre a altura MÉDIA do footprint (Regra c).
            int h = AvgHeight(anchor, footprint);
            float bx = (cx - cy) * _halfW;
            float bz = -(cx + cy) * _halfH;
            return new Vector3(bx, h * _heightStep, bz);
        }

        /// <summary>Altura média das células do footprint (usada p/ unidades 3x3 ficarem empilhadas).</summary>
        public int AvgHeight(Vector2Int anchor, int footprint = AttributeStats.DefaultFootprint)
        {
            int sum = 0, n = 0;
            for (int x = 0; x < footprint; x++)
            for (int y = 0; y < footprint; y++)
            {
                int cx = anchor.x + x, cy = anchor.y + y;
                if (cx < 0 || cy < 0 || cx >= width || cy >= height) continue;
                sum += HeightAt(cx, cy);
                n++;
            }
            return n > 0 ? Mathf.RoundToInt((float)sum / n) : 0;
        }

        /// <summary>Ordem de desenho para uma posição de grid (maior = mais à frente).</summary>
        public int SortingFor(Vector2Int anchor, int footprint = 1)
        {
            float cx = anchor.x + (footprint - 1) * 0.5f;
            float cy = anchor.y + (footprint - 1) * 0.5f;
            return Mathf.RoundToInt((cx + cy) * 4f);
        }

        // Converte world -> cell base (0°), depois aplica rotação inversa do mapa.
        // Migração XY→XZ (2026-07-20): o chão é XZ, então lemos world.x / world.z.
        // CORREÇÃO (2026-07-20): InverseTransformPoint devolve o ponto relativo ao
        // pivô do _gridRig (centro do grid), mas CellToWorld é definido relativo à
        // ORIGEM e os tiles são posicionados em world = CellToWorld(cell)+positionOffset.
        // Então, após tirar a rotação do rig, devolvemos o ponto à origem somando a
        // posição do pivô e subtraindo o offset. Sem isso, o cell calculado saía
        // deslocado em ~centro/halfH (selection aparecia no tile errado / clampada).
        private Vector2Int WorldToCellBase(Vector3 world, bool clamp)
        {
            Vector3 local;
            if (_gridRig != null)
            {
                local = _gridRig.InverseTransformPoint(world);
                local += _gridRig.position - positionOffset;
            }
            else
            {
                local = world - positionOffset;
            }

            float ux = local.x;
            float uz = local.z;
            float a = ux / _halfW;          // col - row (base 0°)
            float b = -uz / _halfH;         // col + row (base 0°)
            float col = (a + b) * 0.5f;
            float row = (b - a) * 0.5f;
            if (clamp)
                return new Vector2Int(
                    Mathf.Clamp(Mathf.RoundToInt(col), 0, width - 1),
                    Mathf.Clamp(Mathf.RoundToInt(row), 0, height - 1));
            return new Vector2Int(Mathf.RoundToInt(col), Mathf.RoundToInt(row));
        }

        public Vector2Int WorldToCell(Vector3 world)
        {
            return WorldToCellBase(world, true);
        }

        // Migração XY→XZ (2026-07-20): com a câmera inclinada (iso), ScreenToWorldPoint
        // numa distância fixa NÃO acerta o chão. Fazemos raycast do mouse contra o
        // plano do chão (y = 0 do mundo) e devolvemos o ponto nesse plano. O ponto já
        // vem rotacionado pelo _gridRig (tiles são filhos dele); WorldToCellBase desfaz.
        private static readonly Plane GroundPlane = new Plane(Vector3.up, 0f);
        public bool ScreenToGround(Camera cam, Vector2 screen, out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(screen);
            if (GroundPlane.Raycast(ray, out float dist))
            {
                worldPoint = ray.GetPoint(dist);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Converte mundo para cell SEM clamp — retorna coordenadas negativas ou >= width/height
        /// quando o clique é fora do grid. Usado pelo Sandbox para detectar expansão.
        /// </summary>
        public Vector2Int WorldToCellRaw(Vector3 world)
        {
            return WorldToCellBase(world, false);
        }

        public Vector2Int GridCenterWorld => new Vector2Int(width, height);

        // ---- Footprint / bounds (lógica inalterada) ----
        public bool IsAnchorInBounds(Vector2Int anchor, int size = AttributeStats.DefaultFootprint)
        {
            if (anchor.x < 0 || anchor.y < 0 ||
                anchor.x + size > width || anchor.y + size > height) return false;
            // Verificar se algum cell do footprint é void
            for (int dx = 0; dx < size; dx++)
                for (int dy = 0; dy < size; dy++)
                    if (IsVoid(anchor.x + dx, anchor.y + dy)) return false;
            return true;
        }

        public static bool FootprintsOverlap(Vector2Int a, int sA, Vector2Int b, int sB)
            => a.x < b.x + sB && b.x < a.x + sA && a.y < b.y + sB && b.y < a.y + sA;

        public static bool FootprintsOverlap(Vector2Int a, Vector2Int b, int size = AttributeStats.DefaultFootprint)
            => FootprintsOverlap(a, size, b, size);

        /// <summary>True se QUALQUER unidade (exceto ignore) ocupa o cell de footprint size.</summary>
        public static bool FootprintsOverlapAny(Vector2Int cell, int size, Unit ignore = null)
        {
            foreach (var u in UnityEngine.Object.FindObjectsOfType<Unit>())
            {
                if (u == ignore || u.IsDead) continue;
                if (FootprintsOverlap(cell, size, u.anchor, u.stats.Footprint)) return true;
            }
            return false;
        }

        public static int FootprintGap(Vector2Int a, int sA, Vector2Int b, int sB)
        {
            int hGap = Mathf.Max(0, Mathf.Max(a.x - (b.x + sB - 1), b.x - (a.x + sA - 1)));
            int vGap = Mathf.Max(0, Mathf.Max(a.y - (b.y + sB - 1), b.y - (a.y + sA - 1)));
            return Mathf.Max(hGap, vGap);
        }

        public static int FootprintGap(Vector2Int a, Vector2Int b, int size = AttributeStats.DefaultFootprint)
            => FootprintGap(a, size, b, size);

        public static bool InAttackRange(Vector2Int attacker, int sA, Vector2Int target, int sB, int range)
            => FootprintGap(attacker, sA, target, sB) <= range;

        public MapData InsertLine(bool column, int index, int copyFrom)
        {
            int newW = column ? width + 1 : width;
            int newH = column ? height : height + 1;
            var data = MapData.CreateEmpty(newW, newH);

            for (int x = 0; x < newW; x++)
            for (int y = 0; y < newH; y++)
            {
                int sx = column && x >= index ? x - 1 : x;
                int sy = !column && y >= index ? y - 1 : y;
                if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                int dx = x, dy = y;

                data.terrainNames[data.Flat(dx, dy)] = _terrainNames[sx, sy];
                data.heights[data.Flat(dx, dy)] = _heights[sx, sy];
                data.voidCells[data.Flat(dx, dy)] = _voidCells[sx, sy];
            }

            if (column)
            {
                int src = Mathf.Clamp(copyFrom, 0, height - 1);
                for (int y = 0; y < newH; y++)
                {
                    data.terrainNames[data.Flat(index, y)] = _terrainNames[Mathf.Clamp(index, 0, width - 1), y >= height ? height - 1 : y];
                    data.heights[data.Flat(index, y)] = _heights[Mathf.Clamp(index, 0, width - 1), y >= height ? height - 1 : y];
                    data.voidCells[data.Flat(index, y)] = _voidCells[Mathf.Clamp(index, 0, width - 1), y >= height ? height - 1 : y];
                }
            }
            else
            {
                for (int x = 0; x < newW; x++)
                {
                    data.terrainNames[data.Flat(x, index)] = _terrainNames[x >= width ? width - 1 : x, Mathf.Clamp(index, 0, height - 1)];
                    data.heights[data.Flat(x, index)] = _heights[x >= width ? width - 1 : x, Mathf.Clamp(index, 0, height - 1)];
                    data.voidCells[data.Flat(x, index)] = _voidCells[x >= width ? width - 1 : x, Mathf.Clamp(index, 0, height - 1)];
                }
            }

            return data;
        }

        public List<Vector2Int> GetReachableAnchors(Vector2Int from, int budget, int selfFootprint, List<Unit> blockerUnits)
        {
            var result = new List<Vector2Int>();
            for (int dx = -budget; dx <= budget; dx++)
            for (int dy = -budget; dy <= budget; dy++)
            {
                var anchor = new Vector2Int(from.x + dx, from.y + dy);
                if (!IsAnchorInBounds(anchor, selfFootprint)) continue;
                // Regra (b): parede de altura — só vizinhos adjacentes checam passo.
                // (célula distante passa pela checagem de cada passo no pathfinding real,
                //  mas p/ highlight de alcance usamos o passo direto adjacente.)
                if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1 && (dx != 0 || dy != 0))
                {
                    if (!CanStepBetween(from, anchor)) continue;
                }
                bool blocked = false;
                foreach (var u in blockerUnits)
                {
                    if (u.anchor == from) continue;
                    if (FootprintsOverlap(anchor, selfFootprint, u.anchor, u.stats.Footprint))
                    { blocked = true; break; }
                }
                if (!blocked) result.Add(anchor);
            }
            return result;
        }

        // Regra (b): pode-se mover de 'from' para 'to' (vizinho adjacente)?
        // Bloqueia se a diferença de altura > maxStepClimb, A MENOS QUE haja
        // rampa direcional ligando os 2 níveis (ponte de altura, Sugestão 2).
        public bool CanStepBetween(Vector2Int from, Vector2Int to)
        {
            // Objeto parede na célula de destino → bloqueia sempre.
            var objTo = GetObjectName(to.x, to.y);
            if (!string.IsNullOrEmpty(objTo) && IsObjectWall(objTo)) return false;
            var objFrom = GetObjectName(from.x, from.y);
            if (!string.IsNullOrEmpty(objFrom) && IsObjectWall(objFrom)) return false;

            int dh = GetHeight(to.x, to.y) - GetHeight(from.x, from.y);
            if (Mathf.Abs(dh) <= MaxStepClimb) return true;

            // Rampa na direção certa faz ponte (ignora limite de step).
            string dir = DirBetween(from, to);
            if (HasRampToward(from, dir) || HasRampToward(to, DirOpposite(dir)))
                return true;

            return false;
        }

        private bool HasRampToward(Vector2Int cell, string dir)
        {
            var t = GetTerrainName(cell.x, cell.y);
            var def = tileDatabase != null ? tileDatabase.GetDef(t) : null;
            return def != null && def.kind == TileKind.Ramp && def.dir == dir;
        }

        private static string DirBetween(Vector2Int from, Vector2Int to)
        {
            int dx = to.x - from.x;
            int dy = to.y - from.y;
            // Iso: adjacency diagonal → 4 direções
            if (dx < 0 && dy < 0) return "NO";
            if (dx > 0 && dy < 0) return "NE";
            if (dx < 0 && dy > 0) return "SO";
            if (dx > 0 && dy > 0) return "SE";
            return "";
        }

        private static string DirOpposite(string dir)
        {
            if (dir == "NO") return "SE";
            if (dir == "NE") return "SO";
            if (dir == "SO") return "NE";
            if (dir == "SE") return "NO";
            return "";
        }

        private int MaxStepClimb => RuntimeTuning.Active != null ? RuntimeTuning.Active.maxStepClimb : 1;

        public List<Vector2Int> GetReachableAnchors(Vector2Int from, int budget, IEnumerable<Vector2Int> blockers)
        {
            var result = new List<Vector2Int>();
            for (int dx = -budget; dx <= budget; dx++)
            for (int dy = -budget; dy <= budget; dy++)
            {
                var anchor = new Vector2Int(from.x + dx, from.y + dy);
                if (!IsAnchorInBounds(anchor)) continue;
                bool blocked = false;
                foreach (var b in blockers)
                {
                    if (b == from) continue;
                    if (FootprintsOverlap(anchor, b)) { blocked = true; break; }
                }
                if (!blocked) result.Add(anchor);
            }
            return result;
        }

        // ---- Highlight (tint do tile central do anchor + contorno estilizado) ----
        public void HighlightAnchors(IEnumerable<Vector2Int> anchors)
            => HighlightAnchors(anchors, highlightColor);

        public void HighlightAnchors(IEnumerable<Vector2Int> anchors, Color color, bool clearFirst = true)
        {
            if (clearFirst) ClearHighlight();
            foreach (var a in anchors)
            {
                var center = new Vector2Int(a.x + 1, a.y + 1);
                if (center.x < 0 || center.y < 0 || center.x >= width || center.y >= height) continue;
                SetTileColor(center, color);
                if (!_highlighted.Contains(center)) _highlighted.Add(center);
                AddHighlightBorder(center, color);
            }
        }

        // Overlay de contorno (losango) sobre o tile destacado — mesmo "look" de borda
        // dos botões pg-button (2px, cor da borda tema). Mantém a cor de SEMÂNTICA do
        // tile (verde move / ouro bônus) no tint; o contorno é a borda estilizada.
        private void AddHighlightBorder(Vector2Int cell, Color highlight)
        {
            if (_tiles == null || _tiles[cell.x, cell.y] == null) return;
            if (_highlightBorders.TryGetValue(cell, out var existing)) { existing.color = BorderTint(highlight); return; }

            var lineGo = new GameObject($"HLBorder_{cell.x}_{cell.y}");
            lineGo.transform.SetParent(_tiles[cell.x, cell.y].transform, false);
            var lineSr = lineGo.AddComponent<SpriteRenderer>();
            lineSr.sprite = GetGridLineSprite();
            lineSr.sortingOrder = _tiles[cell.x, cell.y].sortingOrder + 3;
            lineSr.color = BorderTint(highlight);
            _highlightBorders[cell] = lineSr;
        }

        // Cor da borda do contorno: usa a borda tema (rgb 42,58,106) clareada levemente
        // para destacar sobre o tint do tile. Mantém identidade visual dos menus.
        private static Color BorderTint(Color highlight)
        {
            // Borda tema base (PangeaTheme --pg-panel-border / --pg-panel-highlight)
            Color border = new Color(0.42f, 0.58f, 1.0f); // azul claro da borda UI
            // Se o highlight for muito claro, escurece a borda p/ contraste
            float lum = 0.299f * highlight.r + 0.587f * highlight.g + 0.114f * highlight.b;
            if (lum > 0.7f) border = new Color(0.16f, 0.23f, 0.42f);
            return border;
        }

        public void ClearHighlight()
        {
            foreach (var c in _highlighted) SetTileColor(c, _baseColors[c.x, c.y]);
            foreach (var kv in _highlightBorders)
            {
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            }
            _highlightBorders.Clear();
            _highlighted.Clear();
        }

        private void SetTileColor(Vector2Int cell, Color c)
        {
            if (_tiles != null && _tiles[cell.x, cell.y] != null)
                _tiles[cell.x, cell.y].color = c;
        }
    }
}
