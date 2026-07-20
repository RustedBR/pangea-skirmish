using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PangeaSkirmish.UI;

namespace PangeaSkirmish
{
    /// <summary>
    /// Editor de mapas (Modo Sandbox). 3 fases: Terreno (pinta tiles) → Aliados → Inimigos
    /// (posiciona/edita unidades). Salva o cenário completo via MapStorage. Pode abrir um
    /// mapa existente para editar (RuntimeSandbox.MapToEdit).
    /// </summary>
    public class SandboxController : MonoBehaviour
    {
        public enum Phase { Terrain, Allies, Enemies }

        private GridManager _grid;
        private Camera _cam;
        private SandboxHUD _hud;
        private IsoConfig _iso;

        private MapData _map;                       // mapa em construção (terreno editado no grid)
        private readonly List<UnitPlacement> _placements = new();
        private readonly List<Unit> _unitVisuals = new();   // paralelo a _placements (mesmo índice)

        private Phase _phase = Phase.Terrain;
        private TileBrush _brush = null;            // pincel atual (Terrain) — inicializado em Start
        private string _spriteId = null;            // spritePath ativo (Allies/Enemies); null = nenhum selecionado
        private UnitStatBlock _activeStats = null;  // override de stats (null = stats padrão)
        private string _activeName = null;          // nome do preset ativo (null = nome do sprite)
        private int _selectedUnit = -1;             // índice em _placements p/ editar stats
        private string _mapName = "Novo Mapa";

        private SpriteRenderer _hoverCursor;

        // Pintura: selecionar → soltar para aplicar
        private bool _isSelecting;
        private Vector2Int _selectionStart;
        private bool _selectionIsShift;
        private readonly HashSet<Vector2Int> _currentSelection = new();
        private readonly List<GameObject> _previewObjects = new();

        private static Color PlayerColor => Tuning.Get().playerTeamColor;
        private static Color EnemyColor  => Tuning.Get().enemyTeamColor;

        public TileDatabase Tiles => _grid != null ? _grid.tileDatabase : null;

        private void Start()
        {
            if (AudioManager.I == null) new GameObject("AudioManager", typeof(AudioManager));
            _iso = Resources.Load<IsoConfig>("IsoConfig");
            if (_iso == null) _iso = ScriptableObject.CreateInstance<IsoConfig>();

            // Em MP: registrar no CollabMapSync (disponível após spawn do host)
            // O registro acontece após o grid ser construído (mais abaixo em Start).

            // Câmera ortográfica
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            var T = Tuning.Get();
            cam.orthographic = true;
            cam.orthographicSize = T.sandboxCameraSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = T.sandboxBackgroundColor;
            _cam = cam;

            var camCtrl = cam.gameObject.GetComponent<CameraController>();
            if (camCtrl == null) camCtrl = cam.gameObject.AddComponent<CameraController>();

            // Mapa: editar um existente (RuntimeSandbox.MapToEdit) ou criar vazio
            bool editing = RuntimeSandbox.MapToEdit != null;
            _map = editing ? RuntimeSandbox.MapToEdit
                           : MapData.CreateEmpty(T.sandboxDefaultMapSize, T.sandboxDefaultMapSize);
            RuntimeSandbox.MapToEdit = null;        // consome
            if (editing) _mapName = _map.mapName;

            int gridW = _map.width, gridH = _map.height;
            float halfW = _iso.TileUnitsW * 0.5f;
            float halfH = _iso.TileUnitsH * 0.5f;
            float centerX = ((gridW - 1) - (gridH - 1)) * 0.5f * halfW;
            float centerY = -((gridW - 1) + (gridH - 1)) * 0.5f * halfH;
            camCtrl.Configure(cam, new Vector3(centerX, centerY, 0f), cam.orthographicSize);

            // Grid
            var gridGo = new GameObject("GridManager");
            _grid = gridGo.AddComponent<GridManager>();
            _grid.sourceMap = _map;
            _grid.iso = _iso;
            _grid.width = gridW;
            _grid.height = gridH;
            _grid.Build();

            // EventSystem (necessário para input do UI Toolkit / UIDocument)
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
            }

            _hud = UI.PangeaScreen.Spawn<SandboxHUD>("SandboxHUD");
            _hud.Init(this);
            _hud.SetMapName(_mapName);

            // Recriar unidades do mapa carregado
            if (editing && _map.units != null)
            {
                foreach (var p in _map.units)
                {
                    var color = ((Team)p.team) == Team.Player ? PlayerColor : EnemyColor;
                    var u = CreateUnitVisual(p, color);
                    _placements.Add(p);
                    _unitVisuals.Add(u);
                }
            }

            CreateHoverCursor();

            if (TilePalette.Brushes.Length > 0) _brush = TilePalette.Brushes[0];

