using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using SFS.World;
using FrameEmbededState.Lib;
using FrameEmbededState; // Needed for OverlayRenderMode

namespace FrameEmbededState
{
    /// <summary>
    /// Encodes a text payload into the current frame using the same tile/packet layout as the Python reference.
    /// Works by copying frame.Source -> frame.Result, then watermarking frame.Result in-place.
    /// </summary>
    public class FrameWatermarkEncoder : BaseShaderEffect
    {
        // Matches Python defaults (bs=8, s=8.0, channel=-1).  :contentReference[oaicite:4]{index=4}
        const int BS = 8;
        const float StrengthS = 2010f;
        const int Channel = -1;

        // Optional: limit how often we rebuild (compress/plan/packets) when payload changes rapidly.
        const float MinRebuildInterval = 10.00f;

        static readonly State _state = new State();

        static FrameWatermarkEncoder _instance = new FrameWatermarkEncoder(); // auto-register

        public FrameWatermarkEncoder() : base("FrameWatermarkEncoder", "Encodes gameplay text into the frame (DCT watermark).") { }

        protected override void ApplyEffect(VisualOverlayManager.VisualOverlaySettings settings)
        {   // Register the watermark encoder overlay with OnTop mode
            if (_state.Registered)
                return;

            settings.Enable = true;
            settings.Execute = Execute;
            settings.RenderMode = OverlayRenderMode.OnTop;

            _state.Registered = true;
        }

        static void Execute(VisualOverlayManager.FrameData frame)
        {   // Encode payload into frame.Result using DCT watermark, following film look pattern

            // Always copy source to result before watermarking
            frame.Source.CopyTo(frame.Result);

            int w = frame.Width, h = frame.Height;
            int blocksX = w / BS, blocksY = h / BS;

            // Python requires at least 32x32 blocks for a valid watermark
            if (blocksX < 32 || blocksY < 32)
                return;

            string payload = BuildPayload();

            float now = Time.unscaledTime;
            if (_state.PreparedForPayload != payload)
            {   // Prepare state for new payload if needed
                if (MinRebuildInterval <= 0f || now >= _state.NextAllowedRebuildTime)
                {   _state.NextAllowedRebuildTime = now + MinRebuildInterval;
                    PrepareMessageState(payload, blocksX, blocksY);
                }
            }
            else if (!_state.Plan.IsValid)
                PrepareMessageState(payload, blocksX, blocksY);

            if (!_state.Plan.IsValid)
                return;

            // Apply watermark in-place to frame.Result
            EncoderCore.Apply(
                frame.Result,
                w, h,
                blocksX, blocksY,
                _state,
                StrengthS,
                Channel
            );
        }

        static string BuildPayload()
        {
            try
            {
                return
                    PlayerController.main.player.Value.name + "\n" +
                    PlayerController.main.player.Value.location.planet.Value.name + "\n" +
                    PlayerController.main.player.Value.mapPlayer.EncounterText + "\n";
            }
            catch
            {
                return "NO_PLAYER\nNO_PLANET\nNO_ENCOUNTER\n";
            }
        }

        static void PrepareMessageState(string payload, int blocksX, int blocksY)
        {
            _state.PreparedForPayload = payload;

            byte[] utf8 = Encoding.UTF8.GetBytes(payload);

            // Python uses zlib.compress(data, 9). :contentReference[oaicite:7]{index=7}
            // To stay dependency-free and correct, we emit a valid ZLIB stream with "stored" DEFLATE blocks.
            // (Still a valid zlib stream; just uncompressed DEFLATE blocks.)
            _state.Compressed = ZlibCompressStored(utf8, useBestHeader: true);

            _state.Plan = EncodedPlan.Build(_state.Compressed, blocksX, blocksY);
            if (!_state.Plan.IsValid)
            {
                _state.ClearDerived();
                return;
            }

            // Build derived caches for this plan/message.
            _state.BuildDerived();
        }

