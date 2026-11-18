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

        // T5 실효 불투명도 양성 대조(개발명세서 §3 ① · 설계 §1-3 ① ⓒ) — 손으로 짠 PDF:
        //   쪽 → ExtGState `/ca 0.12` → 투명 묶음 양식 Fm1 → 그림 Im1(4분면 밝기 · `/SMask` 회색 값 128)
        //   기대: 그림 그린 자리 1 · alpha = 0.12 · imgSMask = true · smaskMean ≈ 0.502 · 자리 = page>Fm1
        //         · 그림 목록 해시 = 그린 자리 해시 · 4분면 밝기 순서 = 차림 순서(왼위 0.90 > 왼아래 0.65 > 오아래 0.40 > 오위 0.15)
        var t5Pdf = HandPdf.OpacityProbe(lum, 128, 0.12);
        File.WriteAllBytes(Path.Combine(work, "t5-opacity.pdf"), t5Pdf);
        var t5 = PdfFacts.Read(t5Pdf);
        var t5Page = t5["pages"]?[0];
        var t5Draws = t5Page?["draws"]?.AsArray() ?? new JsonArray();
        var t5Img = t5Draws.FirstOrDefault(d => d?["inline"]?.GetValue<bool>() == false);
        var t5Alpha = t5Img?["alpha"]?.GetValue<double>() ?? -1;
        var t5Mask = t5Img?["imgSMask"]?.GetValue<bool>() ?? false;
        var t5Mean = t5Img?["smaskMean"]?.GetValue<double>() ?? -1;
        var t5Where = t5Img?["where"]?.GetValue<string>() ?? "";
        Check("T5a 실효 불투명도(묶음 안 그림)", t5Page?["drawsOk"]?.GetValue<bool>() == true && t5Draws.Count == 1 && Math.Abs(t5Alpha - 0.12) < 0.0001 && t5Where == "page>Fm1",
            $"그린 자리 {t5Draws.Count} · alpha={t5Alpha} · 자리={t5Where}");
        Check("T5b 그림 소프트 마스크", t5Mask && Math.Abs(t5Mean - (128 / 255.0)) < 0.001, $"imgSMask={t5Mask} · smaskMean={t5Mean}");
        var t5Images = t5Page?["images"]?.AsArray() ?? new JsonArray();
        var t5Listed = t5Images.FirstOrDefault();
        var t5Quad = t5Listed?["quad"]?.AsArray().Select(n => n!.GetValue<double>()).ToArray() ?? Array.Empty<double>();
        var orderWant = Enumerable.Range(0, 4).OrderByDescending(i => lum[i]).ToArray();
        var orderGot = t5Quad.Length == 4 ? Enumerable.Range(0, 4).OrderByDescending(i => t5Quad[i]).ToArray() : Array.Empty<int>();
        Check("T5c 그림 목록 해시 = 그린 자리 해시 · 4분면 밝기 순서",
            t5Images.Count == 1 && t5Listed?["sha"]?.GetValue<string>() == t5Img?["sha"]?.GetValue<string>() && orderGot.SequenceEqual(orderWant),
            $"그림 {t5Images.Count} · 해시 같음={t5Listed?["sha"]?.GetValue<string>() == t5Img?["sha"]?.GetValue<string>()} · 순서 {string.Join(">", orderGot)} · 기대 {string.Join(">", orderWant)} · 밝기 {string.Join(",", t5Quad.Select(v => v.ToString("0.000")))}");
        // T5d 음성 대조 — 같은 PDF 를 ca 1.0 으로 짜면 alpha = 1(판독기가 늘 0.12 를 내는 고장이 아님)
        var t5n = PdfFacts.Read(HandPdf.OpacityProbe(lum, 255, 1.0));
        var t5nImg = t5n["pages"]?[0]?["draws"]?.AsArray().FirstOrDefault(d => d?["inline"]?.GetValue<bool>() == false);
        var t5nAlpha = t5nImg?["alpha"]?.GetValue<double>() ?? -1;
        var t5nMean = t5nImg?["smaskMean"]?.GetValue<double>() ?? -1;
        Check("T5d 음성 대조(ca 1.0 · 마스크 255)", Math.Abs(t5nAlpha - 1.0) < 0.0001 && Math.Abs(t5nMean - 1.0) < 0.0001, $"alpha={t5nAlpha} · smaskMean={t5nMean}");

        // T6 P4 꼴(PM §6-a 5) — 손으로 짠 비압축 본문 PDF: 원시 `/Filter` 0 · 본문 `(/Encrypt) Tj` 1 · 주석 꼴 0 · 열림 · 암호 아님 · h4 이름 검사 = Encrypt 있음(과잉 거절 쪽)
        var p4 = HandPdf.PlainTextPage("/Encrypt");
        var p4f = PdfFacts.Read(p4);
        var p4Word = p4f["pages"]?[0]?["words"]?.AsArray().Any(n => n?["t"]?.GetValue<string>() == "/Encrypt") == true;
        Check("T6 P4 손 편집 비압축 본문",
            NameScan.RawCount(p4, "/Filter") == 0 && NameScan.RawCount(p4, "(/Encrypt) Tj") == 1 && NameScan.RawCount(p4, "%/Encrypt") == 0
            && p4f["ok"]?.GetValue<bool>() == true && p4f["encrypted"]?.GetValue<bool>() == false && p4Word && NameScan.DecodedNames(p4).ContainsKey("Encrypt"),
            $"/Filter {NameScan.RawCount(p4, "/Filter")} · 본문 글자 {NameScan.RawCount(p4, "(/Encrypt) Tj")} · 열림 {p4f["ok"]} · 암호 {p4f["encrypted"]} · 낱말 {p4Word}");

        Console.WriteLine($"자체시험 끝 — 통과 {pass} · 실패 {fail}");
        return fail == 0 ? 0 : 1;
    }
}