            // Em MP: pular fases Allies/Enemies (só terreno), registrar no CollabMapSync
            if (RuntimeMultiplayerSession.IsMultiplayer && !RuntimeMultiplayerSession.IsLocalContentSession)
            {
                Debug.Log($"[MP] Sandbox iniciado em modo COLABORATIVO (CollabMapSync={(CollabMapSync.Instance != null)})");
                _phase = Phase.Terrain;
                if (CollabMapSync.Instance != null)
                    CollabMapSync.Instance.RegisterSandbox(this, _map);
                else
                    Debug.LogWarning("[MP] CollabMapSync.Instance NULL no Sandbox — sincronizacao de tiles pode falhar");
                // HUD MP: botão "Finalizar Mapa" em vez de salvar
                _hud.SetMpMode(true);
            }
            else if (RuntimeMultiplayerSession.IsLocalContentSession)
            {
                // Sala loopback SOLO (criar mapa offline): mantém o fluxo de SALVAR local,
                // mas adiciona um botão "Voltar ao Menu" que encerra a sala e retorna.
                Debug.Log("[LocalContent] Sandbox iniciado em modo CRIAÇÃO LOCAL (só terreno, salvar local).");
                _phase = Phase.Terrain;
                _hud.SetLocalContentMode(true);
            }
            else Debug.Log("[MP] Sandbox iniciado em modo SINGLE-PLAYER (IsMultiplayer=false)");

            _hud.SetPhaseUI(_phase);
        }

        private void Update()
        {
            if (_cam == null || _grid == null) return;

            UpdateHoverCursor();

            if (Mouse.current == null) return;
            bool leftDown  = Mouse.current.leftButton.wasPressedThisFrame;
            bool leftHeld  = Mouse.current.leftButton.isPressed;
            bool leftUp    = Mouse.current.leftButton.wasReleasedThisFrame;
            bool rightDown = Mouse.current.rightButton.wasPressedThisFrame;
            bool shiftHeld = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

            // Só bloqueia cliques que acertaram um Button (não fundo de painel)
            if (IsOverButton())
            {
                if (leftUp && _isSelecting) CancelSelection();
                return;
            }

            Vector2Int cell = CellUnderMouse();

            switch (_phase)
            {
                case Phase.Terrain:
                    if (_brush != null)
                    {
                        // Início da seleção
                        if (leftDown)
                        {
                            Vector2Int raw = CellUnderMouse();
                            int dist = DistanceToGridEdge(raw);

                            _isSelecting = true;
                            _selectionIsShift = shiftHeld;
                            _currentSelection.Clear();

                            if (dist == 0)
                            {
                                // Dentro do grid — pinta/empilha
                                _selectionStart = raw;
                                _currentSelection.Add(raw);
                            }
                            else
                            {
                                // Fora do grid — calcular posição final após expansão.
                                // Norte (x<0 E y<0) e Noroeste (x>W E y<0) são cantos:
                                // tratamos OS DOIS eixos juntos (não só um).
                                int fx = raw.x;
                                int fy = raw.y;
                                // Eixo X: esquerda (x<0) ou direita (x>=W)
                                int offX = 0;
                                if (raw.x < 0) { fx = 0; offX = -1; }
                                else if (raw.x >= _grid.width) { fx = _grid.width; offX = 1; }
                                // Eixo Y: baixo (y<0) ou cima (y>=H)
                                int offY = 0;
                                if (raw.y < 0) { fy = 0; offY = -1; }
                                else if (raw.y >= _grid.height) { fy = _grid.height; offY = 1; }
                                // Canto norte/noroeste: ambos eixos fora → expande nos 2
                                _selectionStart = new Vector2Int(fx, fy);
                                _currentSelection.Add(_selectionStart);
                            }

                            UpdatePreview();
                            Debug.Log($"[Sandbox] Selection start: raw=({raw.x},{raw.y}) dist={dist} → ({_selectionStart.x},{_selectionStart.y}) shift={shiftHeld}");
                        }

                        // Atualizar seleção enquanto segura
                        if (_isSelecting && leftHeld)
                        {
                            _currentSelection.Clear();

                            if (_selectionIsShift)
                            {
                                // Shift+drag → losango
                                GetDiamondCells(_selectionStart, cell, _currentSelection);
                            }
                            else
                            {
                                // Pincel livre → linha reta (Bresenham)
                                GetLineCells(_selectionStart, cell, _currentSelection);
                            }

                            UpdatePreview();
                        }

                        // Aplicar pintura ao soltar
                        if (leftUp && _isSelecting)
                        {
                            Debug.Log($"[Sandbox] Applying paint to {_currentSelection.Count} tiles");
                            ApplyPaint();
                            ClearPreview();
                            _isSelecting = false;
                            _currentSelection.Clear();
                        }
                    }
                    break;
            }

            // Clique direito: debug — log de posição
            if (rightDown)
            {
                Vector2 screen = Mouse.current.position.ReadValue();
                Vector3 world;
                if (!_grid.ScreenToGround(_cam, screen, out world)) world = Vector3.zero;
                Vector2Int rawCell = _grid.WorldToCellRaw(world);
                Vector2Int clampedCell = _grid.WorldToCell(world);
                Debug.Log($"[Sandbox-DEBUG] screen=({screen.x:F0},{screen.y:F0}) world=({world.x:F2},{world.y:F2}) rawCell=({rawCell.x},{rawCell.y}) clampedCell=({clampedCell.x},{clampedCell.y}) gridW={_grid.width} gridH={_grid.height} camPos=({_cam.transform.position.x:F2},{_cam.transform.position.y:F2}) orthoSize={_cam.orthographicSize}");

                // Limpar seleção no modo terreno
                if (_phase == Phase.Terrain && _isSelecting)
                    CancelSelection();
            }

            // Clique direito (Allies/Enemies): limpa sprite ativo
            if (rightDown && (_phase == Phase.Allies || _phase == Phase.Enemies))
            {
                _spriteId = null;
                _activeStats = null;
                _activeName = null;
                _hud.SetNoClassSelected();
            }
        }