        /// <summary>
        /// Builds a valid RFC1950 ZLIB stream containing RFC1951 "stored" (uncompressed) DEFLATE blocks.
        /// Header uses 0x78 0xDA (same as typical zlib "best" header) to resemble Python level-9 streams.
        /// </summary>
        static byte[] ZlibCompressStored(byte[] input, bool useBestHeader)
        {
            if (input == null) input = Array.Empty<byte>();

            using (var ms = new MemoryStream(input.Length + 64))
            {
                // ZLIB header (CMF/FLG). 0x78 0xDA is common for "best" compression. :contentReference[oaicite:8]{index=8}
                // Decompressors do not require this to match the internal block type; it is just a hint.
                if (useBestHeader)
                {
                    ms.WriteByte(0x78);
                    ms.WriteByte(0xDA);
                }
                else
                {
                    // "fastest" header 0x78 0x01 (also valid).
                    ms.WriteByte(0x78);
                    ms.WriteByte(0x01);
                }

                int offset = 0;
                while (offset < input.Length)
                {
                    int chunk = Math.Min(65535, input.Length - offset);
                    bool final = (offset + chunk) >= input.Length;

                    // Stored block header: 3 bits (BFINAL + BTYPE=00), then pad to byte boundary.
                    // This becomes 0x01 for final block, 0x00 for non-final.
                    ms.WriteByte(final ? (byte)0x01 : (byte)0x00);

                    // LEN and NLEN are little-endian.
                    ushort len = (ushort)chunk;
                    ushort nlen = (ushort)~len;

                    ms.WriteByte((byte)(len & 0xFF));
                    ms.WriteByte((byte)((len >> 8) & 0xFF));
                    ms.WriteByte((byte)(nlen & 0xFF));
                    ms.WriteByte((byte)((nlen >> 8) & 0xFF));

                    ms.Write(input, offset, chunk);
                    offset += chunk;
                }

                // Adler32 trailer is big-endian in zlib.
                uint adler = HashBridge.Adler32(input);
                WriteBE_U32(ms, adler);

                return ms.ToArray();
            }
        }

        static void WriteBE_U32(Stream s, uint v)
        {
            s.WriteByte((byte)((v >> 24) & 0xFF));
            s.WriteByte((byte)((v >> 16) & 0xFF));
            s.WriteByte((byte)((v >> 8) & 0xFF));
            s.WriteByte((byte)(v & 0xFF));
        }

        sealed class State
        {
            public bool Registered;

            public string PreparedForPayload;
            public float NextAllowedRebuildTime;

            public byte[] Compressed;
            public EncodedPlan Plan;

            // Derived caches (rebuilt when payload/plan changes)
            public BlockCoord[] DataCoords;         // coords in a tile where packet bits go
            public BootWrite[] BootWrites;          // coords/bits for boot region (already replicated and mapped)
            public bool[][] PacketBitsPerIndex;     // per packet index: repeated bits trimmed to DataCoords length

            public int TotalPackets => Plan.TotalPackets;

            public void ClearDerived()
            {
                DataCoords = null;
                BootWrites = null;
                PacketBitsPerIndex = null;
            }

            public void BuildDerived()
            {
                ClearDerived();

                int td = Plan.TileDim;
                int red = Plan.Redundancy;
                int maxp = Plan.MaxPayload;

                // Data coords (same as python data_coords(td)). :contentReference[oaicite:9]{index=9}
                DataCoords = EncoderCore.BuildDataCoords(td);

                // Boot writes (same mapping as python BOOT_BITS + BOOT_REP into BOOT_COORDS). :contentReference[oaicite:10]{index=10}
                byte[] bootPacket = EncoderPlan.BuildBoot(td, red);
                bool[] bootBits = BitUtil.UnpackBits(bootPacket);
                BootWrites = EncoderCore.BuildBootWrites(bootBits);

                // Split compressed into chunks and build packet bits for each packet index.
                int total = Plan.TotalPackets;
                PacketBitsPerIndex = new bool[total][];

                for (int i = 0; i < total; i++)
                {
                    int off = i * maxp;
                    int len = Math.Min(maxp, Compressed.Length - off);
                    var payload = new byte[len];
                    Buffer.BlockCopy(Compressed, off, payload, 0, len);

                    byte[] pkt = EncoderPlan.BuildPacket(Plan.MsgId, (ushort)i, (ushort)total, payload, td, red);

                    // Repeat each bit red times, but only keep what fits in DataCoords.
                    PacketBitsPerIndex[i] = BitUtil.RepeatBitsTrim(BitUtil.UnpackBits(pkt), red, DataCoords.Length);
                }
            }
        }

