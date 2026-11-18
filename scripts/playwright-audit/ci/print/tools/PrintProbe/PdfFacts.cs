// W-G1-51 갈래 Z — PDF 판독기: 「사실」 만 뽑는다(판정은 judge-print.js 한 곳 · 설계 §1-3)
//   뽑는 것: 쪽 크기(mm) · 낱말(글자 · 상자 mm · 종이 왼쪽 위 원점) · 글꼴 이름 · 그림(원시 바이트 해시 · 상자 · 풀린 4분면 밝기)
//            · 그림을 그린 자리마다 실효 불투명도(ExtGState ca × 감싼 묶음 ca) · 소프트 마스크 유무 · 평균
//            · 능동 내용 이름(풀린 이름) · 원시 바이트 글자 셈(trailer · XRef · /Encrypt)
//   🔴 판정 0 — 「있다 / 없다」 를 여기서 정하지 않는다(사실 · 판정 분리 = 판독기 양성 대조를 따로 세우려고 · 설계 §1-3 F4 ⑴).
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace PrintProbe;

public static class PdfFacts
{
    private const double MmPerPt = 25.4 / 72.0;

    public static string Sha(ReadOnlySpan<byte> b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static double R2(double v) => Math.Round(v, 2);

    public static JsonObject Read(byte[] bytes)
    {
        var o = new JsonObject
        {
            ["schema"] = "print-facts/1",
            ["bytes"] = bytes.Length,
            ["sha256"] = Sha(bytes),
            ["head"] = bytes.Length >= 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-",
        };
        var raw = new JsonObject
        {
            ["trailer"] = NameScan.RawCount(bytes, "trailer"),
            ["XRef"] = NameScan.RawCount(bytes, "XRef"),
            ["EncryptLiteral"] = NameScan.RawCount(bytes, "/Encrypt"),
        };
        o["raw"] = raw;
        var active = new JsonObject();
        foreach (var (k, v) in NameScan.ActiveContent(bytes)) active[k] = v;
        o["active"] = active;

        PdfDocument doc;
        try
        {
            doc = PdfDocument.Open(bytes);
        }
        catch (Exception ex)
        {
            // 열기 실패는 사실이다 — 판정기가 사유로 쓴다(빈 catch 0 · #15).
            o["ok"] = false;
            o["error"] = ex.GetType().Name;
            Console.Error.WriteLine($"판독기: 열기 실패 {ex.GetType().Name}");
            return o;
        }

        using (doc)
        {
            o["ok"] = true;
            o["encrypted"] = doc.IsEncrypted;
            o["pageCount"] = doc.NumberOfPages;
            var pages = new JsonArray();
            foreach (var page in doc.GetPages())
            {
                pages.Add(ReadPage(doc, page));
            }
            o["pages"] = pages;
        }
        return o;
    }

    private static JsonObject ReadPage(PdfDocument doc, Page page)
    {
        var hPt = page.Height;
        var p = new JsonObject
        {
            ["n"] = page.Number,
            ["wMm"] = R2(page.Width * MmPerPt),
            ["hMm"] = R2(hPt * MmPerPt),
        };

        var words = new JsonArray();
        foreach (var w in page.GetWords())
        {
            var b = w.BoundingBox;
            words.Add(new JsonObject
            {
                ["t"] = w.Text,
                ["x"] = R2(b.Left * MmPerPt),
                ["y"] = R2((hPt - b.Top) * MmPerPt),
                ["w"] = R2(b.Width * MmPerPt),
                ["h"] = R2(b.Height * MmPerPt),
                // 🔴 2차(개발명세서 §3 ② · 시안 §3-3 M2) — 첫 글자 크기(pt) · 글꼴 이름 = 스타일 표지(제목 크기 · 격식 글꼴) 판독 재료
                ["fs"] = w.Letters.Count > 0 ? R2(w.Letters[0].PointSize) : null,
                ["fn"] = w.Letters.Count > 0 ? w.Letters[0].FontName : null,
            });
        }
        p["words"] = words;

        var fonts = new JsonArray();
        foreach (var f in page.Letters.Select(l => l.FontName ?? "").Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            fonts.Add(f);
        p["fonts"] = fonts;

        var images = new JsonArray();
        foreach (var img in page.GetImages())
        {
            var rawImg = img.RawMemory.ToArray();
            var b = img.Bounds;
            var io = new JsonObject
            {
                ["sha"] = Sha(rawImg),
                ["x"] = R2(b.Left * MmPerPt),
                ["y"] = R2((hPt - b.Top) * MmPerPt),
                ["w"] = R2(b.Width * MmPerPt),
                ["h"] = R2(b.Height * MmPerPt),
                ["ws"] = img.WidthInSamples,
                ["hs"] = img.HeightInSamples,
                ["inline"] = img.IsInlineImage,
            };
            if (img.TryGetPng(out var png) && png is not null)
            {
                var luma = PngLite.TryDecode(png, out var why);
                if (luma is null) io["decode"] = why ?? "실패";
                else
                {
                    io["decode"] = "ok";
                    var q = new JsonArray();
                    foreach (var m in luma.QuadrantMeans()) q.Add(Math.Round(m, 4));
                    io["quad"] = q;
                }
            }
            else io["decode"] = "PNG 변환 불가";
            images.Add(io);
        }
        p["images"] = images;

        var draws = new JsonArray();
        try
        {
            var walker = new ContentWalker(doc, draws);
            walker.WalkPage(page.Dictionary);
            p["drawsOk"] = true;
        }
        catch (Exception ex)
        {
            // 해석 못 한 내용 = 사실(판정기가 「미측정(판독기)」 로 센다). 빈 catch 0.
            p["drawsOk"] = false;
            p["drawsError"] = ex.GetType().Name;
            Console.Error.WriteLine($"판독기: 내용 흐름 해석 실패 쪽 {page.Number} {ex.GetType().Name}");
        }
        p["draws"] = draws;
        return p;
    }

    /// <summary>내용 흐름을 따라가며 그림을 그린 자리의 실효 불투명도를 적는다.</summary>
    private sealed class ContentWalker
    {
        private readonly PdfDocument _doc;
        private readonly JsonArray _draws;

        private sealed class Gs
        {
            public double Ca = 1.0;
            public double Group = 1.0;
            public bool ExtSMask;
            public Gs Clone() => new() { Ca = Ca, Group = Group, ExtSMask = ExtSMask };
        }

        public ContentWalker(PdfDocument doc, JsonArray draws)
        {
            _doc = doc;
            _draws = draws;
        }

        private IToken? Res(IToken? t)
        {
            var guard = 0;
            while (t is IndirectReferenceToken r && guard++ < 32) t = _doc.Structure.GetObject(r.Data)?.Data;
            return t;
        }

        private DictionaryToken? Dict(IToken? t) => Res(t) as DictionaryToken;

        private IToken? Get(DictionaryToken? d, string key) =>
            d is not null && d.Data.TryGetValue(key, out var v) ? Res(v) : null;

        private static byte[] Decode(StreamToken st)
        {
            var provider = DefaultFilterProvider.Instance;
            var filters = provider.GetFilters(st.StreamDictionary);
            var data = st.Data.ToArray();
            for (var i = 0; i < filters.Count; i++)
            {
                data = filters[i].Decode(data, st.StreamDictionary, provider, i).ToArray();
            }
            return data;
        }

        public void WalkPage(DictionaryToken pageDict)
        {
            DictionaryToken? res = null;
            var node = pageDict;
            var guard = 0;
            while (node is not null && guard++ < 32)
            {
                res = Dict(Get(node, "Resources"));
                if (res is not null) break;
                node = Dict(Get(node, "Parent"));
            }

            using var all = new MemoryStream();
            var contents = Get(pageDict, "Contents");
            if (contents is StreamToken one) all.Write(Decode(one));
            else if (contents is ArrayToken arr)
            {
                foreach (var t in arr.Data)
                {
                    if (Res(t) is StreamToken s)
                    {
                        all.Write(Decode(s));
                        all.WriteByte((byte)'\n');
                    }
                }
            }
            Walk(res, all.ToArray(), new Gs(), 0, "page");
        }

        private void Walk(DictionaryToken? res, byte[] content, Gs start, int depth, string where)
        {
            if (depth > 12) return;
            var stack = new Stack<Gs>();
            var gs = start.Clone();
            var operands = new List<object>();
            var lx = new Lexer(content);
            while (lx.Next(out var kind, out var text))
            {
                if (kind == Lexer.Kind.Operand)
                {
                    operands.Add(text);
                    continue;
                }
                switch (text)
                {
                    case "q": stack.Push(gs.Clone()); break;
                    case "Q": if (stack.Count > 0) gs = stack.Pop(); break;
                    case "gs":
                        if (operands.Count > 0 && operands[^1] is string gname && gname.StartsWith('/'))
                        {
                            var ext = Dict(Get(Dict(Get(res, "ExtGState")), gname[1..]));
                            if (Get(ext, "ca") is NumericToken ca) gs.Ca = ca.Data;
                            var sm = Get(ext, "SMask");
                            if (sm is not null) gs.ExtSMask = !(sm is NameToken nm && nm.Data == "None");
                        }
                        break;
                    case "Do":
                        if (operands.Count > 0 && operands[^1] is string xname && xname.StartsWith('/'))
                            DoXObject(res, xname[1..], gs, depth, where);
                        break;
                    case "EI":
                        _draws.Add(new JsonObject
                        {
                            ["sha"] = null,
                            ["inline"] = true,
                            ["alpha"] = Math.Round(gs.Ca * gs.Group, 4),
                            ["extSMask"] = gs.ExtSMask,
                            ["where"] = where,
                        });
                        break;
                }
                operands.Clear();
            }
        }

        private void DoXObject(DictionaryToken? res, string name, Gs gs, int depth, string where)
        {
            var xo = Get(Dict(Get(res, "XObject")), name) as StreamToken;
            if (xo is null) return;
            var d = xo.StreamDictionary;
            var subtype = (Get(d, "Subtype") as NameToken)?.Data;
            if (subtype == "Image")
            {
                var draw = new JsonObject
                {
                    ["sha"] = Sha(xo.Data.Span),
                    ["inline"] = false,
                    ["alpha"] = Math.Round(gs.Ca * gs.Group, 4),
                    ["extSMask"] = gs.ExtSMask,
                    ["where"] = where,
                    ["ws"] = (Get(d, "Width") as NumericToken)?.Data,
                    ["hs"] = (Get(d, "Height") as NumericToken)?.Data,
                };
                if (Get(d, "SMask") is StreamToken sm)
                {
                    draw["imgSMask"] = true;
                    try
                    {
                        var bpc = (Get(sm.StreamDictionary, "BitsPerComponent") as NumericToken)?.Data ?? 8;
                        var samples = Decode(sm);
                        if (bpc == 8 && samples.Length > 0)
                        {
                            double s = 0;
                            foreach (var b in samples) s += b;
                            draw["smaskMean"] = Math.Round(s / samples.Length / 255.0, 4);
                        }
                        else draw["smaskMean"] = null;
                    }
                    catch (Exception ex)
                    {
                        draw["smaskMean"] = null;
                        draw["smaskError"] = ex.GetType().Name;
                        Console.Error.WriteLine($"판독기: 소프트 마스크 풀기 실패 {ex.GetType().Name}");
                    }
                }
                else draw["imgSMask"] = false;
                _draws.Add(draw);
            }
            else if (subtype == "Form")
            {
                var formRes = Dict(Get(d, "Resources")) ?? res;
                var inner = new Gs { Ca = 1.0, Group = gs.Ca * gs.Group, ExtSMask = gs.ExtSMask };
                Walk(formRes, Decode(xo), inner, depth + 1, $"{where}>{name}");
            }
        }
    }

    /// <summary>내용 흐름 낱말 끊기 — 연산자와 피연산자(이름 · 숫자 · 그 밖은 자리표)만 가른다.</summary>
    private sealed class Lexer
    {
        public enum Kind { Operand, Operator }
        private readonly byte[] _b;
        private int _i;
        public Lexer(byte[] b) { _b = b; }

        private static bool White(byte c) => c is 0 or 9 or 10 or 12 or 13 or 32;
        private static bool Delim(byte c) => c is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

        public bool Next(out Kind kind, out string text)
        {
            kind = Kind.Operand;
            text = "";
            while (_i < _b.Length)
            {
                var c = _b[_i];
                if (White(c)) { _i++; continue; }
                if (c == (byte)'%') { while (_i < _b.Length && _b[_i] != 10 && _b[_i] != 13) _i++; continue; }
                if (c == (byte)'/')
                {
                    var s = ++_i;
                    while (_i < _b.Length && !White(_b[_i]) && !Delim(_b[_i])) _i++;
                    text = "/" + DecodeName(_b.AsSpan(s, _i - s));
                    return true;
                }
                if (c == (byte)'(')
                {
                    var depth = 0;
                    while (_i < _b.Length)
                    {
                        var ch = _b[_i++];
                        if (ch == (byte)'\\') { _i++; continue; }
                        if (ch == (byte)'(') depth++;
                        else if (ch == (byte)')' && --depth == 0) break;
                    }
                    text = "(str)";
                    return true;
                }
                if (c == (byte)'<')
                {
                    if (_i + 1 < _b.Length && _b[_i + 1] == (byte)'<') { _i += 2; text = "<<"; return true; }
                    while (_i < _b.Length && _b[_i] != (byte)'>') _i++;
                    _i++;
                    text = "<hex>";
                    return true;
                }
                if (c == (byte)'>') { _i += (_i + 1 < _b.Length && _b[_i + 1] == (byte)'>') ? 2 : 1; text = ">>"; return true; }
                if (c is (byte)'[' or (byte)']' or (byte)'{' or (byte)'}') { _i++; text = ((char)c).ToString(); return true; }
                if (c is (byte)'+' or (byte)'-' or (byte)'.' || (c >= (byte)'0' && c <= (byte)'9'))
                {
                    var s = _i;
                    while (_i < _b.Length && !White(_b[_i]) && !Delim(_b[_i])) _i++;
                    text = Encoding.ASCII.GetString(_b, s, _i - s);
                    return true;
                }
                {
                    var s = _i;
                    while (_i < _b.Length && !White(_b[_i]) && !Delim(_b[_i])) _i++;
                    if (_i == s) { _i++; continue; }
                    text = Encoding.ASCII.GetString(_b, s, _i - s);
                    if (text is "true" or "false" or "null") return true;
                    kind = Kind.Operator;
                    if (text == "ID") SkipInlineData();
                    return true;
                }
            }
            return false;
        }

        private void SkipInlineData()
        {
            // ID 뒤 공백 1 · 그 뒤 「공백 EI 공백/끝」 까지 바이트를 건너뛴다.
            _i++;
            while (_i + 2 < _b.Length)
            {
                if (White(_b[_i]) && _b[_i + 1] == (byte)'E' && _b[_i + 2] == (byte)'I' && (_i + 3 >= _b.Length || White(_b[_i + 3])))
                {
                    _i += 1;
                    return;
                }
                _i++;
            }
            _i = _b.Length;
        }

        private static string DecodeName(ReadOnlySpan<byte> t)
        {
            var sb = new StringBuilder();
            for (var k = 0; k < t.Length; k++)
            {
                if (t[k] == (byte)'#' && k + 2 < t.Length && Uri.IsHexDigit((char)t[k + 1]) && Uri.IsHexDigit((char)t[k + 2]))
                {
                    sb.Append((char)Convert.ToByte(Encoding.ASCII.GetString(t.Slice(k + 1, 2)), 16));
                    k += 2;
                }
                else sb.Append((char)t[k]);
            }
            return sb.ToString();
        }
    }
}