        private Vector2Int CellUnderMouse()
        {
            Vector2 screen = Mouse.current.position.ReadValue();
            Vector3 world;
            if (!_grid.ScreenToGround(_cam, screen, out world)) world = Vector3.zero;
            return _grid.WorldToCellRaw(world);
        }

        /// <summary>
        /// Calcula distância de um rawCell até a borda do grid.
        /// Retorna 0 se está dentro do grid.
        /// </summary>
        private int DistanceToGridEdge(Vector2Int raw)
        {
            if (raw.x >= 0 && raw.x < _grid.width && raw.y >= 0 && raw.y < _grid.height)
                return 0;

            int dx = raw.x < 0 ? -raw.x : (raw.x >= _grid.width ? raw.x - (_grid.width - 1) : 0);
            int dy = raw.y < 0 ? -raw.y : (raw.y >= _grid.height ? raw.y - (_grid.height - 1) : 0);
            return Mathf.Max(dx, dy);
        }

        /// <summary>Só retorna true se o ponteiro estiver sobre um elemento clicável da UI
        /// (Button/Label dentro do painel do UIDocument) — permite cliques no grid mesmo
        /// atrás de painéis/backgrounds. Usa UI Toolkit (panel.Pick), não EventSystem UGUI.</summary>
        private bool IsOverButton()
        {
            if (_hud == null || Mouse.current == null) return false;
            var panel = _hud.UiPanel;
            if (panel == null) return false;
            var pos = Mouse.current.position.ReadValue();
            var picked = panel.Pick(new Vector2(pos.x, panel.visualTree.layout.height - pos.y));
            if (picked == null) return false;
            // sobe na árvore procurando um Button (ou elemento com name de botão)
            var e = picked;
            while (e != null)
            {
                if (e is Button) return true;
                e = e.parent;
            }
            return false;
        }

        // ── PINTURA: SELECIONAR → SOLTAR PARA APLICAR ─────────────────────

        /// <summary>
        /// Calcula cells numa linha reta entre dois pontos (algoritmo de Bresenham).
        /// </summary>
        private void GetLineCells(Vector2Int from, Vector2Int to, HashSet<Vector2Int> result)
        {
            int x0 = from.x, y0 = from.y;
            int x1 = to.x, y1 = to.y;
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                result.Add(new Vector2Int(x0, y0));
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>
        /// Calcula cells num losango (diamond) entre dois pontos.
        /// Usa distância Manhattan: |2x - cx| + |2y - cy| <= raio.
        /// </summary>
        private void GetDiamondCells(Vector2Int from, Vector2Int to, HashSet<Vector2Int> result)
        {
            int cx = from.x + to.x;
            int cy = from.y + to.y;
            int radius = Mathf.Abs(to.x - from.x) + Mathf.Abs(to.y - from.y);

            int minX = Mathf.Min(from.x, to.x);
            int maxX = Mathf.Max(from.x, to.x);
            int minY = Mathf.Min(from.y, to.y);
            int maxY = Mathf.Max(from.y, to.y);

            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                int dist = Mathf.Abs(2 * x - cx) + Mathf.Abs(2 * y - cy);
                if (dist <= radius)
                    result.Add(new Vector2Int(x, y));
            }
        }

        /// <summary>
        /// Atualiza os sprites de preview visual para mostrar a seleção atual.
        /// Usa o sprite real do brush com tint verde (igual ao hover mas verde).
        /// </summary>
        private void UpdatePreview()
        {
            ClearPreview();

            bool isVoidBrush = string.IsNullOrEmpty(_brush.spriteName);

            foreach (var cell in _currentSelection)
            {
                var go = new GameObject($"Preview_{cell.x}_{cell.y}");
                go.transform.SetParent(transform, false);
                go.transform.position = _grid.CellToWorld(cell) + _grid.positionOffset;

                // Migração XY→XZ (2026-07-20): preview de tile é topo → deita no XZ.
                var topGo = new GameObject("Top");
                topGo.transform.SetParent(go.transform, false);
                topGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                topGo.transform.localScale = new Vector3(_grid.spriteScale, _grid.spriteScale, 1f);

                var sr = topGo.AddComponent<SpriteRenderer>();

                // Usar sprite real do brush (não genérico)
                if (!isVoidBrush && _grid.tileDatabase != null)
                    sr.sprite = _grid.tileDatabase.GetTile(_brush.spriteName);
                if (sr.sprite == null)
                    sr.sprite = _grid.BlockSprite;

                sr.color = Tuning.Get().paintPreviewColor; // verde semi-transparente
                sr.sortingOrder = 9999;

                _previewObjects.Add(go);
            }
        }