        struct EncodedPlan
        {
            public bool IsValid;
            public int TileDim, Redundancy, MaxPayload, TotalPackets;
            public uint MsgId;

            public static EncodedPlan Build(byte[] compressed, int blocksX, int blocksY, int? forceTileDim = null)
            {
                var plan = new EncodedPlan { IsValid = false };
                if (compressed == null || compressed.Length == 0) return plan;

                // msg_id matches Python:
                // (zlib.crc32(comp) ^ (len(comp) << 8) ^ sum(comp)) & 0xFFFFFFFF :contentReference[oaicite:11]{index=11}
                uint crc = HashBridge.Crc32(compressed);

                ulong sum = 0;
                for (int i = 0; i < compressed.Length; i++) sum += compressed[i];

                uint msgId = unchecked(crc ^ ((uint)compressed.Length << 8) ^ (uint)sum);

                int minTd = Math.Max(20, 16 + 4);
                int maxTd = Math.Min(128, Math.Min(blocksX, blocksY));

                bool haveBest = false;
                long bestS0 = 0;
                int bestTd = 0, bestRed = 0, bestMaxp = 0;

                if (forceTileDim.HasValue)
                {
                    EvaluateTileDim(forceTileDim.Value);
                }
                else
                {
                    for (int td = minTd; td <= maxTd; td++)
                        EvaluateTileDim(td);
                }

                if (!haveBest) return plan;

                int totalPackets = (compressed.Length + bestMaxp - 1) / bestMaxp;

                plan.IsValid = true;
                plan.TileDim = bestTd;
                plan.Redundancy = bestRed;
                plan.MaxPayload = bestMaxp;
                plan.TotalPackets = totalPackets;
                plan.MsgId = msgId;
                return plan;

                void EvaluateTileDim(int td)
                {
                    int tx = blocksX / td;
                    int ty = blocksY / td;
                    if (tx < 2 || ty < 2) return;

                    int cap = DataCoordsCount(td);
                    if (cap <= 0) return;

                    // r from 64 -> 1 (same as Python). :contentReference[oaicite:12]{index=12}
                    for (int r = 64; r >= 1; r--)
                    {
                        int mp = ((cap / r) / 8) - 16 - 4; // PKT_HDR_LEN=16, PKT_CRC_LEN=4 :contentReference[oaicite:13]{index=13}
                        if (mp <= 0) continue;

                        int pkts = (compressed.Length + mp - 1) / mp;
                        if (pkts > tx * ty) continue;

                        long s0 = (long)r * ((tx * ty) / pkts);

                        // lexicographic score: (s0, r, mp) :contentReference[oaicite:14]{index=14}
                        bool better = !haveBest ||
                                      s0 > bestS0 ||
                                      (s0 == bestS0 && r > bestRed) ||
                                      (s0 == bestS0 && r == bestRed && mp > bestMaxp);

                        if (better)
                        {
                            haveBest = true;
                            bestS0 = s0;
                            bestTd = td;
                            bestRed = r;
                            bestMaxp = mp;
                        }
                    }
                }
            }

            static int DataCoordsCount(int td)
            {
                int control = Math.Min(td, 16);
                return td * td - control * control;
            }
        }

        static class EncoderCore
        {
            public const int LOC_SIZE = 9;
            public const int CONTROL_SIZE = 16;
            public const int BOOT_REP = 2;

            static readonly byte[,] LOC_PATTERN = new byte[LOC_SIZE, LOC_SIZE]
            {
                {1,1,1,1,1,1,1,1,1},
                {1,0,0,0,0,0,0,0,1},
                {1,0,1,1,1,1,1,0,1},
                {1,0,1,0,0,0,1,0,1},
                {1,0,1,0,1,0,1,0,1},
                {1,0,1,0,0,0,1,0,1},
                {1,0,1,1,1,1,1,0,1},
                {1,0,0,0,0,0,0,0,1},
                {1,1,1,1,1,1,1,1,1},
            };

            static readonly BlockCoord[] BOOT_COORDS;
            static readonly int[] EMBED_IDXS;
            static readonly float[] EMBED_SIGNS;

