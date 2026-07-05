// Net/CollabMapSync.cs
// NetworkBehaviour que sincroniza pinceladas do mapa colaborativo.
// Spawn: host cria junto do RoomManager (via NetBootstrap.SpawnRoomManager).
// Convergência por eco: o cliente NÃO aplica localmente antes do RPC —
// aplica apenas quando recebe o ClientRpc de volta (inclusive o próprio autor).

using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace PangeaSkirmish
{
    // -------------------------------------------------------------------------
    // Tipo de operação de pintura
    // -------------------------------------------------------------------------
    public enum PaintOpKind : byte
    {
        Paint  = 0,  // pinta tile (tileIndex, height, isVoid=false)
        Erase  = 1,  // apaga → void
        Height = 2,  // ajusta altura apenas
    }

    // -------------------------------------------------------------------------
    // Struct serializado pela rede: uma operação de pintura em (x,y)
    // -------------------------------------------------------------------------
    public struct PaintOp : INetworkSerializable
    {
        public int X;
        public int Y;
        public int TileIndex;
        public int Height;
        public bool IsVoid;
        public PaintOpKind Kind;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Y);
            serializer.SerializeValue(ref TileIndex);
            serializer.SerializeValue(ref Height);
            serializer.SerializeValue(ref IsVoid);

            // PaintOpKind como byte
            byte k = (byte)Kind;
            serializer.SerializeValue(ref k);
            Kind = (PaintOpKind)k;
        }
    }

    // -------------------------------------------------------------------------
    // CollabMapSync — NetworkBehaviour
    // -------------------------------------------------------------------------
    public class CollabMapSync : NetworkBehaviour
    {
        public static CollabMapSync Instance { get; private set; }

        // ---- Mapa canônico (host) --------------------------------------------
        private MapData _canonical;

        // ---- SandboxController local (injetado quando a cena Sandbox carregar) --
        private SandboxController _sandboxCtrl;

        // ---- Constantes de chunk (snapshot GZip) ----------------------------
        private const int ChunkSize = 4096;

        // ---- Evento disparado quando snapshot remoto é aplicado -------------
        public event Action OnSnapshotApplied;

        public override void OnNetworkSpawn()
        {
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        // ---- Injeção do SandboxController (chamado pelo SandboxController.Start) ----
        public void RegisterSandbox(SandboxController ctrl, MapData map)
        {
            _sandboxCtrl = ctrl;
            if (IsServer) _canonical = map;
        }

        // =========================================================================
        // Operação de pintura
        // =========================================================================

        [ServerRpc(RequireOwnership = false)]
        public void PaintOpServerRpc(PaintOp op)
        {
            Debug.Log($"[MP] Tile pintado (host): ({op.X},{op.Y}) tile={op.TileIndex} altura={op.Height} void={op.IsVoid}");
            // Host aplica no canônico
            ApplyOpToMap(_canonical, op);

            // Ecoa para todos (incluindo o autor)
            PaintOpClientRpc(op);
        }

        [ClientRpc]
        private void PaintOpClientRpc(PaintOp op)
        {
            // Todos aplicam localmente via SandboxController
            _sandboxCtrl?.ApplyPaintOp(op);
        }

        // =========================================================================
        // Expansão de grid
        // =========================================================================

        [ServerRpc(RequireOwnership = false)]
        public void ExpandGridServerRpc(int newW, int newH)
        {
            if (_canonical == null) return;

            // Recria o MapData com as novas dimensões preservando os dados existentes
            var expanded = MapData.CreateEmpty(newW, newH);
            // Copia os tiles do mapa canônico existente
            int oldW = _canonical.width;
            int oldH = _canonical.height;
            for (int x = 0; x < Mathf.Min(oldW, newW); x++)
            for (int y = 0; y < Mathf.Min(oldH, newH); y++)
            {
                int ni = expanded.Flat(x, y);
                int oi = _canonical.Flat(x, y);
                expanded.tileIndices[ni] = _canonical.tileIndices[oi];
                expanded.heights[ni]     = _canonical.heights[oi];
                expanded.voidCells[ni]   = _canonical.voidCells[oi];
            }
            expanded.mapName = _canonical.mapName;
            _canonical = expanded;

            ExpandGridClientRpc(newW, newH);
        }

        [ClientRpc]
        private void ExpandGridClientRpc(int newW, int newH)
        {
            _sandboxCtrl?.ApplyGridExpand(newW, newH);
        }

        // =========================================================================
        // Snapshot para late-joiner
        // =========================================================================

        [ServerRpc(RequireOwnership = false)]
        public void RequestMapSnapshotServerRpc(ServerRpcParams rpc = default)
        {
            if (_canonical == null) return;
            ulong requesterId = rpc.Receive.SenderClientId;
            StartCoroutine(SendSnapshotChunks(requesterId));
        }

        private IEnumerator SendSnapshotChunks(ulong targetId)
        {
            // Serializa e comprime
            string json = JsonUtility.ToJson(_canonical);
            byte[] raw  = Encoding.UTF8.GetBytes(json);
            byte[] compressed = GzipCompress(raw);

            int total = Mathf.CeilToInt((float)compressed.Length / ChunkSize);
            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { targetId } }
            };

            for (int i = 0; i < total; i++)
            {
                int offset = i * ChunkSize;
                int len    = Mathf.Min(ChunkSize, compressed.Length - offset);
                var chunk  = new byte[len];
                Array.Copy(compressed, offset, chunk, 0, len);
                MapChunkClientRpc(i, total, chunk, target);
                yield return null; // um frame entre chunks para não saturar o canal
            }
        }

        // ---- Reconstrução no lado do cliente ---------------------------------
        private byte[] _chunkBuffer;
        private int    _chunkTotal;
        private int    _chunksReceived;

        [ClientRpc]
        private void MapChunkClientRpc(int index, int total, byte[] data, ClientRpcParams _ = default)
        {
            if (index == 0)
            {
                // Calcular tamanho estimado: total * ChunkSize (máximo superior)
                _chunkBuffer   = new byte[total * ChunkSize];
                _chunkTotal    = total;
                _chunksReceived = 0;
            }

            Array.Copy(data, 0, _chunkBuffer, index * ChunkSize, data.Length);
            _chunksReceived++;

            if (_chunksReceived >= _chunkTotal)
            {
                // Trimmar o buffer (último chunk pode ser menor)
                // Detectar tamanho real: decomprimir começa com GZip magic bytes
                try
                {
                    byte[] decompressed = GzipDecompress(_chunkBuffer);
                    string json = Encoding.UTF8.GetString(decompressed);
                    var map = JsonUtility.FromJson<MapData>(json);
                    if (map != null)
                    {
                        if (IsServer) _canonical = map;
                        RuntimeMultiplayerSession.CollabMap = map;
                        _sandboxCtrl?.ApplyFullSnapshot(map);
                        OnSnapshotApplied?.Invoke();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CollabMapSync] Erro ao reconstruir snapshot: {e.Message}");
                }
            }
        }

        // =========================================================================
        // Snapshot final: host envia a TODOS e avança para CharCreation
        // =========================================================================
        public void SendFinalSnapshotAndAdvance()
        {
            if (!IsServer) return;
            RuntimeMultiplayerSession.CollabMap = _canonical;
            StartCoroutine(SendFinalSnapshot());
        }

        private IEnumerator SendFinalSnapshot()
        {
            string json = JsonUtility.ToJson(_canonical);
            byte[] raw  = Encoding.UTF8.GetBytes(json);
            byte[] compressed = GzipCompress(raw);

            int total = Mathf.CeilToInt((float)compressed.Length / ChunkSize);

            // Broadcast (sem target = todos)
            var broadcast = new ClientRpcParams();
            for (int i = 0; i < total; i++)
            {
                int offset = i * ChunkSize;
                int len    = Mathf.Min(ChunkSize, compressed.Length - offset);
                var chunk  = new byte[len];
                Array.Copy(compressed, offset, chunk, 0, len);
                FinalChunkClientRpc(i, total, chunk);
                yield return null;
            }
        }

        private byte[] _finalBuffer;
        private int _finalTotal;
        private int _finalReceived;

        [ClientRpc]
        private void FinalChunkClientRpc(int index, int total, byte[] data)
        {
            if (index == 0)
            {
                _finalBuffer   = new byte[total * ChunkSize];
                _finalTotal    = total;
                _finalReceived = 0;
            }

            Array.Copy(data, 0, _finalBuffer, index * ChunkSize, data.Length);
            _finalReceived++;

            if (_finalReceived >= _finalTotal)
            {
                try
                {
                    byte[] decompressed = GzipDecompress(_finalBuffer);
                    string json = Encoding.UTF8.GetString(decompressed);
                    var map = JsonUtility.FromJson<MapData>(json);
                    if (map != null)
                    {
                        RuntimeMultiplayerSession.CollabMap = map;
                        RuntimeMap.Selected = map;
                        Debug.Log("[CollabMapSync] Snapshot final aplicado. RuntimeMap.Selected atualizado.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CollabMapSync] Erro ao aplicar snapshot final: {e.Message}");
                }
            }
        }

        // =========================================================================
        // GZip helpers
        // =========================================================================
        private static byte[] GzipCompress(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
                gz.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        private static byte[] GzipDecompress(byte[] data)
        {
            using var ms  = new MemoryStream(data);
            using var gz  = new GZipStream(ms, CompressionMode.Decompress);
            using var out_ = new MemoryStream();
            gz.CopyTo(out_);
            return out_.ToArray();
        }

        // =========================================================================
        // Aplicar op ao MapData (helper compartilhado host+cliente via SandboxController)
        // =========================================================================
        public static void ApplyOpToMap(MapData map, PaintOp op)
        {
            if (map == null) return;
            if (op.X < 0 || op.Y < 0 || op.X >= map.width || op.Y >= map.height) return;

            int flat = map.Flat(op.X, op.Y);
            switch (op.Kind)
            {
                case PaintOpKind.Erase:
                    map.voidCells[flat] = true;
                    break;
                case PaintOpKind.Paint:
                    map.tileIndices[flat] = op.TileIndex;
                    map.heights[flat]     = op.Height;
                    map.voidCells[flat]   = false;
                    break;
                case PaintOpKind.Height:
                    map.heights[flat] = op.Height;
                    break;
            }
        }
    }
}