        /// <summary>
        /// Destrói todos os objetos de preview.
        /// </summary>
        private void ClearPreview()
        {
            foreach (var go in _previewObjects)
                if (go != null) Destroy(go);
            _previewObjects.Clear();
        }

        /// <summary>
        /// Cancela a seleção sem aplicar pintura.
        /// </summary>
        private void CancelSelection()
        {
            ClearPreview();
            _isSelecting = false;
            _currentSelection.Clear();
        }

        /// <summary>
        /// Aplica a pintura em todos os tiles da seleção.
        /// Em SP: aplica direto. Em MP: envia via RPC (convergência por eco).
        /// </summary>
        private void ApplyPaint()
        {
            bool isVoidBrush = string.IsNullOrEmpty(_brush.spriteName);
            Debug.Log($"[Sandbox:ApplyPaint] _brush.name={_brush?.name} spriteName='{_brush?.spriteName}' kind={_brush?.kind} height={_brush?.height} isVoidBrush={isVoidBrush} selection={_currentSelection.Count}");

            // Expandir grid até que TODAS as cells da seleção cabam
            bool expanded = true;
            while (expanded)
            {
                expanded = false;
                foreach (var cell in _currentSelection)
                {
                    if (cell.x < 0 || cell.y < 0 || cell.x >= _grid.width || cell.y >= _grid.height)
                    {
                        int dirX = cell.x < 0 ? -1 : (cell.x >= _grid.width ? 1 : 0);
                        int dirY = cell.y < 0 ? -1 : (cell.y >= _grid.height ? 1 : 0);

                        var tuning = RuntimeTuning.Active;
                        if (tuning != null && tuning.maxMapSize > 0)
                        {
                            int newW = _grid.width + (dirX != 0 ? 1 : 0);
                            int newH = _grid.height + (dirY != 0 ? 1 : 0);
                            if (newW > tuning.maxMapSize || newH > tuning.maxMapSize)
                                continue;
                        }

                        if (RuntimeMultiplayerSession.IsMultiplayer && CollabMapSync.Instance != null)
                        {
                            // Em MP: rotear expansão via RPC (sincroniza com outros),
                            // MAS expande localmente também (modo Sandbox local depende do grid na tela).
                            CollabMapSync.Instance.ExpandGridServerRpc(
                                _grid.width  + (dirX != 0 ? 1 : 0),
                                _grid.height + (dirY != 0 ? 1 : 0));
                        }
                        var newMap = _grid.Expand(dirX, dirY);
                        if (newMap != null) { _map = newMap; expanded = true; }
                        break;
                    }
                }

                // Em MP não expandimos localmente nesta iteração
                if (RuntimeMultiplayerSession.IsMultiplayer) break;
            }

            // Reposicionar câmera no centro do grid
            {
                float halfW = _iso.TileUnitsW * 0.5f;
                float halfH = _iso.TileUnitsH * 0.5f;
                float centerX = ((_grid.width - 1) - (_grid.height - 1)) * 0.5f * halfW;
                float centerY = -((_grid.width - 1) + (_grid.height - 1)) * 0.5f * halfH;
                _cam.transform.position = new Vector3(centerX, centerY, _cam.transform.position.z);
            }

            // Aplicar pintura em cada cell
            foreach (var cell in _currentSelection)
            {
                if (cell.x < 0 || cell.y < 0 || cell.x >= _grid.width || cell.y >= _grid.height)
                {
                    Debug.LogWarning($"[Sandbox] Cell ({cell.x},{cell.y}) out of bounds after expansion ({_grid.width}x{_grid.height})");
                    continue;
                }

                // SEMPRE aplica localmente no grid (para o Sandbox refletir na tela).
                ApplySingleCellLocal(cell, isVoidBrush);

                // Se MP: também sincroniza com os outros jogadores via RPC.
                if (RuntimeMultiplayerSession.IsMultiplayer && CollabMapSync.Instance != null)
                {
                    var op = BuildPaintOp(cell, isVoidBrush);
                    CollabMapSync.Instance.PaintOpServerRpc(op);
                }
            }
        }