            static EncoderCore()
            {
                // Embed positions: identical enumeration/truncation as Python (POS = p[:12]). :contentReference[oaicite:15]{index=15}
                var idxs = new List<int>(12);
                var signs = new List<float>(12);

                for (int u = 2; u < Math.Min(BS - 1, 6); u++)
                {
                    for (int v = 2; v < Math.Min(BS - 1, 6); v++)
                    {
                        int s = u + v;
                        if (s < 4 || s > 8) continue;

                        idxs.Add(u * BS + v);
                        signs.Add(((u + v) & 1) == 0 ? 1f : -1f);

                        if (idxs.Count == 12) goto Done;
                    }
                }

            Done:
                EMBED_IDXS = idxs.ToArray();
                EMBED_SIGNS = signs.ToArray();

                // BOOT_COORDS = all coords in 16x16 except the 9x9 locator region. :contentReference[oaicite:16]{index=16}
                var boot = new List<BlockCoord>(CONTROL_SIZE * CONTROL_SIZE - LOC_SIZE * LOC_SIZE);
                for (int r = 0; r < CONTROL_SIZE; r++)
                {
                    for (int c = 0; c < CONTROL_SIZE; c++)
                    {
                        if (r < LOC_SIZE && c < LOC_SIZE) continue;
                        boot.Add(new BlockCoord((ushort)r, (ushort)c));
                    }
                }
                BOOT_COORDS = boot.ToArray();
            }

            public static BlockCoord[] BuildDataCoords(int td)
            {
                int control = Math.Min(td, CONTROL_SIZE);

                int capacity = td * td - control * control;
                var coords = new BlockCoord[capacity];

                int k = 0;
                for (int r = 0; r < td; r++)
                {
                    for (int c = 0; c < td; c++)
                    {
                        if (r < control && c < control) continue;
                        coords[k++] = new BlockCoord((ushort)r, (ushort)c);
                    }
                }

                return coords;
            }

            public static BootWrite[] BuildBootWrites(bool[] bootBits)
            {
                int maxWrites = Math.Min(bootBits.Length * BOOT_REP, BOOT_COORDS.Length);
                var writes = new BootWrite[maxWrites];

                for (int j = 0; j < maxWrites; j++)
                {
                    bool bit = bootBits[j / BOOT_REP];
                    writes[j] = new BootWrite(BOOT_COORDS[j], bit);
                }

                return writes;
            }

            public static void Apply(
                Unity.Collections.NativeArray<Color32> img,
                int widthPx, int heightPx,
                int blocksX, int blocksY,
                State state,
                float strengthS,
                int channel)
            {   // Apply watermark to the image buffer in-place

                int td = state.Plan.TileDim;
                int tx = blocksX / td;
                int ty = blocksY / td;

                // Cap the number of tiles to avoid GPU overload (e.g., 256x256 tiles max)
                const int MaxTiles = 256 * 256;
                if (tx * ty > MaxTiles)
                {
                    tx = Math.Min(tx, 256);
                    ty = Math.Min(ty, 256);
                }

                if (tx < 2 || ty < 2) return;

                float embedScale = strengthS * 0.15f;

                var dataCoords = state.DataCoords;
                var bootWrites = state.BootWrites;
                var packetBits = state.PacketBitsPerIndex;
                int totalPackets = state.TotalPackets;

                // Parallelize over tiles for performance
                System.Threading.Tasks.Parallel.For(0, ty, tileY =>
                {
                    for (int tileX = 0; tileX < tx; tileX++)
                    {
                        int bi = tileY * tx + tileX;
                        int pi = bi % totalPackets;

                        int ox = tileX * td;
                        int oy = tileY * td;

                        // Locator pattern (9x9)
                        for (int r = 0; r < LOC_SIZE; r++)
                        {
                            for (int c = 0; c < LOC_SIZE; c++)
                            {
                                bool bit = LOC_PATTERN[r, c] != 0;
                                WriteBit(img, widthPx, heightPx, ox + c, oy + r, bit, embedScale, channel, flipY: true, heightPx: heightPx);
                            }
                        }

                        // Boot region (mapped and replicated)
                        for (int i = 0; i < bootWrites.Length; i++)
                        {
                            var bw = bootWrites[i];
                            WriteBit(img, widthPx, heightPx, ox + bw.Coord.C, oy + bw.Coord.R, bw.Bit, embedScale, channel, flipY: true, heightPx: heightPx);
                        }

                        // Packet bits for this tile (already repeated and trimmed)
                        bool[] pb = packetBits[pi];
                        int bitCount = Math.Min(pb.Length, dataCoords.Length);

                        for (int i = 0; i < bitCount; i++)
                        {
                            var dc = dataCoords[i];
                            WriteBit(img, widthPx, heightPx, ox + dc.C, oy + dc.R, pb[i], embedScale, channel, flipY: true, heightPx: heightPx);
                        }
                    }
                });
            }

