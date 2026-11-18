// W-G1-51 갈래 Z — PrintProbe 진입점 (시험 쪽 도구 · 제품 코드 0)
//   read <pdf> <out.json>   PDF 사실 뽑기(판정 0)
//   samples <dir>           ⑨ 표본 만들기 + manifest.json(⒜ · ⒝ · ⒞ · 유효 조건)
//   selftest <workdir>      판독기 자체시험(이름 풀기 · PNG · 글자 좌표 · 표본 유효 조건) — DB · 앱 · 네트워크 0
using System.Text.Json;
using System.Text.Json.Nodes;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace PrintProbe;

public static class Program
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length >= 3 && args[0] == "read")
        {
            var facts = PdfFacts.Read(File.ReadAllBytes(args[1]));
            File.WriteAllText(args[2], facts.ToJsonString(Pretty));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "samples")
        {
            var m = NineSamples.Make(args[1]);
            File.WriteAllText(Path.Combine(args[1], "manifest.json"), m.ToJsonString(Pretty));
            return 0;
        }
        if (args.Length >= 2 && args[0] == "selftest") return SelfTest(args[1]);
        Console.Error.WriteLine("쓰는 법: read <pdf> <out.json> | samples <dir> | selftest <workdir>");
        return 2;
    }

    private static int pass;
    private static int fail;

    private static void Check(string id, bool ok, string got)
    {
        if (ok) pass++; else fail++;
        Console.WriteLine($"{(ok ? "🟢 PASS" : "🔴 FAIL")} {id} — {got}");
    }

    private static int SelfTest(string work)
    {
        Directory.CreateDirectory(work);

        // T1 이름 풀기 — 설계 §7-2 ㉮-h4 (가)~(라) 글자 꼴(시험 쪽 구현)
        var cases = new (string src, bool want)[]
        {
            ("<< /Encr#79pt 5 0 R >>", true), ("<< /E#6ecrypt 5 0 R >>", true), ("<< /#45#6E#63#72#79#70#74 5 0 R >>", true),
            ("<< /Type/XRef/Encrypt 5 0 R >>", true), ("(a%b) /Encrypt 5 0 R", true), ("<< /encrypt 5 0 R >>", false),
            ("<< /EncryptMetadata false >>", false), ("<< /Encrypt#zz 1 >>", false),
        };
        foreach (var (src, want) in cases)
        {
            var got = NameScan.DecodedNames(System.Text.Encoding.ASCII.GetBytes(src)).ContainsKey("Encrypt");
            Check($"T1 이름 풀기 「{src}」", got == want, $"Encrypt 있음={got} · 기대={want}");
        }

        // T2 PNG 쓰기 → 읽기 4분면 밝기(왼위 0.90 · 오위 0.15 · 왼아래 0.65 · 오아래 0.40)
        var lum = new[] { 0.90, 0.15, 0.65, 0.40 };
        var rgb = new byte[40 * 40 * 3];
        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 40; x++)
            {
                var v = (byte)Math.Round(lum[(y < 20 ? 0 : 2) + (x < 20 ? 0 : 1)] * 255);
                var i = ((y * 40) + x) * 3;
                rgb[i] = v; rgb[i + 1] = v; rgb[i + 2] = v;
            }
        var dec = PngLite.TryDecode(PngLite.EncodeRgb(40, 40, rgb), out var why);
        var q = dec?.QuadrantMeans() ?? Array.Empty<double>();
        Check("T2 PNG 4분면 밝기", q.Length == 4 && q.Zip(lum).All(t => Math.Abs(t.First - t.Second) < 0.01), q.Length == 4 ? string.Join(",", q.Select(v => v.ToString("0.000"))) : why ?? "실패");

        // T3 글자 좌표(양성 대조 꼴) — 알려진 자리에 찍은 낱말을 mm 로 되읽음
        var b = new PdfDocumentBuilder();
        var font = b.AddStandard14Font(Standard14Font.Helvetica);
        var page = b.AddPage(595.28, 841.89);
        page.AddText("PARTNERX", 12, new PdfPoint(100, 700), font);
        var facts = PdfFacts.Read(b.Build());
        var w = facts["pages"]?[0]?["words"]?.AsArray().FirstOrDefault(n => n?["t"]?.GetValue<string>() == "PARTNERX");
        var xmm = w?["x"]?.GetValue<double>() ?? -1;
        Check("T3 글자 좌표 x(mm)", Math.Abs(xmm - (100 * 25.4 / 72)) < 0.5, $"x={xmm} · 기대≈{100 * 25.4 / 72:0.00}");

        // T4 ⑨ 표본 — 만들고 유효 조건 표를 낸다(⒞ = qpdf 없으면 「미측정(설치 실패)」)
        var dir = Path.Combine(work, "nine");
        var m = NineSamples.Make(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), m.ToJsonString(Pretty));
        foreach (var row in m["rows"]!.AsArray())
        {
            var r = row!.AsObject();
            var v = r["validity"]?.GetValue<string>() ?? "?";
            Check($"T4 표본 {r["id"]}", v != "무효", $"유효={v} · ⒜={r["a"]} · ⒝={r["b"]} · ⒞={r["c"]} · {r["validityWhy"]}");
        }

        Console.WriteLine($"자체시험 끝 — 통과 {pass} · 실패 {fail}");
        return fail == 0 ? 0 : 1;
    }
}