        private PaintOp BuildPaintOp(Vector2Int cell, bool isVoidBrush)
        {
            if (isVoidBrush)
            {
                return new PaintOp
                {
                    X = cell.x, Y = cell.y,
                    TerrainName = "", ObjectName = "", Height = 0,
                    IsVoid = true, Kind = PaintOpKind.Erase
                };
            }

            // Pincel de objeto
            if (_brush.kind == TileKind.Object)
            {
                int curH = _grid.GetHeight(cell.x, cell.y);
                string curT = _grid.GetTerrainName(cell.x, cell.y);
                return new PaintOp
                {
                    X = cell.x, Y = cell.y,
                    TerrainName = curT, ObjectName = _brush.spriteName,
                    Height = curH, IsVoid = false, Kind = PaintOpKind.Paint
                };
            }

            // Pincel de terreno (inclui rampa)
            string curT2 = _grid.GetTerrainName(cell.x, cell.y);
            int curH2  = _grid.GetHeight(cell.x, cell.y);
            string curObj = _grid.GetObjectName(cell.x, cell.y);
            int newH = curH2;
            if (curT2 == _brush.spriteName)
            {
                int maxH = RuntimeTuning.Active != null ? RuntimeTuning.Active.maxTileHeight : 8;
                newH = Mathf.Min(curH2 + _brush.height, maxH);
            }
            else
            {
                newH = _brush.height;
            }
            return new PaintOp
            {
                X = cell.x, Y = cell.y,
                TerrainName = _brush.spriteName, ObjectName = curObj,
                Height = newH, IsVoid = false, Kind = PaintOpKind.Paint
            };
        }

        // Retorna a "família" do terreno (remove sufixos _full/_half/_ramp_*/etc).
        // Ex.: grass_tile_full e grass_tile_half → "grass_tile" (mesma família → empilha).
        private static string TerrainFamily(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var n = name.ToLowerInvariant();
            foreach (var suf in new[] { "_full", "_half",
                "_ramp_no", "_ramp_ne", "_ramp_so", "_ramp_se",
                "_slide_no", "_slide_ne", "_slide_so", "_slide_se",
                "_cliff_so", "_cliff_se" })
                if (n.EndsWith(suf)) return n.Substring(0, n.Length - suf.Length);
            return n;
        }

        private void ApplySingleCellLocal(Vector2Int cell, bool isVoidBrush)
        {
            bool isVoidCell = _grid.IsVoid(cell.x, cell.y);
            if (isVoidBrush)
            {
                if (!isVoidCell)
                    _grid.SetCell(cell.x, cell.y, "", 0, "", true); // apaga terreno + objeto
                return;
            }

            // Pincel de OBJETO: não toca terreno/altura, ocupa 1 slot por célula.
            if (_brush.kind == TileKind.Object)
            {
                if (isVoidCell) return; // não pinta objeto em célula vazia
                int curH = _grid.GetHeight(cell.x, cell.y);
                string curT = _grid.GetTerrainName(cell.x, cell.y);
                _grid.SetCell(cell.x, cell.y, curT, curH, _brush.spriteName, false);
                return;
            }

            // Pincel de TERRENO (inclui rampa): empilha ou substitui a base.
            if (isVoidCell)
            {
                _grid.SetCell(cell.x, cell.y, _brush.spriteName, _brush.height, _grid.GetObjectName(cell.x, cell.y), false);
            }
            else
            {
                string curT = _grid.GetTerrainName(cell.x, cell.y);
                int curH   = _grid.GetHeight(cell.x, cell.y);
                // Empilha se MESMA FAMÍLIA de terreno sólido (grama full/half = mesma grama).
                // Água (height < 0) "escava": substitui em vez de empilhar.
                bool sameFamily = TerrainFamily(curT) == TerrainFamily(_brush.spriteName);
                bool bothSolid  = _brush.height >= 0 && curH >= 0;
                if (sameFamily && bothSolid)
                {
                    // Mesma família → empilha (soma altura), até maxTileHeight
                    int maxH = RuntimeTuning.Active != null ? RuntimeTuning.Active.maxTileHeight : 8;
                    int newH = Mathf.Min(curH + _brush.height, maxH);
                    Debug.Log($"[Sandbox] EMPILHA ({cell.x},{cell.y}): curT={curT} curH={curH} brush={_brush.spriteName}(h{_brush.height}) -> newH={newH}");
                    _grid.SetCell(cell.x, cell.y, curT, newH, _grid.GetObjectName(cell.x, cell.y), false);
                }
                else
                {
                    Debug.Log($"[Sandbox] SUBSTITUI ({cell.x},{cell.y}): curT={curT} curH={curH} brush={_brush.spriteName}(h{_brush.height}) -> newH={_brush.height}");
                    // Terreno diferente → substitui a base (mantém objeto e altura atual)
                    _grid.SetCell(cell.x, cell.y, _brush.spriteName, _brush.height, _grid.GetObjectName(cell.x, cell.y), false);
                }
            }
        }

        // =========================================================================
        // API pública para CollabMapSync (chamada pelo ClientRpc)
        // =========================================================================

