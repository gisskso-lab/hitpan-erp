// W-G1-51 갈래 Z — 작은 PNG 읽기·쓰기 (시험 쪽 · 새 의존 0 · System.IO.Compression 만)
//   읽기: 판독기가 PDF 안 그림(PdfPig TryGetPng)을 풀어 4분면 밝기 순서를 본다(설계 §1-3 ① ⓑ 2순위 · §1-2 G11).
//   쓰기: ⑨ 표본 P1 · U2 의 그림(정상 스캔 꼴 · 20MB 가까운 그림 반복)을 만든다(설계 §7-2 ⑨ 표).
using System.Buffers.Binary;
using System.IO.Compression;

namespace PrintProbe;

/// <summary>풀린 그림 — 밝기(0~1) 격자. 색 그림은 BT.709 휘도로 접는다.</summary>
public sealed class LumaImage
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double[] Luma { get; init; } = Array.Empty<double>();

    /// <summary>4분면(왼위 · 오위 · 왼아래 · 오아래) 평균 밝기.</summary>
    public double[] QuadrantMeans()
    {
        var sums = new double[4];
        var counts = new long[4];
        var hw = Width / 2;
        var hh = Height / 2;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                // 가운데 줄 · 칸은 빼서 경계 섞임을 줄인다(홀수 폭).
                if ((Width % 2 == 1 && x == hw) || (Height % 2 == 1 && y == hh)) continue;
                var q = (y < hh ? 0 : 2) + (x < hw ? 0 : 1);
                sums[q] += Luma[(y * Width) + x];
                counts[q]++;
            }
        }
        var means = new double[4];
        for (var i = 0; i < 4; i++) means[i] = counts[i] == 0 ? double.NaN : sums[i] / counts[i];
        return means;
    }
}

public static class PngLite
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static LumaImage? TryDecode(byte[] png, out string? why)
    {
        why = null;
        try
        {
            if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature)) { why = "PNG 머리 아님"; return null; }
            var pos = 8;
            int w = 0, h = 0, depth = 0, colorType = 0, interlace = 0;
            byte[]? palette = null;
            using var idat = new MemoryStream();
            while (pos + 8 <= png.Length)
            {
                var len = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos, 4));
                var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                var data = png.AsSpan(pos + 8, len);
                if (type == "IHDR")
                {
                    w = (int)BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                    h = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
                    depth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                }
                else if (type == "PLTE") palette = data.ToArray();
                else if (type == "IDAT") idat.Write(data);
                else if (type == "IEND") break;
                pos += 12 + len;
            }
            if (interlace != 0) { why = "겹침(interlace) PNG 는 안 푼다"; return null; }
            if (depth != 8) { why = $"비트 깊이 {depth} 는 안 푼다(8 만)"; return null; }
            var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            if (channels == 0) { why = $"색 형식 {colorType} 모름"; return null; }
            if (colorType == 3 && palette is null) { why = "팔레트 없음"; return null; }

            idat.Position = 0;
            using var z = new ZLibStream(idat, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            z.CopyTo(raw);
            var bytes = raw.ToArray();
            var stride = w * channels;
            if (bytes.Length < (stride + 1) * h) { why = "풀린 길이 모자람"; return null; }

            var prev = new byte[stride];
            var cur = new byte[stride];
            var luma = new double[w * h];
            for (var y = 0; y < h; y++)
            {
                var off = y * (stride + 1);
                var filter = bytes[off];
                Array.Copy(bytes, off + 1, cur, 0, stride);
                Unfilter(filter, cur, prev, channels);
                for (var x = 0; x < w; x++)
                {
                    double r, g, b;
                    var p = x * channels;
                    switch (colorType)
                    {
                        case 0: r = g = b = cur[p]; break;
                        case 4: r = g = b = cur[p]; break;
                        case 3:
                            var idx = cur[p] * 3;
                            r = palette![idx]; g = palette[idx + 1]; b = palette[idx + 2];
                            break;
                        default: r = cur[p]; g = cur[p + 1]; b = cur[p + 2]; break;
                    }
                    luma[(y * w) + x] = ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 255.0;
                }
                (prev, cur) = (cur, prev);
            }
            return new LumaImage { Width = w, Height = h, Luma = luma };
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            why = $"PNG 풀기 실패 {ex.GetType().Name}";
            return null;
        }
    }

    private static void Unfilter(byte filter, byte[] cur, byte[] prev, int bpp)
    {
        for (var i = 0; i < cur.Length; i++)
        {
            var a = i >= bpp ? cur[i - bpp] : 0;
            var b = prev[i];
            var c = i >= bpp ? prev[i - bpp] : 0;
            cur[i] = filter switch
            {
                0 => cur[i],
                1 => (byte)(cur[i] + a),
                2 => (byte)(cur[i] + b),
                3 => (byte)(cur[i] + ((a + b) / 2)),
                4 => (byte)(cur[i] + Paeth(a, b, c)),
                _ => throw new InvalidDataException("PNG 줄 거르개 모름"),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    /// <summary>8비트 RGB PNG 쓰기. rgb 길이 = w*h*3.</summary>
    public static byte[] EncodeRgb(int w, int h, byte[] rgb, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream();
        ms.Write(Signature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)h);
        ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(ms, "IHDR", ihdr);
        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, level, leaveOpen: true))
            {
                for (var y = 0; y < h; y++)
                {
                    z.WriteByte(0);
                    z.Write(rgb, y * w * 3, w * 3);
                }
            }
            WriteChunk(ms, "IDAT", raw.ToArray());
        }
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = Crc32(t, data);
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
