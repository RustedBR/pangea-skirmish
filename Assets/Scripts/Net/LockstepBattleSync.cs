// Net/LockstepBattleSync.cs
// Máquina de lockstep por round para batalha MP.
// Spawned pelo PlacementSync junto ao RoomManager após todos os posicionamentos.
//
// Fluxo por round:
//   1. PLANNING — cada cliente planeja normalmente via PlanningController.
//   2. ConfirmPlan (botão ou timeout) → SubmitPlanServerRpc(gz)
//   3. Host coleta planos de todos; timeout +5s → ausentes = plano vazio
//   4. Host monta RoundPlansWire { roundSeed, plans } → ExecuteRoundClientRpc(gz)
//   5. Cada cliente: ApplyToUnit em cada unidade, BattleRng.Seed(seed), RoundManager.RunActionPhaseMp()
//   6. Fim de round → ReportHashServerRpc(hash) → host compara → diverge = snapshot resync

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace PangeaSkirmish
{
    public class LockstepBattleSync : NetworkBehaviour
    {
        public static LockstepBattleSync Instance { get; private set; }

        // ---- Referências injetadas pelo PlacementSync / RoundManager ----
        private RoundManager  _round;
        private List<Unit>    _units;

        // ---- Coleta de planos no host ----
        private readonly Dictionary<ulong, byte[]> _receivedPlans = new Dictionary<ulong, byte[]>();
        private bool _collectingPlans;
        private float _collectionTimer;
        private const float HostExtraTimeout = 5f;
        private int _expectedCount;
        // Corrotina de timeout do round ATUAL — precisa ser cancelada quando a coleta fecha
        // cedo (AllSlotsHaveSubmitted). Sem isto, a corrotina antiga continua viva em segundo
        // plano e, ~35s depois (contados a partir do round em que nasceu), acorda e fecha
        // A RODADA SEGUINTE que estiver em coleta naquele instante — mesmo que ela ainda devesse
        // aguardar planos — porque o único sinal que ela olha (_collectingPlans) é compartilhado
        // entre rodadas. Isso causava "rounds pulados" (broadcast com 0 planos de 0 jogadores
        // bem antes do timeout real daquela rodada) e contribuiu para DESYNCs.
        private Coroutine _timeoutCoroutine;

        // ---- Hash por round ----
        // Indexado por ROUND (não só por clientId): antes, um hash atrasado de um round
        // (latência de rede) se misturava com o hash do round SEGUINTE já calculado pelo
        // host (_myHash era um campo único, sobrescrito a cada round) — causava "DESYNC"
        // espúrio comparando hashes de rounds DIFERENTES entre si. Cada round agora tem seu
        // próprio "balde" de hashes, fechado e comparado só entre si, independente de atraso.
        private readonly Dictionary<int, Dictionary<ulong, ulong>> _hashesByRound = new Dictionary<int, Dictionary<ulong, ulong>>();
        private int _currentRound;
        private int _hashesExpected;

        // ---- Resync (snapshot) ----
        private const int ChunkSize = 3000; // bytes por chunk (margem abaixo do MTU do Relay)
        private byte[] _snapshotBuffer;
        private int _snapshotTotal;

        // ---- AFK tracker ----
        private readonly Dictionary<ulong, int> _afkRounds = new Dictionary<ulong, int>();

        public override void OnNetworkSpawn()
        {
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        public void Init(RoundManager round, List<Unit> units)
        {
            _round = round;
            _units = units;
        }

        /// <summary>Id local REAL (NGO); RuntimeMultiplayerSession pode ter sido capturado cedo.</summary>
        private static ulong LocalId =>
            NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId
                                             : RuntimeMultiplayerSession.LocalClientId;

        /// <summary>
        /// Fiação preguiçosa: no CLIENTE este objeto pode ser replicado DEPOIS do
        /// PlacementSync.OnBattleStart (Init nunca roda) — sem isto, OnPlanningComplete
        /// estourava em _units==null, o plano do cliente nunca era enviado e o round
        /// rodava com o plano dele VAZIO ("comandos do jogador 2 não funcionam").
        /// </summary>
        private bool EnsureWired()
        {
            if (_round == null)
                _round = UnityEngine.Object.FindAnyObjectByType<RoundManager>();
            if (_units == null || _units.Count == 0)
            {
                _units = new List<Unit>(UnitRegistry.AllUnits);
                if (_units.Count > 0)
                    Debug.Log($"[Lockstep] fiação preguiçosa: {_units.Count} unidades via UnitRegistry");
            }
            if (_round == null || _units == null || _units.Count == 0)
            {
                Debug.LogError("[Lockstep] EnsureWired falhou: RoundManager/unidades indisponíveis");
                return false;
            }
            return true;
        }

        // =========================================================================
        // Chamado pelo RoundManager ao fim do PLANNING (somente em MP)
        // =========================================================================
        public void OnPlanningComplete()
        {
            if (!RuntimeMultiplayerSession.IsMultiplayer) return;
            if (!EnsureWired()) return;

            // Serializa o plano das unidades do jogador local e envia
            var wire = new RoundPlansWire();
            bool hasAnyAction = false;
            int nMoves = 0, nAtks = 0, nSpells = 0;
            foreach (var u in _units)
            {
                if (u.ownerId != LocalId) continue;
                wire.plans.Add(UnitPlanWire.FromUnit(u));
                if (u.actionSequence.Count > 0) hasAnyAction = true;
                nMoves += u.plannedMoveCount; nAtks += u.plannedAttacks.Count; nSpells += u.plannedSpells.Count;
            }
            Debug.Log($"[Lockstep] enviando plano (clientId={LocalId}): unidades={wire.plans.Count} moves={nMoves} atks={nAtks} spells={nSpells}");

            // Track AFK
            ulong myId = LocalId;
            if (!hasAnyAction)
            {
                _afkRounds.TryGetValue(myId, out int cnt);
                _afkRounds[myId] = cnt + 1;
                if (_afkRounds[myId] >= 3)
                    ChatAfkWarningServerRpc(myId);
            }
            else
            {
                _afkRounds[myId] = 0;
            }

            string json = JsonUtility.ToJson(wire);
            byte[] gz   = NetCompression.GzipCompress(Encoding.UTF8.GetBytes(json));
            SubmitPlanServerRpc(gz);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitPlanServerRpc(byte[] gz, ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!_collectingPlans)
            {
                Debug.LogWarning($"[Lockstep] plano de clientId={sender} chegou FORA da janela de coleta — ignorado");
                return;
            }
            if (!_receivedPlans.ContainsKey(sender))
                _receivedPlans[sender] = gz;

            Debug.Log($"[Lockstep] plano recebido de clientId={sender} ({gz.Length} bytes) — {_receivedPlans.Count}/{PlayerCount()}");

            if (AllSlotsHaveSubmitted())
            {
                // Fechamento antecipado: cancela o timeout pendente desta coleta ANTES de
                // broadcastar — senão ele fica vivo e pode fechar a rodada seguinte cedo demais.
                if (_timeoutCoroutine != null) { StopCoroutine(_timeoutCoroutine); _timeoutCoroutine = null; }
                StartCoroutine(BroadcastRound());
            }
        }

        /// <summary>
        /// Verdade mais forte que "contagem >= N": confere que TODO clientId presente em
        /// RoomManager.Slots (a lista de jogadores da sala) está em _receivedPlans — não só
        /// que o NÚMERO de planos recebidos bate com uma contagem qualquer. Protege contra
        /// qualquer cenário onde a contagem coincida mas os remetentes reais não sejam os
        /// jogadores esperados (ex.: duplicidade). Ver PlayerCount() para o porquê de não
        /// usar NetworkManager.ConnectedClientsIds aqui.
        /// </summary>
        private bool AllSlotsHaveSubmitted()
        {
            if (RoomManager.Instance == null) return _receivedPlans.Count >= PlayerCount();
            var slots = RoomManager.Instance.Slots;
            if (slots.Count == 0) return false;
            foreach (var slot in slots)
                if (!_receivedPlans.ContainsKey(slot.ClientId)) return false;
            return true;
        }

        /// <summary>Fonte de verdade para "quantos jogadores esperar" — RoomManager.Slots
        /// (NetworkList da sala), com fallback para ConnectedClientsIds só se o RoomManager
        /// ainda não existir por algum motivo.</summary>
        private static int PlayerCount()
        {
            if (RoomManager.Instance != null) return RoomManager.Instance.Slots.Count;
            return NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;
        }

        // =========================================================================
        // Início da coleta (chamado pelo RoundManager ao entrar em Planning em MP)
        // =========================================================================
        public void BeginCollection(int expectedPlayers)
        {
            if (!IsServer) return;
            _receivedPlans.Clear();
            _collectingPlans = true;
            _expectedCount   = expectedPlayers;
            _collectionTimer = 0f;
            // Defensivo: se por algum motivo ainda houver um timeout pendente de uma coleta
            // anterior (não deveria, já que fechamento antecipado cancela o dele), cancela antes
            // de iniciar um novo — nunca mais de um CollectionTimeout vivo por vez.
            if (_timeoutCoroutine != null) StopCoroutine(_timeoutCoroutine);
            _timeoutCoroutine = StartCoroutine(CollectionTimeout());
        }

        private IEnumerator CollectionTimeout()
        {
            // Aguarda planningTime + HostExtraTimeout — a mesma duração que os clientes esperam
            float planningTime = RuntimeMultiplayerSession.CurrentConfig.planningTime;
            float startedAt = Time.realtimeSinceStartup;
            Debug.Log($"[Lockstep] CollectionTimeout iniciado: planningTime={planningTime}s (+{HostExtraTimeout}s extra) — dispara em {planningTime + HostExtraTimeout}s");
            yield return new WaitForSeconds(planningTime + HostExtraTimeout);
            float elapsed = Time.realtimeSinceStartup - startedAt;
            if (_collectingPlans)
            {
                Debug.Log($"[Lockstep] CollectionTimeout disparou após {elapsed:0.0}s real (esperado {planningTime + HostExtraTimeout}s) — fechando coleta por timeout");
                _timeoutCoroutine = null;
                yield return BroadcastRound();
            }
            else
            {
                Debug.Log($"[Lockstep] CollectionTimeout acordou após {elapsed:0.0}s mas a coleta já tinha fechado antes (todos confirmaram)");
            }
        }

        private IEnumerator BroadcastRound()
        {
            _collectingPlans = false;
            _currentRound++;

            // Seed novo para o round
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            // Monta envelope
            var envelope = new RoundPlansWire { roundSeed = seed, roundNum = _currentRound };
            foreach (var kv in _receivedPlans)
            {
                try
                {
                    string json = Encoding.UTF8.GetString(NetCompression.GzipDecompress(kv.Value));
                    var partial = JsonUtility.FromJson<RoundPlansWire>(json);
                    if (partial?.plans != null)
                    {
                        envelope.plans.AddRange(partial.plans);
                        int m = 0, a = 0, s = 0;
                        foreach (var p in partial.plans) { m += p.plannedMoveCount; a += p.attacks.Count; s += p.spells.Count; }
                        Debug.Log($"[Lockstep] plano de clientId={kv.Key}: unidades={partial.plans.Count} moves={m} atks={a} spells={s}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Lockstep] Falha ao desserializar plano do client {kv.Key}: {e.Message}");
                }
            }
            Debug.Log($"[Lockstep] broadcast round {_currentRound}: {envelope.plans.Count} planos de {_receivedPlans.Count} jogadores (seed={seed})");

            string envJson = JsonUtility.ToJson(envelope);
            byte[] gz = NetCompression.GzipCompress(Encoding.UTF8.GetBytes(envJson));
            ExecuteRoundClientRpc(gz);
            yield break;
        }

        [ClientRpc]
        private void ExecuteRoundClientRpc(byte[] gz)
        {
            try
            {
                if (!EnsureWired()) return;
                string json     = Encoding.UTF8.GetString(NetCompression.GzipDecompress(gz));
                var envelope    = JsonUtility.FromJson<RoundPlansWire>(json);
                if (envelope == null) { Debug.LogError("[Lockstep] envelope nulo"); return; }

                // Sincroniza o contador local de round com o do host (ver comentário em
                // RoundPlansWire.roundNum) — sem isto, o cliente sempre reportava "round 0".
                _currentRound = envelope.roundNum;

                // Aplica os planos de TODAS as unidades
                int applied = 0;
                foreach (var pw in envelope.plans)
                {
                    var unit = UnitRegistry.Get(pw.unitId);
                    if (unit == null) { Debug.LogWarning($"[Lockstep] plano p/ unitId={pw.unitId} sem unidade local"); continue; }
                    UnitPlanWire.ApplyToUnit(pw, unit);
                    applied++;
                }
                Debug.Log($"[Lockstep] executando round: {applied}/{envelope.plans.Count} planos aplicados (seed={envelope.roundSeed})");

                // Semeia RNG e manda o RoundManager executar a fase de ação
                _round?.RunActionPhaseMp(envelope.roundSeed);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Lockstep] Falha ao processar ExecuteRound: {e}");
            }
        }

        // =========================================================================
        // Hash de estado por round
        // =========================================================================
        public void ReportRoundHash(ulong hash)
        {
            LogPlayerStats();
            ReportHashServerRpc(hash, _currentRound);
        }

        /// <summary>[MP-STATS] — estado de cada unidade por dono ao fim do round (console).</summary>
        private void LogPlayerStats()
        {
            if (_units == null) return;
            var sb = new StringBuilder();
            sb.Append($"[MP-STATS] fim do round {_currentRound} (visão de clientId={LocalId}):\n");
            var sorted = new List<Unit>(_units);
            sorted.Sort((a, b) => UnitRegistry.GetId(a).CompareTo(UnitRegistry.GetId(b)));
            foreach (var u in sorted)
            {
                sb.Append($"  uid={UnitRegistry.GetId(u)} '{u.unitName}' dono={u.ownerId} " +
                          $"HP={u.currentHP}/{u.stats.MaxHP} MP={u.currentMana}/{u.stats.MaxMana} " +
                          $"pos=({u.anchor.x},{u.anchor.y}){(u.IsDead ? " MORTO" : "")}\n");
            }
            Debug.Log(sb.ToString());
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReportHashServerRpc(ulong hash, int roundNum, ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!_hashesByRound.TryGetValue(roundNum, out var bucket))
            {
                bucket = new Dictionary<ulong, ulong>();
                _hashesByRound[roundNum] = bucket;
            }
            bucket[sender] = hash;

            // Aguarda todos os hashes DESTE round específico (outros rounds pendentes,
            // se houver, ficam em seus próprios baldes e não interferem)
            int connected = PlayerCount();
            if (bucket.Count < connected) return;

            bool allMatch = true;
            ulong reference = 0;
            bool first = true;
            foreach (var kv in bucket)
            {
                if (first) { reference = kv.Value; first = false; continue; }
                if (kv.Value != reference) { allMatch = false; break; }
            }

            if (allMatch)
            {
                Debug.Log($"[Lockstep] hash OK round {roundNum} = {reference:X16}");
                HashResultClientRpc(true, roundNum);
                // BARREIRA: só agora, com o hash de TODOS os jogadores confirmado, é seguro
                // liberar o próximo round para todo mundo (ver RoundManager.ProceedToNextRoundMp).
                AdvanceRoundClientRpc();
            }
            else
            {
                // Log de breakdown
                var sb = new StringBuilder();
                sb.Append($"[Lockstep] DESYNC round {roundNum}:\n");
                foreach (var kv in bucket)
                    sb.Append($"  client {kv.Key}: {kv.Value:X16}\n");
                Debug.LogError(sb.ToString());

                // Broadcast hash result para logar nos clientes também
                HashResultClientRpc(false, roundNum);

                // Resync: serializa estado canônico do host e envia; libera o próximo round
                // só DEPOIS que o snapshot terminar de ser enviado (ver fim de SendStateSnapshot).
                StartCoroutine(SendStateSnapshot());
            }
            _hashesByRound.Remove(roundNum); // limpa só este round — outros pendentes seguem intactos
        }

        [ClientRpc]
        private void AdvanceRoundClientRpc()
        {
            EnsureWired();
            _round?.ProceedToNextRoundMp();
        }

        [ClientRpc]
        private void HashResultClientRpc(bool ok, int roundNum)
        {
            if (!ok)
            {
                // Dump local para debug
                var sb = new StringBuilder();
                sb.Append($"[Lockstep] DESYNC round {roundNum} — estado local:\n");
                var sorted = new List<Unit>(_units);
                sorted.Sort((a, b) => UnitRegistry.GetId(a).CompareTo(UnitRegistry.GetId(b)));
                foreach (var u in sorted)
                    sb.Append($"  uid={UnitRegistry.GetId(u)} HP={u.currentHP} MP={u.currentMana} anchor=({u.anchor.x},{u.anchor.y})\n");
                Debug.LogError(sb.ToString());
            }
        }

        // =========================================================================
        // Hash FNV-1a do estado (unitId/HP/mana/anchor/isDead) — ordem por unitId
        // =========================================================================
        public static ulong ComputeStateHash(List<Unit> units)
        {
            const ulong FnvOffsetBasis = 14695981039346656037UL;
            const ulong FnvPrime       = 1099511628211UL;

            var sorted = new List<Unit>(units);
            sorted.Sort((a, b) => UnitRegistry.GetId(a).CompareTo(UnitRegistry.GetId(b)));

            ulong hash = FnvOffsetBasis;
            foreach (var u in sorted)
            {
                hash = FnvMix(hash, FnvPrime, (ulong)UnitRegistry.GetId(u));
                hash = FnvMix(hash, FnvPrime, (ulong)u.currentHP);
                hash = FnvMix(hash, FnvPrime, (ulong)u.currentMana);
                hash = FnvMix(hash, FnvPrime, (ulong)(uint)u.anchor.x);
                hash = FnvMix(hash, FnvPrime, (ulong)(uint)u.anchor.y);
                hash = FnvMix(hash, FnvPrime, u.IsDead ? 1UL : 0UL);
            }
            return hash;
        }

        private static ulong FnvMix(ulong hash, ulong prime, ulong val)
        {
            hash ^= val;
            hash *= prime;
            return hash;
        }

        // =========================================================================
        // Snapshot resync (host → cliente)
        // =========================================================================
        private IEnumerator SendStateSnapshot()
        {
            var snapshot = new StateSnapshot();
            var sorted = new List<Unit>(_units);
            sorted.Sort((a, b) => UnitRegistry.GetId(a).CompareTo(UnitRegistry.GetId(b)));
            foreach (var u in sorted)
            {
                snapshot.entries.Add(new UnitStateEntry
                {
                    unitId   = UnitRegistry.GetId(u),
                    hp       = u.currentHP,
                    mana     = u.currentMana,
                    anchorX  = u.anchor.x,
                    anchorY  = u.anchor.y,
                    isDead   = u.IsDead,
                });
            }

            string json = JsonUtility.ToJson(snapshot);
            byte[] gz   = NetCompression.GzipCompress(Encoding.UTF8.GetBytes(json));

            // Envia em chunks
            int total  = gz.Length;
            int offset = 0;
            int seq    = 0;
            while (offset < total)
            {
                int len   = Mathf.Min(ChunkSize, total - offset);
                var chunk = new byte[len];
                Array.Copy(gz, offset, chunk, 0, len);
                bool last = (offset + len) >= total;
                StateSnapshotChunkClientRpc(chunk, seq, total, last);
                offset += len;
                seq++;
                yield return null; // spread over frames
            }

            // Só libera o próximo round DEPOIS que o snapshot completo foi transmitido —
            // senão um cliente poderia começar o round seguinte antes de aplicar a correção.
            AdvanceRoundClientRpc();
        }

        [ClientRpc]
        private void StateSnapshotChunkClientRpc(byte[] chunk, int seq, int totalBytes, bool last)
        {
            if (IsServer) return; // host não precisa aplicar

            if (seq == 0)
            {
                _snapshotBuffer = new byte[totalBytes];
                _snapshotTotal  = 0;
            }
            if (_snapshotBuffer == null) return;

            Array.Copy(chunk, 0, _snapshotBuffer, _snapshotTotal, chunk.Length);
            _snapshotTotal += chunk.Length;

            if (!last) return;

            try
            {
                string json     = Encoding.UTF8.GetString(NetCompression.GzipDecompress(_snapshotBuffer));
                var snapshot    = JsonUtility.FromJson<StateSnapshot>(json);
                foreach (var entry in snapshot.entries)
                {
                    var unit = UnitRegistry.Get(entry.unitId);
                    if (unit == null) continue;
                    unit.currentHP   = entry.hp;
                    unit.currentMana = entry.mana;
                    unit.anchor      = new Vector2Int(entry.anchorX, entry.anchorY);
                    unit.SnapToAnchor();
                    if (entry.isDead && !unit.IsDead) unit.TakeDamage(unit.currentHP + 1);
                }
                Debug.Log("[Lockstep] Resync aplicado.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Lockstep] Falha ao aplicar snapshot: {e}");
            }
        }

        // =========================================================================
        // AFK warning
        // =========================================================================
        [ServerRpc(RequireOwnership = false)]
        private void ChatAfkWarningServerRpc(ulong clientId)
        {
            string name = "Jogador";
            if (RoomManager.Instance != null)
            {
                var slots = RoomManager.Instance.Slots;
                for (int i = 0; i < slots.Count; i++)
                    if (slots[i].ClientId == clientId) { name = slots[i].PlayerName.ToString(); break; }
            }
            ChatAfkWarningClientRpc(name);
        }

        [ClientRpc]
        private void ChatAfkWarningClientRpc(string playerName)
        {
            // Loga no BattleHUD se disponível
            var hud = UnityEngine.Object.FindObjectOfType<BattleHUD>();
            hud?.LogAction($"<color=#ffaa44>[Chat] {playerName} parece ausente (3 rounds sem ação)</color>");
        }

        // =========================================================================
        // Desconexão de jogador durante batalha
        // =========================================================================
        public void EliminateDisconnectedPlayer(ulong clientId)
        {
            if (!IsServer) return;
            EliminatePlayerClientRpc(clientId);
        }

        [ClientRpc]
        private void EliminatePlayerClientRpc(ulong clientId)
        {
            foreach (var u in _units)
            {
                if (u.ownerId != clientId) continue;
                if (!u.IsDead)
                {
                    u.TakeDamage(u.currentHP + 9999); // elimina garantido
                    Debug.Log($"[Lockstep] Unidade {u.unitName} eliminada por desconexão de {clientId}");
                }
            }
        }
    }

    // ---- DTOs de snapshot -------------------------------------------------------

    [Serializable]
    public class UnitStateEntry
    {
        public uint unitId;
        public int  hp;
        public int  mana;
        public int  anchorX;
        public int  anchorY;
        public bool isDead;
    }

    [Serializable]
    public class StateSnapshot
    {
        public List<UnitStateEntry> entries = new List<UnitStateEntry>();
    }
}