        /// <summary>Aplica uma PaintOp recebida pela rede no estado local do grid.</summary>
        public void ApplyPaintOp(PaintOp op)
        {
            if (_grid == null) return;
            if (op.X < 0 || op.Y < 0 || op.X >= _grid.width || op.Y >= _grid.height) return;

            switch (op.Kind)
            {
                case PaintOpKind.Erase:
                    _grid.SetCell(op.X, op.Y, "", 0, "", true);
                    break;

                case PaintOpKind.Paint:
                    _grid.SetCell(op.X, op.Y, op.TerrainName, op.Height, op.ObjectName, false);
                    break;

                case PaintOpKind.Height:
                    _grid.SetCell(op.X, op.Y, _grid.GetTerrainName(op.X, op.Y),
                                  op.Height, _grid.GetObjectName(op.X, op.Y),
                                  _grid.IsVoid(op.X, op.Y));
                    break;
            }

            // Atualizar _map local para ficar em sincronia
            _map = _grid.ExportToMapData();
        }

        /// <summary>Aplica expansão de grid recebida pela rede.</summary>
        public void ApplyGridExpand(int newW, int newH)
        {
            if (_grid == null) return;
            // Re-expandir localmente para igualar as dimensões
            while (_grid.width < newW)
            {
                var nm = _grid.Expand(1, 0);
                if (nm != null) _map = nm; else break;
            }
            while (_grid.height < newH)
            {
                var nm = _grid.Expand(0, 1);
                if (nm != null) _map = nm; else break;
            }
            // Recentrar câmera
            if (_cam != null && _iso != null)
            {
                float halfW = _iso.TileUnitsW * 0.5f;
                float halfH = _iso.TileUnitsH * 0.5f;
                float cx = ((_grid.width - 1) - (_grid.height - 1)) * 0.5f * halfW;
                float cy = -((_grid.width - 1) + (_grid.height - 1)) * 0.5f * halfH;
                _cam.transform.position = new Vector3(cx, cy, _cam.transform.position.z);
            }
        }

        /// <summary>Aplica snapshot completo do mapa (late-joiner ou snapshot final).</summary>
        public void ApplyFullSnapshot(MapData map)
        {
            if (map == null || _grid == null) return;
            _map = map;
            _grid.sourceMap = map;
            _grid.width  = map.width;
            _grid.height = map.height;
            _grid.Build();
            Debug.Log($"[SandboxController] Snapshot aplicado: {map.width}x{map.height}");
        }

        // ── HOVER CURSOR ─────────────────────
        private void CreateHoverCursor()
        {
            var go = new GameObject("HoverCursor");
            // Migração XY→XZ (2026-07-20): cursor de tile é topo → deita no XZ.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(_grid.spriteScale, _grid.spriteScale, 1f);
            _hoverCursor = go.AddComponent<SpriteRenderer>();
            _hoverCursor.sprite = BuildCursorSprite();
            _hoverCursor.sortingOrder = 9000;
            _hoverCursor.enabled = false;
        }

        private void UpdateHoverCursor()
        {
            if (_hoverCursor == null) return;
            if (Mouse.current == null || IsOverButton()) { _hoverCursor.enabled = false; return; }

            var raw = CellUnderMouse();
            int dist = DistanceToGridEdge(raw);

            Vector2Int target;
            if (dist == 0)
            {
                // Dentro do grid — mostra onde vai pintar
                target = raw;
            }
            else if (dist == 1)
            {
                // 1 tile fora — calcular posição final após expansão
                int fx = raw.x;
                int fy = raw.y;
                if (raw.x < 0) fx = 0;
                else if (raw.x >= _grid.width) fx = _grid.width;
                if (raw.y < 0) fy = 0;
                else if (raw.y >= _grid.height) fy = _grid.height;
                target = new Vector2Int(fx, fy);
            }
            else
            {
                // Longe demais — mostra na borda mais próxima (dist=1)
                int fx = raw.x;
                int fy = raw.y;
                if (raw.x < 0) fx = 0;
                else if (raw.x >= _grid.width) fx = _grid.width;
                if (raw.y < 0) fy = 0;
                else if (raw.y >= _grid.height) fy = _grid.height;
                target = new Vector2Int(fx, fy);
            }

            _hoverCursor.enabled = true;
            _hoverCursor.transform.position = _grid.CellToWorld(target);
        }

        private static Sprite _cursorSprite;
        private static Sprite BuildCursorSprite()
        {
            if (_cursorSprite != null) return _cursorSprite;
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < S * S; i++) tex.SetPixel(i % S, i / S, Color.clear);