            static void WriteBit(Unity.Collections.NativeArray<Color32> img, int wPx, int hPx, int blockX, int blockY, bool bit, float embedScale, int channel, bool flipY, int heightPx)
            {   // Write a single bit to the image, flipping Y if needed

                int px0 = blockX * BS;
                int py0 = blockY * BS;

                // Flip Y so overlay is not upside down
                if (flipY)
                    py0 = (heightPx - BS) - py0;

                if (px0 + BS > wPx || py0 + BS > hPx || px0 < 0 || py0 < 0) return;

                if (channel == -1)
                {
                    ApplyToChannel(img, wPx, px0, py0, 0, bit, embedScale);
                    ApplyToChannel(img, wPx, px0, py0, 1, bit, embedScale);
                    ApplyToChannel(img, wPx, px0, py0, 2, bit, embedScale);
                }
                else
                    ApplyToChannel(img, wPx, px0, py0, channel, bit, embedScale);
            }

            [ThreadStatic] static float[] _spatial64;
            [ThreadStatic] static float[] _freq64;
            [ThreadStatic] static byte[] _alpha64;

            static void ApplyToChannel(Unity.Collections.NativeArray<Color32> img, int wPx, int px0, int py0, int ch, bool bit, float embedScale)
            {   // Apply DCT watermark to a single channel in an 8x8 block

                if (_spatial64 == null || _spatial64.Length != 64) _spatial64 = new float[64];
                if (_freq64 == null || _freq64.Length != 64) _freq64 = new float[64];
                if (_alpha64 == null || _alpha64.Length != 64) _alpha64 = new byte[64];

                int k = 0;
                for (int y = 0; y < BS; y++)
                {
                    int row = (py0 + y) * wPx + px0;
                    for (int x = 0; x < BS; x++, k++)
                    {
                        Color32 c = img[row + x];
                        _alpha64[k] = c.a;
                        _spatial64[k] = (ch == 0) ? c.r : (ch == 1 ? c.g : c.b);
                    }
                }

                Dct8x8.Forward(_spatial64, _freq64);

                float dir = bit ? 1f : -1f;
                for (int i = 0; i < EMBED_IDXS.Length; i++)
                {
                    int idx = EMBED_IDXS[i];
                    _freq64[idx] += embedScale * dir * EMBED_SIGNS[i];
                }

                Dct8x8.Inverse(_freq64, _spatial64);

                k = 0;
                for (int y = 0; y < BS; y++)
                {
                    int row = (py0 + y) * wPx + px0;
                    for (int x = 0; x < BS; x++, k++)
                    {
                        int v = (int)Mathf.Clamp(_spatial64[k], 0f, 255f);
                        byte q = (byte)v;

                        Color32 c = img[row + x];
                        if (ch == 0) c.r = q;
                        else if (ch == 1) c.g = q;
                        else c.b = q;

                        c.a = _alpha64[k];
                        img[row + x] = c;
                    }
                }
            }
        }

        struct BlockCoord
        {
            public readonly ushort R;
            public readonly ushort C;

            public BlockCoord(ushort r, ushort c) { R = r; C = c; }
        }

        struct BootWrite
        {
            public readonly BlockCoord Coord;
            public readonly bool Bit;

            public BootWrite(BlockCoord coord, bool bit) { Coord = coord; Bit = bit; }
        }

        static class BitUtil
        {
            public static bool[] UnpackBits(byte[] bytes)
            {
                var bits = new bool[bytes.Length * 8];
                int k = 0;

                for (int i = 0; i < bytes.Length; i++)
                {
                    byte b = bytes[i];
                    for (int bit = 7; bit >= 0; bit--)
                        bits[k++] = ((b >> bit) & 1) != 0;
                }
                return bits;
            }

