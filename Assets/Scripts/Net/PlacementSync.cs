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
        private Canvas _canvas;
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
        public void OnGridReady(GridManager grid, Canvas canvas, BattleHUD hud,
            Camera cam, CameraController camCtrl,
            RoundManager round, PlanningController planner,
            TileEffectManager tileFx, GameTuning tuning)
        {
            _grid    = grid;
            _canvas  = canvas;
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

        private void OnDestroy()
        {
            if (RoomManager.Instance != null)
                RoomManager.Instance.OnBattleStart -= OnBattleStart;
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

            // Validações
            if (x < 0 || y < 0 || x >= _grid.width || y >= _grid.height) { Debug.Log("[PlacementSync] rejeitado: fora do grid"); return; }
            if (_grid.IsVoid(x, y)) { Debug.Log("[PlacementSync] rejeitado: celula void"); return; }
            if (!IsInSpawnZone(senderId, anchor)) { Debug.Log($"[PlacementSync] rejeitado: ({x},{y}) fora da zona de {senderId}"); return; }
            if (_occupiedCells.Contains(anchor)) { Debug.Log("[PlacementSync] rejeitado: celula ocupada"); return; }

            // Buscar preset do jogador
            if (!RoomManager.Instance.SubmittedCharacters.TryGetValue(senderId, out var preset))
            { Debug.LogWarning($"[PlacementSync] sem personagem submetido para {senderId} — posicionamento ignorado"); return; }

            // Buscar slot para team
            var slots = RoomManager.Instance.Slots;
            int slotTeam = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].ClientId == senderId) { slotTeam = slots[i].Team; break; }

            uint unitId = _nextUnitId++;
            _occupiedCells.Add(anchor);
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

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 screen = Mouse.current.position.ReadValue();
                float depth = Mathf.Abs(_cam.transform.position.z);
                Vector3 world = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
                var cell = _grid.WorldToCell(world);

                // Verificar se a célula é da zona local
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
            _round.Setup(_grid, _planner, _hud, _canvas, _cam, _camCtrl,
                _allUnits, _controlled, _tileFx);

            // Chat na batalha: mensagens do RoomManager aparecem no log do BattleHUD
            if (RoomManager.Instance != null && _hud != null)
                RoomManager.Instance.OnChatMessage += (name, msg) =>
                    _hud.LogAction($"<color=#aaddff>[Chat] {name}: {msg}</color>");

            // Spawn do LockstepBattleSync (somente host)
            if (Unity.Netcode.NetworkManager.Singleton != null
                && Unity.Netcode.NetworkManager.Singleton.IsServer
                && LockstepBattleSync.Instance == null)
            {
                var go = new GameObject("LockstepBattleSync");
                var lbs = go.AddComponent<LockstepBattleSync>();
                go.AddComponent<Unity.Netcode.NetworkObject>();
                go.GetComponent<Unity.Netcode.NetworkObject>().Spawn();
                lbs.Init(_round, _allUnits);
            }
            else
            {
                // CLIENTE: o LockstepBattleSync pode replicar DEPOIS deste callback —
                // tentar agora e re-tentar até aparecer (senão o Init nunca roda e o
                // plano local não é submetido).
                StartCoroutine(InitLockstepWhenReady());
            }

            _round.Begin();

            Debug.Log($"[PlacementSync] Batalha iniciada. Seed={seed}. Unidades={_allUnits.Count}");
        }

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