            // Losango do topo do tile (32px), centrado em (16,24) — mesmo pivot dos tiles.
            Vector2[] quad = { new(0, 24), new(16, 32), new(32, 24), new(16, 16) };
            Color fill = Tuning.Get().hoverCursorColor;
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (InConvex(p, quad)) tex.SetPixel(x, y, fill);
            }
            tex.Apply();
            _cursorSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.75f), 16f);
            return _cursorSprite;
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

        // ── UNIDADES ─────────────────────────
        private void HandleUnitClick(Vector2Int cell, Team team)
        {
            // Selecionar unidade existente cujo FOOTPRINT cobre a célula clicada
            for (int i = 0; i < _placements.Count; i++)
            {
                var pl = _placements[i];
                int pfp = Mathf.Max(1, pl.stats.Footprint);
                if (cell.x >= pl.x && cell.x < pl.x + pfp &&
                    cell.y >= pl.y && cell.y < pl.y + pfp)
                {
                    _selectedUnit = i;
                    _hud.ShowUnitEditor(pl);
                    return;
                }
            }

            // Clique em espaço vazio com unidade selecionada → desseleciona
            if (_selectedUnit >= 0)
            {
                _selectedUnit = -1;
                _hud.ShowUnitEditor(null);
                return;
            }

            // Criar nova unidade — só se um sprite estiver ativo
            if (string.IsNullOrEmpty(_spriteId)) return;
            var spriteDef = CharacterSpriteCatalog.GetByPath(_spriteId);
            var baseStats = _activeStats ?? new UnitStatBlock { STR=5, VIT=5, DEX=5, AGI=5, INT=1, WIS=1, Footprint=3 };
            int fp = Mathf.Max(1, baseStats.Footprint);
            int half = (fp - 1) / 2;
            int ax = Mathf.Clamp(cell.x - half, 0, Mathf.Max(0, _grid.width  - fp));
            int ay = Mathf.Clamp(cell.y - half, 0, Mathf.Max(0, _grid.height - fp));
            var anchor = new Vector2Int(ax, ay);

            // Impedir sobreposição com outra unidade
            foreach (var pl in _placements)
            {
                if (GridManager.FootprintsOverlap(anchor, fp,
                        new Vector2Int(pl.x, pl.y), Mathf.Max(1, pl.stats.Footprint)))
                {
                    _hud.ShowToast("Espaço ocupado por outra unidade.");
                    return;
                }
            }

            var placement = new UnitPlacement
            {
                spritePath = _spriteId,
                team = (int)team,
                x = ax,
                y = ay,
                displayName = _activeName ?? (spriteDef != null ? spriteDef.displayName : "Unidade"),
                stats = CloneStatBlock(baseStats),
                weaponId = "Hatchet" // arma padrão; pode ser trocada no painel de stats
            };

            var color = team == Team.Player ? PlayerColor : EnemyColor;
            var u = CreateUnitVisual(placement, color);

            _placements.Add(placement);
            _unitVisuals.Add(u);
            _selectedUnit = _placements.Count - 1;
            _hud.ShowUnitEditor(placement);
        }

        private Unit CreateUnitVisual(UnitPlacement p, Color color)
        {
            // Resolver spritePath com fallback
            string resPath = !string.IsNullOrEmpty(p.spritePath) ? p.spritePath : CharacterSpriteCatalog.Default;
            var go = new GameObject(p.displayName);
            var u = go.AddComponent<Unit>();
            u.unitName = p.displayName;
            u.team = (Team)p.team;
            u.stats = p.stats.ToAttributeStats();
            u.weaponId = !string.IsNullOrEmpty(p.weaponId) ? p.weaponId : "";
            u.Init(_grid, new Vector2Int(p.x, p.y), color, resPath);
            return u;
        }

        private UnitStatBlock CloneStatBlock(UnitStatBlock o)
        {
            return new UnitStatBlock
            {
                STR = o.STR, VIT = o.VIT, DEX = o.DEX, AGI = o.AGI, INT = o.INT, WIS = o.WIS,
                Footprint = o.Footprint, AttackRange = o.AttackRange
            };
        }

        // ── API chamada pela HUD ─────────────
        public void SetPhase(Phase p)
        {
            _phase = p;
            _selectedUnit = -1;
            _hud.SetPhaseUI(p);
            _hud.ShowUnitEditor(null);
        }

        public void SetBrush(TileBrush b) { _brush = b; Debug.Log($"[Sandbox:SetBrush] name={b.name} spriteName='{b.spriteName}' kind={b.kind} height={b.height}"); }

        public void SetSprite(string spritePath)
        {
            _spriteId = spritePath;
            _activeStats = null;
            _activeName = null;
        }

        public void SetPreset(CharacterPreset p)
        {
            _spriteId = !string.IsNullOrEmpty(p.spritePath) ? p.spritePath : CharacterSpriteCatalog.Default;
            _activeStats = p.stats;
            _activeName = p.presetName;
        }

        public void SetMapName(string name) => _mapName = name;

        public void SetSelectedName(string name)
        {
            if (_selectedUnit < 0 || _selectedUnit >= _placements.Count) return;
            _placements[_selectedUnit].displayName = name;
            if (_selectedUnit < _unitVisuals.Count && _unitVisuals[_selectedUnit] != null)
                _unitVisuals[_selectedUnit].unitName = name;
        }

        public void SaveSelectedAsPreset()
        {
            if (_selectedUnit < 0 || _selectedUnit >= _placements.Count)
            {
                _hud.ShowToast("Selecione uma unidade primeiro.");
                return;
            }
            var pl = _placements[_selectedUnit];
            var preset = new CharacterPreset
            {
                presetName = string.IsNullOrWhiteSpace(pl.displayName) ? "Personagem" : pl.displayName,
                spritePath = !string.IsNullOrEmpty(pl.spritePath) ? pl.spritePath : CharacterSpriteCatalog.Default,
                weaponId = !string.IsNullOrEmpty(pl.weaponId) ? pl.weaponId : "Hatchet",
                stats = CloneStatBlock(pl.stats)
            };
            CharacterStorage.Save(preset);
            _hud.RebuildClassPalette();
            _hud.ShowToast("Personagem salvo: " + preset.presetName);
        }

        public void EditSelectedStat(string attr, float delta)
        {
            if (_selectedUnit < 0 || _selectedUnit >= _placements.Count) return;

            var p = _placements[_selectedUnit];
            switch (attr)
            {
                case "STR": p.stats.STR = Mathf.Clamp(p.stats.STR + delta, CharacterConfig.AttrMin, CharacterConfig.AttrMax); break;
                case "VIT": p.stats.VIT = Mathf.Clamp(p.stats.VIT + delta, CharacterConfig.AttrMin, CharacterConfig.AttrMax); break;
                case "DEX": p.stats.DEX = Mathf.Clamp(p.stats.DEX + delta, CharacterConfig.AttrMin, CharacterConfig.AttrMax); break;
                case "AGI": p.stats.AGI = Mathf.Clamp(p.stats.AGI + delta, CharacterConfig.AttrMin, CharacterConfig.AttrMax); break;
                case "INT": p.stats.INT = Mathf.Clamp(p.stats.INT + delta, CharacterConfig.AttrMin, CharacterConfig.AttrMax); break;
                case "WIS": p.stats.WIS = Mathf.Clamp(p.stats.WIS + delta, CharacterConfig.AttrMin, CharacterConfig.AttrMax); break;
            }

            if (_selectedUnit < _unitVisuals.Count && _unitVisuals[_selectedUnit] != null)
                _unitVisuals[_selectedUnit].stats = p.stats.ToAttributeStats();

            _hud.ShowUnitEditor(p);
        }

        public void DeleteSelectedUnit()
        {
            if (_selectedUnit < 0 || _selectedUnit >= _placements.Count) return;

            if (_unitVisuals[_selectedUnit] != null)
                Destroy(_unitVisuals[_selectedUnit].gameObject);

            _placements.RemoveAt(_selectedUnit);
            _unitVisuals.RemoveAt(_selectedUnit);
            _selectedUnit = -1;
            _hud.ShowUnitEditor(null);
        }

        public void CycleSelectedWeapon()
        {
            if (_selectedUnit < 0 || _selectedUnit >= _placements.Count) return;

            var p = _placements[_selectedUnit];
            var weapons = WeaponCatalog.All();

            // Encontrar índice da arma atual
            int currentIdx = -1;
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i].id == p.weaponId)
                {
                    currentIdx = i;
                    break;
                }
            }

            // Passar para a próxima (ou voltar ao início + "nenhuma")
            if (currentIdx < 0)
            {
                // Atualmente desarmado → primeira arma
                p.weaponId = weapons.Length > 0 ? weapons[0].id : "none";
            }
            else if (currentIdx < weapons.Length - 1)
            {
                // Próxima arma
                p.weaponId = weapons[currentIdx + 1].id;
            }
            else
            {
                // Última arma → desarmado (sentinela "none": NÃO cai no fallback da arma default)
                p.weaponId = "none";
            }

            // Recriar visual para refletir a nova arma
            if (_unitVisuals[_selectedUnit] != null)
                Destroy(_unitVisuals[_selectedUnit].gameObject);

            var color = ((Team)p.team) == Team.Player ? PlayerColor : EnemyColor;
            var u = CreateUnitVisual(p, color);
            _unitVisuals[_selectedUnit] = u;

            // Atualizar o painel
            _hud.ShowUnitEditor(p);
        }

        public void SaveMap()
        {
            bool hasAlly  = _placements.Exists(p => p.team == 0);
            bool hasEnemy = _placements.Exists(p => p.team == 1);
            if (!hasAlly || !hasEnemy)
            {
                _hud.ShowToast("Coloque ao menos 1 aliado e 1 inimigo.");
                return;
            }

            _map.mapName = _mapName;
            _grid.ExportTerrain(_map);
            _map.units = new List<UnitPlacement>(_placements);
            MapStorage.Save(_map);
            _hud.ShowToast("Mapa salvo: " + _mapName);
        }

        public void BackToMenu()
        {
            if (RuntimeMultiplayerSession.IsLocalContentSession)
                LocalContentLauncher.FinishAndReturnToMenu();
            else
                SceneManager.LoadScene("MainMenu");
        }

        // ── MP: sinalizar que o jogador local está pronto ─────────────────────
        public void SetReadyMap()
        {
            if (!RuntimeMultiplayerSession.IsMultiplayer) return;
            RoomManager.Instance?.SetReadyMapServerRpc(true);

            // Se for o host e todos já estiverem prontos, envia snapshot final
            // (o servidor verifica CheckAllReadyMap automaticamente)
        }
    }
}