            public static bool[] RepeatBitsTrim(bool[] bits, int r, int maxLen)
            {
                if (r <= 1)
                {
                    if (bits.Length <= maxLen) return bits;

                    var trimmed = new bool[maxLen];
                    Array.Copy(bits, trimmed, maxLen);
                    return trimmed;
                }

                int want = Math.Min(bits.Length * r, maxLen);
                var outBits = new bool[want];

                int k = 0;
                for (int i = 0; i < bits.Length && k < want; i++)
                {
                    bool v = bits[i];
                    for (int j = 0; j < r && k < want; j++)
                        outBits[k++] = v;
                }
                return outBits;
            }
        }

        static class Dct8x8
        {
            static readonly float[,] C = new float[8, 8];

            static Dct8x8()
            {
                // Orthonormal DCT-II matrix (matches norm="ortho"). :contentReference[oaicite:19]{index=19}
                for (int k = 0; k < 8; k++)
                {
                    float a = (k == 0) ? Mathf.Sqrt(1f / 8f) : Mathf.Sqrt(2f / 8f);
                    for (int n = 0; n < 8; n++)
                        C[k, n] = a * Mathf.Cos(((2f * n + 1f) * k * Mathf.PI) / 16f);
                }
            }

            // freq[u,v] = sum_y sum_x C[u,y] * spatial[y,x] * C[v,x]
            public static void Forward(float[] spatial, float[] freq)
            {
                for (int u = 0; u < 8; u++)
                {
                    for (int v = 0; v < 8; v++)
                    {
                        float sum = 0f;
                        for (int y = 0; y < 8; y++)
                        {
                            float cuy = C[u, y];
                            int row = y * 8;
                            for (int x = 0; x < 8; x++)
                                sum += cuy * spatial[row + x] * C[v, x];
                        }
                        freq[u * 8 + v] = sum;
                    }
                }
            }

            // spatial[y,x] = sum_u sum_v C[u,y] * freq[u,v] * C[v,x]
            public static void Inverse(float[] freq, float[] spatial)
            {
                for (int y = 0; y < 8; y++)
                {
                    int row = y * 8;
                    for (int x = 0; x < 8; x++)
                    {
                        float sum = 0f;
                        for (int u = 0; u < 8; u++)
                        {
                            float cuy = C[u, y];
                            int baseU = u * 8;
                            for (int v = 0; v < 8; v++)
                                sum += cuy * freq[baseU + v] * C[v, x];
                        }
                        spatial[row + x] = sum;
                    }
                }
            }
        }

        static class EncoderPlan
        {
            const ushort MAGIC = 0xCAFE;
            const byte VERSION = 1;

            const ushort BOOT_MAGIC = 0xBEEF;
            const byte BOOT_VERSION = 1;

            const int PKT_HDR_LEN = 16;
            const int PKT_CRC_LEN = 4;

            public static byte[] BuildBoot(int td, int red)
            {
                // Python: struct.pack(">HBBBBB", BOOT_MAGIC, BOOT_VERSION, td, red, PKT_HDR_LEN, 0) + crc32 :contentReference[oaicite:20]{index=20}
                byte[] noCrc = new byte[2 + 1 + 1 + 1 + 1 + 1]; // 7
                int o = 0;

                WriteBE_U16(noCrc, ref o, BOOT_MAGIC);
                noCrc[o++] = BOOT_VERSION;
                noCrc[o++] = (byte)td;
                noCrc[o++] = (byte)red;
                noCrc[o++] = (byte)PKT_HDR_LEN;
                noCrc[o++] = 0;

                uint crc = HashBridge.Crc32(noCrc);

                byte[] outBytes = new byte[noCrc.Length + 4];
                Buffer.BlockCopy(noCrc, 0, outBytes, 0, noCrc.Length);
                o = noCrc.Length;
                WriteBE_U32(outBytes, ref o, crc);
                return outBytes;
            }

