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
        // 0 = grama plana (cubo verde limpo); 3 = grama alta (morrinho)
        private static readonly int[] GrassVariants         = { 0 };
        private static readonly int[] ElevatedGrassVariants = { 3 };

        [Header("Tile Alignment (debug)")]
        public Vector2 spritePivot = new Vector2(0.5f, 0.75f);  // Pivot do sprite no losango
        public Vector3 positionOffset = Vector3.zero;  // Offset de posição dos tiles
        public float spriteScale = 1f;  // Scale do sprite
        public bool useDebugGridSprite = false;  // Ativar sprite de debug com grid

        [Header("Grid Lines")]
        public bool showGridLines = true;
        public Color gridLineColor = new Color(1f, 1f, 1f, 0.25f);

        private bool _lastDebugState = false;

        public MapData sourceMap;        // se != null, Build() usa este mapa em vez do platô hardcoded
        private int[,] _tileIndices;     // índice do atlas por célula (para edição/exportação)
        private bool[,] _voidCells;      // cells vazias (não renderiza, sem colisão)

        private SpriteRenderer[,] _tiles;
        private Color[,] _baseColors;
        private int[,] _heights;
        private Sprite _blockSprite;
        private static Sprite _gridLineSprite;
        private Transform _tilesRoot;

        private float _halfW = 1.0f;   // meia-largura do losango (unidades)
        private float _halfH = 0.5f;   // meia-altura do losango (unidades)
        private float _heightStep = 0.5f;

        private readonly List<Vector2Int> _highlighted = new List<Vector2Int>();

        public float HalfW => _halfW;
        public float HalfH => _halfH;
        public Sprite BlockSprite => _blockSprite;

        private void Awake()
        {
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

            // Gerar sprite fallback enquanto TileDatabase está em debug
            BuildBlockSprite();
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

        public int GetTileIndex(int x, int y)
        {
            if (_tileIndices == null) return 0;
            x = Mathf.Clamp(x, 0, width - 1); y = Mathf.Clamp(y, 0, height - 1);
            return _tileIndices[x, y];
        }

        public int GetHeight(int x, int y)
        {
            if (_heights == null) return 0;
            x = Mathf.Clamp(x, 0, width - 1); y = Mathf.Clamp(y, 0, height - 1);
            return _heights[x, y];
        }

        // Edita uma célula em runtime (usado pelo editor de terreno do Sandbox).
        // Atenção: o parâmetro de altura é "cellHeight" (NÃO "height") para não sombrear
        // o campo da classe "height" (largura/altura do grid) usado na checagem de bounds.
        public void SetCell(int x, int y, int tileIndex, int cellHeight, bool isVoid = false)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int oldHeight = _heights[x, y];
            _heights[x, y] = cellHeight;
            _tileIndices[x, y] = tileIndex;
            _voidCells[x, y] = isVoid;
            var sr = _tiles != null ? _tiles[x, y] : null;
            if (isVoid)
            {
                // Destruir tile se existe
                if (sr != null) { Destroy(sr.gameObject); _tiles[x, y] = null; }
                return;
            }
            if (sr == null)
            {
                // Recriar tile (era void, agora não é mais)
                BuildSingleTile(x, y);
                return;
            }
            sr.transform.position = CellToWorld(new Vector2Int(x, y)) + positionOffset;
            var sprite = tileDatabase != null ? tileDatabase.GetTile(tileIndex) : null;
            sr.sprite = sprite ?? _blockSprite;

            // Atualizar faces laterais se altura mudou
            if (cellHeight != oldHeight)
                UpdateSideFaces(x, y, cellHeight);
        }

        /// <summary>
        /// Atualiza as faces laterais de um tile empilhado.
        /// Duas faixas retangulares (esquerda escuro + direita médio) abaixo do tile.
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

            float sideH = height * _heightStep;
            float halfW = _halfW;
            int order = _tiles[x, y].sortingOrder - 10;
            Sprite px = GetPixelSprite();

            // Face esquerda (metade esquerda do tile, cor mais escura)
            var goL = new GameObject("SideFaceL");
            goL.transform.SetParent(tileGo.transform, false);
            goL.transform.localPosition = new Vector3(-halfW * 0.5f, -sideH * 0.5f, 0f);
            goL.transform.localScale = new Vector3(halfW, sideH, 1f);
            var srL = goL.AddComponent<SpriteRenderer>();
            srL.sprite = px;
            srL.color = Tuning.Get().sideFaceLeftColor;
            srL.sortingOrder = order;

            // Face direita (metade direita do tile, cor média)
            var goR = new GameObject("SideFaceR");
            goR.transform.SetParent(tileGo.transform, false);
            goR.transform.localPosition = new Vector3(halfW * 0.5f, -sideH * 0.5f, 0f);
            goR.transform.localScale = new Vector3(halfW, sideH, 1f);
            var srR = goR.AddComponent<SpriteRenderer>();
            srR.sprite = px;
            srR.color = Tuning.Get().sideFaceRightColor;
            srR.sortingOrder = order;
        }

        private static Sprite _pixelSprite;
        private static Sprite GetPixelSprite()
        {
            if (_pixelSprite != null) return _pixelSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f), 1f);
            return _pixelSprite;
        }

        private void BuildSingleTile(int x, int y)
        {
            if (_tilesRoot == null)
            {
                _tilesRoot = new GameObject("Tiles").transform;
                _tilesRoot.SetParent(transform, false);
            }

            var go = new GameObject($"Tile_{x}_{y}");
            go.transform.SetParent(_tilesRoot, false);
            go.transform.position = CellToWorld(new Vector2Int(x, y)) + positionOffset;
            go.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            Sprite tileSprite = null;
            if (useDebugGridSprite)
                tileSprite = DebugGridSprite.CreateGridDebugSprite();
            else if (useAtlasSprites && tileDatabase != null)
                tileSprite = tileDatabase.GetTile(_tileIndices[x, y]);
            if (tileSprite == null) tileSprite = _blockSprite;

            sr.sprite = tileSprite;
            sr.sortingOrder = (x + y) * 4;
            sr.color = Color.white;
            _tiles[x, y] = sr;
            _baseColors[x, y] = Color.white;

            if (showGridLines)
            {
                var lineGo = new GameObject("GridLine");
                lineGo.transform.SetParent(go.transform, false);
                var lineSr = lineGo.AddComponent<SpriteRenderer>();
                lineSr.sprite = GetGridLineSprite();
                lineSr.sortingOrder = sr.sortingOrder + 2;
                lineSr.color = gridLineColor;
            }

            // Adicionar faces laterais se tile já está empilhado
            int h = _heights[x, y];
            if (h > 0)
                UpdateSideFaces(x, y, h);
        }

        // Copia o terreno atual para um MapData (flatten). units NÃO é tocado aqui.
        public void ExportTerrain(MapData map)
        {
            map.width = width; map.height = height;
            map.tileIndices = new int[width * height];
            map.heights     = new int[width * height];
            map.voidCells   = new bool[width * height];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int f = x * height + y;             // MESMA fórmula de MapData.Flat
                map.tileIndices[f] = _tileIndices[x, y];
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
            newMap.tileIndices = new int[newW * newH];
            newMap.heights = new int[newW * newH];
            newMap.voidCells = new bool[newW * newH];

            // Preencher tudo como void
            for (int i = 0; i < newW * newH; i++)
            {
                newMap.voidCells[i] = true;
                newMap.heights[i] = 0;
                newMap.tileIndices[i] = 0;
            }

            // Copiar dados existentes com offset
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int nx = x + offX;
                int ny = y + offY;
                int nf = nx * newH + ny;
                int of = x * height + y;
                newMap.tileIndices[nf] = _tileIndices[x, y];
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
            _baseColors = new Color[width, height];
            _heights = new int[width, height];
            _tileIndices = new int[width, height];
            _voidCells = new bool[width, height];

            // Copiar dados do MapData
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                _heights[x, y] = map.HeightAt(x, y);
                _tileIndices[x, y] = map.TileAt(x, y);
                _voidCells[x, y] = map.IsVoid(x, y);
            }

            // Reconstruir sprites
            _tilesRoot = new GameObject("Tiles").transform;
            _tilesRoot.SetParent(transform, false);
            var gridLineSprite = showGridLines ? GetGridLineSprite() : null;

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (_voidCells[x, y]) continue;

                var go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(_tilesRoot, false);
                go.transform.position = CellToWorld(new Vector2Int(x, y)) + positionOffset;
                go.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                Sprite tileSprite = null;
                if (useDebugGridSprite)
                    tileSprite = DebugGridSprite.CreateGridDebugSprite();
                else if (useAtlasSprites && tileDatabase != null)
                    tileSprite = tileDatabase.GetTile(_tileIndices[x, y]);
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
            _baseColors  = new Color[width, height];
            _heights     = new int[width, height];
            _tileIndices = new int[width, height];
            _voidCells   = new bool[width, height];

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (sourceMap != null)
                {
                    _heights[x, y]     = sourceMap.HeightAt(x, y);
                    _tileIndices[x, y] = sourceMap.TileAt(x, y);
                    _voidCells[x, y]   = sourceMap.IsVoid(x, y);
                }
                else
                {
                    _heights[x, y]     = (x >= 7 && x <= 11 && y >= 7 && y <= 11) ? 1 : 0;
                    _tileIndices[x, y] = PickTileVariant(x, y, _heights[x, y]);
                    _voidCells[x, y]   = false;
                }
            }

            _tilesRoot = new GameObject("Tiles").transform;
            _tilesRoot.SetParent(transform, false);
            var gridLineSprite = showGridLines ? GetGridLineSprite() : null;

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (_voidCells[x, y]) continue;

                var go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(_tilesRoot, false);
                go.transform.position = CellToWorld(new Vector2Int(x, y)) + positionOffset;
                go.transform.localScale = new Vector3(spriteScale, spriteScale, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                Sprite tileSprite = null;
                if (useDebugGridSprite)
                    tileSprite = DebugGridSprite.CreateGridDebugSprite();
                else if (useAtlasSprites && tileDatabase != null)
                    tileSprite = tileDatabase.GetTile(_tileIndices[x, y]);
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
        }

        // ---- Cliff: faces de penhasco nas bordas do platô elevado ----
        private void PlaceCliffFaces(Transform parent)
        {
            if (tileDatabase == null) return;
            var cliffSW = tileDatabase.GetCliffFace(0); // BL — borda sul
            var cliffSE = tileDatabase.GetCliffFace(1); // BR — borda leste

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
                    newSprite = tileDatabase.GetTile(_tileIndices[x, y]);
                _tiles[x, y].sprite = newSprite != null ? newSprite : _blockSprite;
                _tiles[x, y].color = Color.white;
            }
        }

        // Seleciona variante de tile baseado em posição (determinístico, distribuição uniforme)
        private int PickTileVariant(int x, int y, int tileHeight)
        {
            int[] variants = tileHeight > 0 ? ElevatedGrassVariants : GrassVariants;
            int hash = x * 1543 ^ y * 9547 ^ (x * y) * 71;
            hash ^= hash >> 16;
            int idx = ((hash % variants.Length) + variants.Length) % variants.Length;
            return variants[idx];
        }

        // ---- Conversões isométricas ----
        public Vector3 CellToWorld(Vector2Int cell)
        {
            float wx = (cell.x - cell.y) * _halfW;
            float wy = -(cell.x + cell.y) * _halfH + HeightAt(cell.x, cell.y) * _heightStep;
            return new Vector3(wx, wy, 0f);
        }

        public Vector3 AnchorToWorldCenter(Vector2Int anchor, int footprint = AttributeStats.DefaultFootprint)
        {
            float cx = anchor.x + (footprint - 1) * 0.5f;
            float cy = anchor.y + (footprint - 1) * 0.5f;
            int h = HeightAt(Mathf.RoundToInt(cx), Mathf.RoundToInt(cy));
            float wx = (cx - cy) * _halfW;
            float wy = -(cx + cy) * _halfH + h * _heightStep;
            return new Vector3(wx, wy, 0f);
        }

        /// <summary>Ordem de desenho para uma posição de grid (maior = mais à frente).</summary>
        public int SortingFor(Vector2Int anchor, int footprint = 1)
        {
            float cx = anchor.x + (footprint - 1) * 0.5f;
            float cy = anchor.y + (footprint - 1) * 0.5f;
            return Mathf.RoundToInt((cx + cy) * 4f);
        }

        public Vector2Int WorldToCell(Vector3 world)
        {
            float a = world.x / _halfW;        // col - row
            float b = -world.y / _halfH;       // col + row  (ignora elevação)
            float col = (a + b) * 0.5f;
            float row = (b - a) * 0.5f;
            return new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt(col), 0, width - 1),
                Mathf.Clamp(Mathf.RoundToInt(row), 0, height - 1));
        }

        /// <summary>
        /// Converte mundo para cell SEM clamp — retorna coordenadas negativas ou >= width/height
        /// quando o clique é fora do grid. Usado pelo Sandbox para detectar expansão.
        /// </summary>
        public Vector2Int WorldToCellRaw(Vector3 world)
        {
            float a = world.x / _halfW;
            float b = -world.y / _halfH;
            float col = (a + b) * 0.5f;
            float row = (b - a) * 0.5f;
            return new Vector2Int(Mathf.RoundToInt(col), Mathf.RoundToInt(row));
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

                data.tileIndices[data.Flat(dx, dy)] = _tileIndices[sx, sy];
                data.heights[data.Flat(dx, dy)] = _heights[sx, sy];
                data.voidCells[data.Flat(dx, dy)] = _voidCells[sx, sy];
            }

            if (column)
            {
                int src = Mathf.Clamp(copyFrom, 0, height - 1);
                for (int y = 0; y < newH; y++)
                {
                    data.tileIndices[data.Flat(index, y)] = _tileIndices[Mathf.Clamp(index, 0, width - 1), y >= height ? height - 1 : y];
                    data.heights[data.Flat(index, y)] = _heights[Mathf.Clamp(index, 0, width - 1), y >= height ? height - 1 : y];
                    data.voidCells[data.Flat(index, y)] = _voidCells[Mathf.Clamp(index, 0, width - 1), y >= height ? height - 1 : y];
                }
            }
            else
            {
                for (int x = 0; x < newW; x++)
                {
                    data.tileIndices[data.Flat(x, index)] = _tileIndices[x >= width ? width - 1 : x, Mathf.Clamp(index, 0, height - 1)];
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

        // ---- Highlight (tint do tile central do anchor) ----
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
            }
        }

        public void ClearHighlight()
        {
            foreach (var c in _highlighted) SetTileColor(c, _baseColors[c.x, c.y]);
            _highlighted.Clear();
        }

        private void SetTileColor(Vector2Int cell, Color c)
        {
            if (_tiles != null && _tiles[cell.x, cell.y] != null)
                _tiles[cell.x, cell.y].color = c;
        }
    }
}
