// Net/PlacementSync.cs
// NetworkBehaviour responsável pelo posicionamento de unidades no início da batalha.
// Spawn: host cria junto do RoomManager (via NetBootstrap.SpawnRoomManager).
//
// Fluxo:
//   Cliente em fase Placement clica em célula válida da sua zona
//   → PlaceUnitServerRpc(x, y)
//   → host valida (zona, célula livre, não-void)
//   → atribui uint unitId sequencial
//   → SpawnUnitClientRpc para TODOS
//   → cada cliente instancia Unit LOCALMENTE e registra no UnitRegistry

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PangeaSkirmish
{
    public class PlacementSync : NetworkBehaviour
    {
        public static PlacementSync Instance { get; private set; }

        // ---- Referências injetadas pelo GameBootstrap -------------------------
        private GridManager _grid;
        private BattleHUD _hud;
        private Camera _cam;
        private CameraController _camCtrl;
        private RoundManager _round;
        private PlanningController _planner;
        private TileEffectManager _tileFx;
        private GameTuning _tuning;

        // ---- Estado servidor -------------------------------------------------
        private uint _nextUnitId = 1;
        private readonly HashSet<Vector2Int> _occupiedCells = new HashSet<Vector2Int>();
        private readonly List<(ulong clientId, uint unitId)> _placements = new List<(ulong, uint)>();

        // ---- Estado cliente local --------------------------------------------
        private bool _gridReady;
        private bool _placementDone;
        private List<Vector2Int> _myZoneHighlights = new List<Vector2Int>();
        private Vector2Int _lastHover = new Vector2Int(int.MinValue, int.MinValue);
        private GameObject _zoneOverlay;

        // ---- Todas as unidades de todos os clientes (ordenadas no spawn) -----
        private List<Unit> _allUnits = new List<Unit>();
        private Unit _controlled;

        // ClientId REAL desta instância em runtime. NÃO usar RuntimeMultiplayerSession.LocalClientId,
        // que é capturado logo após StartClient (antes do NGO atribuir o id ao cliente → fica 0).
        private static ulong LocalId =>
            NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId
                                             : RuntimeMultiplayerSession.LocalClientId;

        public override void OnNetworkSpawn()
        {
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        // =========================================================================
        // Chamado pelo GameBootstrap quando o grid da cena Battle está pronto
        // =========================================================================
        public void OnGridReady(GridManager grid, BattleHUD hud,
            Camera cam, CameraController camCtrl,
            RoundManager round, PlanningController planner,
            TileEffectManager tileFx, GameTuning tuning)
        {
            // _occupiedCells/_placements/_nextUnitId só cresciam (nunca eram limpos) — se o
            // host reiniciasse a fase de posicionamento (ex.: por desconexão), células e
            // unitIds de uma sessão anterior persistiam e rejeitavam posicionamentos válidos
            // com "célula ocupada". Reinicia o estado servidor toda vez que o grid fica pronto.
            if (IsServer) ResetPlacementState();

            _grid    = grid;
            _hud     = hud;
            _cam     = cam;
            _camCtrl = camCtrl;
            _round   = round;
            _planner = planner;
            _tileFx  = tileFx;
            _tuning  = tuning;
            _gridReady = true;

            ShowPlacementZone();
            _hud?.SetWaitingText(_myZoneHighlights.Count > 0
                ? "Clique numa célula destacada para posicionar seu personagem"
                : "ERRO: sem zona de posicionamento (veja o console)");
            Debug.Log($"[PlacementSync] OnGridReady: LocalClientId={LocalId}, grid {_grid.width}x{_grid.height}, zona local = {_myZoneHighlights.Count} celulas");

            // Escutar StartBattle
            if (RoomManager.Instance != null)
                RoomManager.Instance.OnBattleStart += OnBattleStart;
        }

        public override void OnDestroy()
        {
            // Ao fechar o Play Mode/aplicação, o NGO destrói NetworkObjects DontDestroyOnLoad
            // em ordem não-determinística; desinscrever eventos aqui pode acessar objetos já
            // destruídos e contribuir para NullReferenceException DENTRO do próprio pacote
            // Netcode (visto no console: NetworkObject.OnNetworkBehaviourDestroyed). É
            // cosmético (só no encerramento, não afeta o gameplay) — pulamos a limpeza manual
            // nesse caso específico e deixamos o motor destruir tudo.
            if (!ApplicationQuitTracker.IsQuitting)
            {
                if (RoomManager.Instance != null)
                    RoomManager.Instance.OnBattleStart -= OnBattleStart;
                if (_chatRoom != null) _chatRoom.OnChatMessage -= HandleChatMessage;
            }
            base.OnDestroy();
        }

        // =========================================================================
        // Zona de spawn
        // =========================================================================
        private void ShowPlacementZone()
        {
            if (_grid == null) return;

            var zone = GetMyZone();
            _myZoneHighlights = zone;

            // Highlight visual usando a infra do GridManager
            _grid.HighlightAnchors(zone, _grid.highlightColor);
        }

        private List<Vector2Int> GetMyZone()
        {
            var result = new List<Vector2Int>();
            if (_grid == null || RoomManager.Instance == null) return result;

            var slots = RoomManager.Instance.Slots;
            int mySlotIdx = -1;
            ulong myId = LocalId;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].ClientId == myId) { mySlotIdx = i; break; }
            if (mySlotIdx < 0) { Debug.LogWarning($"[PlacementSync] slot nao encontrado p/ LocalClientId={myId} (zona vazia)"); return result; }

            int gameMode = RuntimeMultiplayerSession.CurrentConfig.gameMode;
            int w = _grid.width, h = _grid.height;
            int halfW = w / 2;

            // TDM: team 0 → esquerda (x < halfW); team 1 → direita (x >= halfW)
            // FFA: 4 quadrantes por índice de slot (0=SW, 1=SE, 2=NW, 3=NE)
            int myTeam = slots[mySlotIdx].Team;

            if (gameMode == 0) // TDM
            {
                int xStart = myTeam == 0 ? 0 : halfW;
                int xEnd   = myTeam == 0 ? halfW : w;
                for (int x = xStart; x < xEnd; x++)
                for (int y = 0; y < h; y++)
                    if (!_grid.IsVoid(x, y)) result.Add(new Vector2Int(x, y));
            }
            else // FFA: 4 quadrantes
            {
                int halfH = h / 2;
                int xStart = (mySlotIdx == 1 || mySlotIdx == 3) ? halfW : 0;
                int xEnd   = (mySlotIdx == 1 || mySlotIdx == 3) ? w : halfW;
                int yStart = (mySlotIdx >= 2) ? halfH : 0;
                int yEnd   = (mySlotIdx >= 2) ? h : halfH;
                for (int x = xStart; x < xEnd; x++)
                for (int y = yStart; y < yEnd; y++)
                    if (!_grid.IsVoid(x, y)) result.Add(new Vector2Int(x, y));
            }

            return result;
        }

        // =========================================================================
        // Verificação de zona (servidor)
        // =========================================================================
        private bool IsInSpawnZone(ulong clientId, Vector2Int anchor)
        {
            if (_grid == null || RoomManager.Instance == null) return false;

            var slots = RoomManager.Instance.Slots;
            int slotIdx = -1;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].ClientId == clientId) { slotIdx = i; break; }
            if (slotIdx < 0) return false;

            int gameMode = RuntimeMultiplayerSession.CurrentConfig.gameMode;
            int w = _grid.width, h = _grid.height;
            int halfW = w / 2;
            int myTeam = slots[slotIdx].Team;

            if (gameMode == 0) // TDM
            {
                int xStart = myTeam == 0 ? 0 : halfW;
                int xEnd   = myTeam == 0 ? halfW : w;
                return anchor.x >= xStart && anchor.x < xEnd &&
                       anchor.y >= 0 && anchor.y < h;
            }
            else // FFA
            {
                int halfH = h / 2;
                int xStart = (slotIdx == 1 || slotIdx == 3) ? halfW : 0;
                int xEnd   = (slotIdx == 1 || slotIdx == 3) ? w : halfW;
                int yStart = slotIdx >= 2 ? halfH : 0;
                int yEnd   = slotIdx >= 2 ? h : halfH;
                return anchor.x >= xStart && anchor.x < xEnd &&
                       anchor.y >= yStart && anchor.y < yEnd;
            }
        }

        // =========================================================================
        // Footprint helpers (3x3 etc.) — item (A): tile de selecao representa o
        // tamanho real da unidade, nao so 1 celula.
        // =========================================================================
        private List<Vector2Int> FootprintCells(Vector2Int anchor, int fp)
        {
            var cells = new List<Vector2Int>();
            for (int dx = 0; dx < fp; dx++)
                for (int dy = 0; dy < fp; dy++)
                    cells.Add(new Vector2Int(anchor.x + dx, anchor.y + dy));
            return cells;
        }

        private int GetMyFootprint()
        {
            if (RoomManager.Instance != null &&
                RoomManager.Instance.SubmittedCharacters.TryGetValue(LocalId, out var p) &&
                p.stats != null)
                return p.stats.Footprint;
            return AttributeStats.DefaultFootprint;
        }

        // =========================================================================
        // PlaceUnit
        // =========================================================================
        [ServerRpc(RequireOwnership = false)]
        public void PlaceUnitServerRpc(int x, int y, ServerRpcParams rpc = default)
        {
            ulong senderId = rpc.Receive.SenderClientId;
            Debug.Log($"[PlacementSync] PlaceUnitServerRpc (host): clientId={senderId} cell=({x},{y})");

            // Já posicionou?
            for (int i = 0; i < _placements.Count; i++)
                if (_placements[i].clientId == senderId) { Debug.Log($"[PlacementSync] rejeitado: {senderId} ja posicionou"); return; }

            var anchor = new Vector2Int(x, y);

            // Buscar preset do jogador (precisa do Footprint p/ validar 3x3)
            if (!RoomManager.Instance.SubmittedCharacters.TryGetValue(senderId, out var preset))
            { Debug.LogWarning($"[PlacementSync] sem personagem submetido para {senderId} — posicionamento ignorado"); return; }
            int fp = (preset.stats != null) ? preset.stats.Footprint : AttributeStats.DefaultFootprint;

            // Validações (footprint-aware — item A)
            if (x < 0 || y < 0 || x >= _grid.width || y >= _grid.height) { Debug.Log("[PlacementSync] rejeitado: fora do grid"); return; }
            if (!_grid.IsAnchorInBounds(anchor, fp)) { Debug.Log($"[PlacementSync] rejeitado: footprint {fp}x{fp} em ({x},{y}) fora dos limites ou em celula void"); return; }
            if (!IsInSpawnZone(senderId, anchor)) { Debug.Log($"[PlacementSync] rejeitado: ({x},{y}) fora da zona de {senderId}"); return; }

            // Ocupação: TODAS as células do footprint devem estar livres
            var cells = FootprintCells(anchor, fp);
            for (int i = 0; i < cells.Count; i++)
                if (_occupiedCells.Contains(cells[i])) { Debug.Log($"[PlacementSync] rejeitado: celula {cells[i]} do footprint ocupada"); return; }

            // Buscar slot para team
            var slots = RoomManager.Instance.Slots;
            int slotTeam = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].ClientId == senderId) { slotTeam = slots[i].Team; break; }

            uint unitId = _nextUnitId++;
            foreach (var c in cells) _occupiedCells.Add(c);
            _placements.Add((senderId, unitId));

            string presetJson = JsonUtility.ToJson(preset);
            SpawnUnitClientRpc(unitId, senderId, slotTeam, presetJson, x, y);

            // Marcar Placed no slot (chamada DIRETA — já estamos no host; via RPC o NGO
            // sobrescreveria o senderId pelo do host e marcaria sempre o slot errado)
            RoomManager.Instance.MarkPlaced(senderId);
        }

        [ClientRpc]
        private void SpawnUnitClientRpc(uint unitId, ulong ownerId, int team, string presetJson, int x, int y)
        {
            if (_grid == null) return;

            CharacterPreset preset;
            try { preset = JsonUtility.FromJson<CharacterPreset>(presetJson); }
            catch { Debug.LogError("[PlacementSync] Falha ao desserializar preset"); return; }

            // Cores/lado RELATIVOS ao jogador LOCAL (cada cliente vê o próprio lado como
            // aliado): FFA = só a minha unidade é aliada; TDM = meu time é aliado.
            // unit.team em MP é APENAS visual — a hostilidade real usa IsHostileTo
            // (teamId/ownerId), então essa divergência por cliente é segura.
            bool isAlly;
            if (RuntimeMultiplayerSession.CurrentConfig != null
                && RuntimeMultiplayerSession.CurrentConfig.gameMode == 0) // TDM
            {
                int localTeam = 0;
                var slots = RoomManager.Instance != null ? RoomManager.Instance.Slots : null;
                if (slots != null)
                    for (int i = 0; i < slots.Count; i++)
                        if (slots[i].ClientId == LocalId) { localTeam = slots[i].Team; break; }
                isAlly = team == localTeam;
            }
            else // FFA: cada um por si
            {
                isAlly = ownerId == LocalId;
            }

            var tuning = _tuning ?? Tuning.Get();
            var color  = isAlly ? tuning.playerTeamColor : tuning.enemyTeamColor;

            // Criar unidade localmente (sem NetworkObject)
            var go = new GameObject(preset.presetName);
            var unit = go.AddComponent<Unit>();
            unit.unitName = preset.presetName;
            unit.team     = isAlly ? Team.Player : Team.Enemy; // visual local (HUD/footprint/tint)
            unit.ownerId  = ownerId;
            unit.teamId   = team;
            unit.isPlayerCharacter = (ownerId == LocalId);
            unit.weaponId = !string.IsNullOrEmpty(preset.weaponId) ? preset.weaponId : "Hatchet";
            unit.stats    = (preset.stats ?? new UnitStatBlock()).ToAttributeStats();
            unit.Init(_grid, new Vector2Int(x, y), color,
                !string.IsNullOrEmpty(preset.spritePath) ? preset.spritePath : CharacterSpriteCatalog.Default);

            UnitRegistry.Register(unitId, unit);
            _allUnits.Add(unit);

            // A primeira unidade controlada é a local do jogador
            if (unit.isPlayerCharacter && _controlled == null)
                _controlled = unit;

            if (_gridReady && ownerId == LocalId)
                _placementDone = true;

            Debug.Log($"[PlacementSync] Unidade {preset.presetName} spawnada em ({x},{y}), team={team}, owner={ownerId}");
        }

        // =========================================================================
        // Input de placement (cliente local)
        // =========================================================================
        private void Update()
        {
            if (!_gridReady || _placementDone) return;
            if (!RuntimeMultiplayerSession.IsMultiplayer) return;
            if (_grid == null || _cam == null) return;
            if (Mouse.current == null) return;

            Vector2 screen = Mouse.current.position.ReadValue();
            // Migração XY→XZ (2026-07-20): raycast no plano y=0 (ScreenToGround),
            // NÃO ScreenToWorldPoint (2D, plano Z=0 que não existe mais).
            Vector3 world;
            if (!_grid.ScreenToGround(_cam, screen, out world)) world = Vector3.zero;
            var cell = _grid.WorldToCell(world);

            // Preview do footprint (3x3) sob o cursor — item (A). Só redesenha quando
            // a célula muda, e restaura o highlight da zona quando fora do alvo.
            if (cell != _lastHover)
            {
                _lastHover = cell;
                int fp = GetMyFootprint();
                bool validTarget = _grid.IsAnchorInBounds(cell, fp) && _myZoneHighlights.Contains(cell);
                if (validTarget)
                    _grid.HighlightAnchors(FootprintCells(cell, fp),
                        _tuning != null ? _tuning.playerFootprintColor : Color.cyan);
                else
                    _grid.HighlightAnchors(_myZoneHighlights, _grid.highlightColor);
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Debug.Log($"[PlacementSync] clique em {cell} — naZona={_myZoneHighlights.Contains(cell)}");
                if (_myZoneHighlights.Contains(cell))
                {
                    PlaceUnitServerRpc(cell.x, cell.y);
                    _grid.ClearHighlight();
                    _placementDone = true;
                    _hud?.SetWaitingText("Você posicionou. Aguardando os outros jogadores...");
                }
            }
        }

        // =========================================================================
        // StartBattle → Begin()
        // =========================================================================
        private void OnBattleStart(int seed)
        {
            if (_hud != null) _hud.HideWaitingForPlacement();

            if (_round == null || _planner == null) return;

            // Configurar PlanningController e RoundManager com todas as unidades
            _planner.Setup(_grid, _cam, _allUnits);
            _round.Setup(_grid, _planner, _hud, _cam, _camCtrl,
                _allUnits, _controlled, _tileFx);

            // Chat na batalha: mensagens do RoomManager aparecem no log do BattleHUD.
            // Método nomeado (não lambda anônima) para poder desinscrever em OnDestroy —
            // senão a captura de _hud vira dangling se a cena recarregar sem descarregar.
            if (RoomManager.Instance != null && _hud != null)
            {
                _chatRoom = RoomManager.Instance;
                _chatRoom.OnChatMessage += HandleChatMessage;
            }

            // O LockstepBattleSync é spawnado pelo HOST no RoomManager.CheckAllPlaced
            // (prefab registrado, antes do StartBattle). Aqui só vinculamos — com retry,
            // pois no cliente a replicação pode chegar alguns frames depois.
            if (LockstepBattleSync.Instance != null)
                LockstepBattleSync.Instance.Init(_round, _allUnits);
            else
                StartCoroutine(InitLockstepWhenReady());

            _round.Begin();

            Debug.Log($"[PlacementSync] Batalha iniciada. Seed={seed}. Unidades={_allUnits.Count}");
        }

        private void ResetPlacementState()
        {
            _occupiedCells.Clear();
            _placements.Clear();
            _nextUnitId = 1;
        }

        private RoomManager _chatRoom;
        private void HandleChatMessage(string name, string msg) =>
            _hud?.LogAction($"<color=#aaddff>[Chat] {name}: {msg}</color>");

        private System.Collections.IEnumerator InitLockstepWhenReady()
        {
            float t = 10f;
            while (LockstepBattleSync.Instance == null && t > 0f)
            {
                t -= Time.deltaTime;
                yield return null;
            }
            if (LockstepBattleSync.Instance != null)
            {
                LockstepBattleSync.Instance.Init(_round, _allUnits);
                Debug.Log("[PlacementSync] LockstepBattleSync vinculado no cliente");
            }
            else
                Debug.LogError("[PlacementSync] LockstepBattleSync NUNCA replicou no cliente (timeout)");
        }
    }
}