            public static byte[] BuildPacket(uint msgId, ushort i, ushort n, byte[] payload, int td, int red)
            {
                // Python header fmt: >HBBBBIHHH then payload then crc32 :contentReference[oaicite:21]{index=21}
                int payloadLen = (payload == null) ? 0 : payload.Length;
                byte[] noCrc = new byte[PKT_HDR_LEN + payloadLen];

                int o = 0;
                WriteBE_U16(noCrc, ref o, MAGIC);
                noCrc[o++] = VERSION;
                noCrc[o++] = 0;            // flags/reserved (Python uses 0)
                noCrc[o++] = (byte)td;
                noCrc[o++] = (byte)red;
                WriteBE_U32(noCrc, ref o, msgId);
                WriteBE_U16(noCrc, ref o, i);
                WriteBE_U16(noCrc, ref o, n);
                WriteBE_U16(noCrc, ref o, (ushort)payloadLen);

                if (payloadLen > 0)
                    Buffer.BlockCopy(payload, 0, noCrc, o, payloadLen);

                uint crc = HashBridge.Crc32(noCrc);

                byte[] outBytes = new byte[noCrc.Length + PKT_CRC_LEN];
                Buffer.BlockCopy(noCrc, 0, outBytes, 0, noCrc.Length);
                o = noCrc.Length;
                WriteBE_U32(outBytes, ref o, crc);

                return outBytes;
            }

            static void WriteBE_U16(byte[] buf, ref int o, ushort v)
            {
                buf[o++] = (byte)((v >> 8) & 0xFF);
                buf[o++] = (byte)(v & 0xFF);
            }

            static void WriteBE_U32(byte[] buf, ref int o, uint v)
            {
                buf[o++] = (byte)((v >> 24) & 0xFF);
                buf[o++] = (byte)((v >> 16) & 0xFF);
                buf[o++] = (byte)((v >> 8) & 0xFF);
                buf[o++] = (byte)(v & 0xFF);
            }
        }

        static class HashBridge
        {
            // Try to bind to MathUtil if present; otherwise fall back to local implementations.
            static readonly Func<byte[], uint> _crc32 = BindMathUtil("Crc32", "CRC32", "GetCrc32");
            static readonly Func<byte[], uint> _adler32 = BindMathUtil("Adler32", "ADLER32", "GetAdler32");

            public static uint Crc32(byte[] data)
            {
                if (data == null) return 0;
                return _crc32 != null ? _crc32(data) : FallbackCrc32(data);
            }

            public static uint Adler32(byte[] data)
            {
                if (data == null) return 1;
                return _adler32 != null ? _adler32(data) : FallbackAdler32(data);
            }

            static Func<byte[], uint> BindMathUtil(params string[] names)
            {
                Type t = typeof(MathUtil);
                foreach (var n in names)
                {
                    var mi = t.GetMethod(
                        n,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Static,
                        binder: null,
                        types: new[] { typeof(byte[]) },
                        modifiers: null
                    );

                    if (mi == null) continue;

                    if (mi.ReturnType == typeof(uint))
                        return (Func<byte[], uint>)Delegate.CreateDelegate(typeof(Func<byte[], uint>), mi);

                    if (mi.ReturnType == typeof(int))
                    {
                        var d = (Func<byte[], int>)Delegate.CreateDelegate(typeof(Func<byte[], int>), mi);
                        return b => unchecked((uint)d(b));
                    }
                }
                return null;
            }

            static readonly uint[] _crcTable = BuildCrcTable();

            static uint[] BuildCrcTable()
            {
                const uint poly = 0xEDB88320u;
                var t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? (poly ^ (c >> 1)) : (c >> 1);
                    t[i] = c;
                }
                return t;
            }

            static uint FallbackCrc32(byte[] data)
            {
                uint c = 0xFFFFFFFFu;
                for (int i = 0; i < data.Length; i++)
                    c = _crcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
                return c ^ 0xFFFFFFFFu;
            }

            static uint FallbackAdler32(byte[] data)
            {
                const uint MOD = 65521;
                uint a = 1, b = 0;
                for (int i = 0; i < data.Length; i++)
                {
                    a = (a + data[i]) % MOD;
                    b = (b + a) % MOD;
                }
                return (b << 16) | a;
            }
        }
    }
}

/*
Commented film shader example from your paste was left out intentionally in the encoder file to keep the encoder focused.
The encoder now follows the same "copy source to result then mutate result" pattern used by that film example. :contentReference[oaicite:22]{index=22}
*/
