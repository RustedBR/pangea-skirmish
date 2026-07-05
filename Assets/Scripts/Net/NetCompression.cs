// Net/NetCompression.cs
// Helpers GZip compartilhados por CollabMapSync e LockstepBattleSync.

using System.IO;
using System.IO.Compression;

namespace PangeaSkirmish
{
    public static class NetCompression
    {
        public static byte[] GzipCompress(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
                gz.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        public static byte[] GzipDecompress(byte[] data)
        {
            using var ms  = new MemoryStream(data);
            using var gz  = new GZipStream(ms, CompressionMode.Decompress);
            using var out_ = new MemoryStream();
            gz.CopyTo(out_);
            return out_.ToArray();
        }
    }
}
