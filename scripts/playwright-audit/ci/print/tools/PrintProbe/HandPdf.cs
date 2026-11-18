// W-G1-51 갈래 Z — 바이트를 손으로 짜는 작은 PDF (시험 쪽 · 새 의존 0)
//   쓰임 ① 자체시험 T5 — 실효 불투명도 양성 대조(ExtGState `/ca 0.12` 묶음 안 그림 + 그림 `/SMask`) · 개발명세서 §3 ①
//        ② ⑨ 표본 P4 — 암호 아님 · **비압축 본문**에 `/Encrypt` 글자(설계 :257 · PM §6-a 5 · 내용 흐름 `/Filter` 0 · xref 오프셋 다시 셈)
//   🔴 흐름은 모두 거르개 없이(`/Filter` 0) 쓴다 — 본문 글자가 파일 바이트에 그대로 남는다.
using System.Globalization;
using System.Text;

namespace PrintProbe;

public sealed class HandPdf
{
    private readonly List<byte[]> _objects = new();

    /// <summary>객체 하나를 더하고 번호를 돌려준다(1부터). 본문 = `<< … >>` 또는 사전 + 흐름.</summary>
    public int Add(string dict) => AddRaw(Encoding.ASCII.GetBytes(dict));

    public int AddStream(string dict, byte[] data)
    {
        var head = Encoding.ASCII.GetBytes($"<< {dict} /Length {data.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n");
        var tail = Encoding.ASCII.GetBytes("\nendstream");
        var b = new byte[head.Length + data.Length + tail.Length];
        Buffer.BlockCopy(head, 0, b, 0, head.Length);
        Buffer.BlockCopy(data, 0, b, head.Length, data.Length);
        Buffer.BlockCopy(tail, 0, b, head.Length + data.Length, tail.Length);
        return AddRaw(b);
    }

    private int AddRaw(byte[] body)
    {
        _objects.Add(body);
        return _objects.Count;
    }

    /// <summary>번호 미리 잡기 — 서로 가리키는 객체(쪽 ↔ 쪽 묶음)용. Set 으로 나중에 채운다.</summary>
    public int Reserve() => AddRaw(Array.Empty<byte>());

    public void Set(int n, string dict) => _objects[n - 1] = Encoding.ASCII.GetBytes(dict);

    /// <summary>xref 표 · trailer 를 붙여 파일 바이트를 만든다(오프셋 = 실제 바이트 자리로 다시 셈).</summary>
    public byte[] Build(int root)
    {
        using var ms = new MemoryStream();
        void W(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        W("%PDF-1.4\n");
        ms.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });
        var offsets = new long[_objects.Count];
        for (var i = 0; i < _objects.Count; i++)
        {
            offsets[i] = ms.Position;
            W($"{i + 1} 0 obj\n");
            ms.Write(_objects[i]);
            W("\nendobj\n");
        }
        var xref = ms.Position;
        W($"xref\n0 {_objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        W($"trailer\n<< /Size {_objects.Count + 1} /Root {root} 0 R >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return ms.ToArray();
    }

    /// <summary>
    /// T5 양성 대조 PDF — A4 1쪽 · 쪽 내용 `q /GS1 gs /Fm1 Do Q` · GS1 = `/ca 0.12` · Fm1 = 투명 묶음 양식(안에서 그림 Im1)
    ///   · Im1 = 40×40 RGB 4분면 밝기(왼위 · 오위 · 왼아래 · 오아래) · Im1 `/SMask` = 40×40 회색 값 <paramref name="maskByte"/>.
    ///   그림이 들어간 자리 = 쪽 왼쪽 아래 기준 (100, 400) 에서 200×200 pt.
    /// </summary>
    public static byte[] OpacityProbe(double[] quadLuma, byte maskByte, double ca)
    {
        const int side = 40;
        var rgb = new byte[side * side * 3];
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var v = (byte)Math.Round(quadLuma[(y < side / 2 ? 0 : 2) + (x < side / 2 ? 0 : 1)] * 255);
                var i = ((y * side) + x) * 3;
                rgb[i] = v; rgb[i + 1] = v; rgb[i + 2] = v;
            }
        }
        var mask = Enumerable.Repeat(maskByte, side * side).ToArray();

        var h = new HandPdf();
        var catalog = h.Reserve();
        var pages = h.Reserve();
        var smask = h.AddStream($"/Type /XObject /Subtype /Image /Width {side} /Height {side} /ColorSpace /DeviceGray /BitsPerComponent 8", mask);
        var img = h.AddStream($"/Type /XObject /Subtype /Image /Width {side} /Height {side} /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {smask} 0 R", rgb);
        var formBody = Encoding.ASCII.GetBytes("q 200 0 0 200 100 400 cm /Im1 Do Q");
        var form = h.AddStream($"/Type /XObject /Subtype /Form /BBox [0 0 595.28 841.89] /Group << /S /Transparency >> /Resources << /XObject << /Im1 {img} 0 R >> >>", formBody);
        var gs = h.Add($"<< /Type /ExtGState /ca {ca.ToString("0.###", CultureInfo.InvariantCulture)} /CA {ca.ToString("0.###", CultureInfo.InvariantCulture)} >>");
        var content = h.AddStream("", Encoding.ASCII.GetBytes("q /GS1 gs /Fm1 Do Q"));
        var page = h.Add($"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 595.28 841.89] /Resources << /ExtGState << /GS1 {gs} 0 R >> /XObject << /Fm1 {form} 0 R >> >> /Contents {content} 0 R >>");
        h.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        h.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        return h.Build(catalog);
    }

    /// <summary>
    /// ⑨ P4 — 암호 아님 · 비압축 본문(내용 흐름 · `/Filter` 0)에 글자 <paramref name="text"/> 를 `( … ) Tj` 로 찍은 A4 1쪽.
    ///   글꼴 = 표준 14 Helvetica(파일에 글꼴 흐름 0). 원시 바이트에 글자가 그대로 남는다.
    /// </summary>
    public static byte[] PlainTextPage(string text)
    {
        var h = new HandPdf();
        var catalog = h.Reserve();
        var pages = h.Reserve();
        var font = h.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        var content = h.AddStream("", Encoding.ASCII.GetBytes($"BT /F1 12 Tf 72 720 Td ({text}) Tj ET"));
        var page = h.Add($"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 595.28 841.89] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        h.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        h.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        return h.Build(catalog);
    }
}
