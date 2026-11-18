// W-G1-51 갈래 Z — ⑨ 표본 만들기 (설계 §7-2 ⑨ 표 · 셈 · CTO 판정 §4-2 조건 2 · 7 · 보안상무 S1~S3)
//   ⒜ 만드는 법 = 이 파일이 건 설정(암호 있음 · 사용자 암호 빈 칸 · 편집 꼴)을 그대로 적는다
//   ⒝ 판독기 = PdfFacts(PdfPig)가 낸 열기 · 암호 판정 — 함께 기록 · 어긋나면 표시만(E7 계열 판정에 안 씀)
//   ⒞ = CI qpdf 명령 `--is-encrypted` · `--requires-password` 종료 코드 · `qpdf --version` 기록(같은 계열 · 판 기록)
//   🔴 암호 값 = 런 안에서 무작위로 만들고 버린다 — 코드 · 문서 · 로그 · 표본 설명에 고정 글자 0(설계 :248)
//   🔴 제품 코드 0 · 제품 시험 스위치 0 — 표본은 시험 쪽에서만 만든다. U3(검사 중 예외)은 시험 스위치 없이 못 만든다 → 「미측정 · 정적만」
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace PrintProbe;

public static class NineSamples
{
    private const long Mb = 1024L * 1024L;

    private static string Pw() => RandomNumberGenerator.GetHexString(24, lowercase: true);

    public static JsonObject Make(string dir)
    {
        Directory.CreateDirectory(dir);
        QuestPDF.Settings.License = LicenseType.Community;
        var rows = new JsonArray();
        var qv = Qpdf(new[] { "--version" });
        var manifest = new JsonObject
        {
            ["schema"] = "print-nine/1",
            ["qpdfVersion"] = qv.Missing ? null : FirstLine(qv.Out),
            ["qpdfMissing"] = qv.Missing,
            ["rows"] = rows,
        };

        string P(string id) => Path.Combine(dir, $"{id}.pdf");

        // P1 — 정상 스캔 꼴 1쪽(그림 1장 → 묶음으로 PDF) · 같은 런 양성 대조
        var scan = ScanLikePng(600, 840);
        Document.Create(c => c.Page(p =>
        {
            p.Size(PageSizes.A4);
            p.Margin(0);
            p.Content().Image(scan).FitArea();
        })).GeneratePdf(P("P1"));
        Add(rows, "P1", P("P1"), maker: "그림 1장 → 묶음 PDF · 암호 설정 없음", makerEncrypted: false, expect: "허용", note: null);

        // E1~E3 — 사용자 암호 40 · 128 · 256(사용자 · 소유자 둘 다)
        DocumentOperation.LoadFile(P("P1")).Encrypt(new DocumentOperation.Encryption40Bit { UserPassword = Pw(), OwnerPassword = Pw() }).Save(P("E1"));
        DocumentOperation.LoadFile(P("P1")).Encrypt(new DocumentOperation.Encryption128Bit { UserPassword = Pw(), OwnerPassword = Pw() }).Save(P("E2"));
        DocumentOperation.LoadFile(P("P1")).Encrypt(new DocumentOperation.Encryption256Bit { UserPassword = Pw(), OwnerPassword = Pw() }).Save(P("E3"));
        Add(rows, "E1", P("E1"), "사용자 암호 있음 · 40비트", true, "거절", null);
        Add(rows, "E2", P("E2"), "사용자 암호 있음 · 128비트", true, "거절", null);
        Add(rows, "E3", P("E3"), "사용자 암호 있음 · 256비트", true, "거절", null);

        // E4 · E5 — 권한 암호만(사용자 암호 빈 칸) 128 · 256 / E6 — 256 · 메타데이터 암호 안 함
        DocumentOperation.LoadFile(P("P1")).Encrypt(new DocumentOperation.Encryption128Bit { UserPassword = "", OwnerPassword = Pw() }).Save(P("E4"));
        DocumentOperation.LoadFile(P("P1")).Encrypt(new DocumentOperation.Encryption256Bit { UserPassword = "", OwnerPassword = Pw() }).Save(P("E5"));
        DocumentOperation.LoadFile(P("P1")).Encrypt(new DocumentOperation.Encryption256Bit { UserPassword = "", OwnerPassword = Pw(), EncryptMetadata = false }).Save(P("E6"));
        Add(rows, "E4", P("E4"), "권한 암호만(사용자 암호 빈 칸) · 128비트", true, "거절", null);
        Add(rows, "E5", P("E5"), "권한 암호만(사용자 암호 빈 칸) · 256비트", true, "거절", null);
        Add(rows, "E6", P("E6"), "권한 암호만 · 256비트 · 메타데이터 암호 안 함", true, "거절", null);

        // E7 · E7a · E7b · E7c — E4 의 trailer(없으면 xref 흐름 사전) `/Encrypt` 이름만 바꿈(오프셋 무변 자리)
        var e4 = File.ReadAllBytes(P("E4"));
        foreach (var (id, repl) in new[] { ("E7", "/Encr#79pt"), ("E7a", "/E#6ecrypt"), ("E7b", "/#45#6E#63#72#79#70#74"), ("E7c", "/Foo/Bar/Encrypt") })
        {
            var (edited, where) = ReplaceTrailerEncrypt(e4, repl);
            if (edited is null)
            {
                rows.Add(new JsonObject { ["id"] = id, ["made"] = false, ["why"] = "E4 에서 trailer · xref 흐름 사전의 /Encrypt 자리를 못 찾음", ["validity"] = "무효" });
                continue;
            }
            File.WriteAllBytes(P(id), edited);
            Add(rows, id, P(id), $"E4 바이트 편집 · {where} `/Encrypt` → `{repl}`", true, "거절", null);
        }

        // E8 · P2 — qpdf --object-streams=generate 로 다시 씀(E8 = E4 암호 유지 · P2 = P1)
        foreach (var (id, src, enc) in new[] { ("E8", "E4", true), ("P2", "P1", false) })
        {
            var r = Qpdf(new[] { "--object-streams=generate", P(src), P(id) });
            if (r.Missing || !File.Exists(P(id)) || (r.Code != 0 && r.Code != 3))
            {
                rows.Add(new JsonObject { ["id"] = id, ["made"] = false, ["why"] = r.Missing ? (QpdfAllowed ? "qpdf 없음(설치 실패)" : "이 잡 qpdf 0(PM §6-a 3)") : $"qpdf 종료 코드 {r.Code}", ["validity"] = r.Missing && !QpdfAllowed ? "미측정(이 잡 qpdf 0)" : "미측정(설치 실패)" });
                continue;
            }
            Add(rows, id, P(id), $"{src} 를 qpdf --object-streams=generate 로 다시 씀", enc, enc ? "거절" : "허용", null);
        }

        // P3 — E4 를 Decrypt() 로 푼 파일
        DocumentOperation.LoadFile(P("E4"), "").Decrypt().Save(P("P3"));
        Add(rows, "P3", P("P3"), "E4 를 묶음 Decrypt() 로 풂", false, "허용", null);

        // P4 — 암호 아님 · 비압축 본문에 `/Encrypt` 글자
        // ⬛ [낡은 줄 · 1차 b674c459] PdfPig 쓰기 → 내용 흐름이 압축돼 글자가 안 남으면 P1 끝 주석 줄 `%/Encrypt` 로 대신
        // ⬛   var p4 = BuildPlainTextPdf("/Encrypt"); … if (RawCount == 0) p4 = P1 + "\n%/Encrypt\n";
        // 🔴 2차(PM §6-a 5 · 설계 :257 글자대로) — 주석 꼴은 P4 로 세지 않는다. 바이트를 손으로 짠 PDF(HandPdf · 내용 흐름 `/Filter` 0 ·
        //    xref 오프셋 다시 셈)의 본문 `(/Encrypt) Tj` 로 만든다. 글자가 안 남으면 대신하지 않고 그 표본 = 무효(Judge).
        var p4 = HandPdf.PlainTextPage("/Encrypt");
        File.WriteAllBytes(P("P4"), p4);
        Add(rows, "P4", P("P4"), "손 편집 PDF · 비압축 내용 흐름(`/Filter` 0) 본문 `(/Encrypt) Tj`", false, "거절(과잉 · 받아들임)", null);

        // U1 — 구조 못 읽음(`%PDF-` 뒤 trailer · XRef 0)
        File.WriteAllBytes(P("U1"), Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Kind /Nothing >>\nendobj\n%%EOF\n"));
        Add(rows, "U1", P("U1"), "손 작성 · trailer · XRef 없음", false, "거절(판정 불능)", null);

        // U2 — 20MB 가까운 정상 PDF(그림 반복 · 쪽 ≤ 10)
        var u2 = BuildNoisePdf(3, 1450);
        File.WriteAllBytes(P("U2"), u2);
        Add(rows, "U2", P("U2"), $"잡음 그림 3쪽 · {u2.Length} 바이트", false, "허용 · h4 시간 기록", null);

        // U3 — 시험 스위치 0 이라 못 만듦
        rows.Add(new JsonObject { ["id"] = "U3", ["made"] = false, ["why"] = "검사 중 예외 · 읽기 실패 — 제품 시험 스위치 0 이라 못 만듦(설계 §7-2 U3)", ["validity"] = "미측정 · 정적만" });

        foreach (var row in rows) Judge(row!.AsObject());
        return manifest;
    }

    private static string FirstLine(string s) => s.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";

    private static void Add(JsonArray rows, string id, string path, string maker, bool makerEncrypted, string expect, string? note)
    {
        var bytes = File.ReadAllBytes(path);
        var facts = PdfFacts.Read(bytes);
        var isEnc = Qpdf(new[] { "--is-encrypted", path });
        var reqPw = Qpdf(new[] { "--requires-password", path });
        string c;
        // ⬛ [낡은 줄 · 1차] if (isEnc.Missing) c = "미측정(설치 실패)";
        // 🔴 2차(PM §6-a 3) — qpdf 를 쓰지 않는 잡(Z1 등)은 「이 잡 qpdf 0」 으로 따로 적는다(설치 실패와 섞지 않음)
        if (isEnc.Missing) c = QpdfAllowed ? "미측정(설치 실패)" : "미측정(이 잡 qpdf 0)";
        else if (isEnc.Code == 2) c = "암호 아님";
        else if (isEnc.Code == 0 && reqPw.Code == 0) c = "암호 · 비번 필요";
        else if (isEnc.Code == 0 && reqPw.Code == 3) c = "암호 · 비번 불요";
        else c = $"결과 기록(코드 {isEnc.Code}/{reqPw.Code})";
        string b = facts["ok"]?.GetValue<bool>() == true
            ? (facts["encrypted"]?.GetValue<bool>() == true ? "암호" : "암호 아님")
            : $"열기 실패({facts["error"]})";
        var raw = facts["raw"]!.AsObject();
        rows.Add(new JsonObject
        {
            ["id"] = id,
            ["made"] = true,
            ["file"] = Path.GetFileName(path),
            ["bytes"] = bytes.Length,
            ["sha256"] = facts["sha256"]?.GetValue<string>(),
            ["a"] = makerEncrypted ? "암호" : "암호 아님",
            ["aDetail"] = maker,
            ["b"] = b,
            ["c"] = c,
            ["cCodes"] = isEnc.Missing ? null : $"{isEnc.Code}/{reqPw.Code}",
            ["rawTrailer"] = raw["trailer"]?.GetValue<int>(),
            ["rawXRef"] = raw["XRef"]?.GetValue<int>(),
            ["rawEncryptLiteral"] = raw["EncryptLiteral"]?.GetValue<int>(),
            ["rawFilter"] = NameScan.RawCount(bytes, "/Filter"),
            ["p4InBody"] = id == "P4" ? NameScan.RawCount(bytes, "(/Encrypt) Tj") > 0 && NameScan.RawCount(bytes, "%/Encrypt") == 0 : null,
            ["expect"] = expect,
            ["note"] = note,
        });
    }

    /// <summary>표본 유효 조건 — 설계 §7-2 ⑨ 표 · 셈(고침 8차 · CTO §4 조건 7 · 보안상무 의견 ⓑ).</summary>
    private static void Judge(JsonObject r)
    {
        if (r["made"]?.GetValue<bool>() != true) return;
        var id = r["id"]!.GetValue<string>();
        var a = r["a"]!.GetValue<string>();
        var c = r["c"]!.GetValue<string>();
        var trailer = r["rawTrailer"]?.GetValue<int>() ?? -1;
        var xref = r["rawXRef"]?.GetValue<int>() ?? -1;
        var encLit = r["rawEncryptLiteral"]?.GetValue<int>() ?? -1;
        var bytes = r["bytes"]?.GetValue<int>() ?? 0;
        var why = new List<string>();
        var cMissing = c.StartsWith("미측정", StringComparison.Ordinal);
        bool cOk = id switch
        {
            "E1" or "E2" or "E3" => c == "암호 · 비번 필요",
            "E4" or "E5" or "E6" or "E7" or "E7a" or "E7b" or "E7c" or "E8" => c == "암호 · 비번 불요",
            "P1" or "P2" or "P3" or "P4" or "U2" => c == "암호 아님",
            "U1" => true, // ⒞ 결과 기록만(보안상무 의견 ⓑ · PM 채택)
            _ => false,
        };
        if (!cOk && !cMissing) why.Add($"⒜ {a} · ⒞ {c} 어긋남");
        if (id is "E7" or "E7a" or "E7b" && encLit != 0) why.Add($"원시 바이트 `/Encrypt` 부분 글자 {encLit}(0 이어야 · CTO §4 조건 2)");
        if (id is "E8" or "P2" && trailer != 0) why.Add($"원시 바이트 trailer {trailer}(0 이어야)");
        if (id == "U1" && (trailer != 0 || xref != 0)) why.Add($"원시 바이트 trailer {trailer} · XRef {xref}(둘 다 0 이어야)");
        if (id == "P4" && encLit < 1) why.Add("원시 바이트에 `/Encrypt` 글자 없음");
        // 🔴 2차(PM §6-a 5) — 「비압축 본문」 = 파일에 거르개 0 · 글자가 주석 줄이 아니라 내용 흐름 본문 `( … ) Tj` 안
        if (id == "P4" && (r["rawFilter"]?.GetValue<int>() ?? -1) != 0) why.Add($"원시 바이트 `/Filter` {r["rawFilter"]}(0 이어야 · 비압축 본문)");
        if (id == "P4" && r["p4InBody"]?.GetValue<bool>() != true) why.Add("`/Encrypt` 글자가 내용 흐름 본문 `( … ) Tj` 안에 없음(주석 꼴 = P4 아님)");
        if (id == "U2" && (bytes < 18 * Mb || bytes > 20 * Mb)) why.Add($"크기 {bytes}(18MB~20MB 이어야)");
        if (id == "U1" && c.StartsWith("암호", StringComparison.Ordinal)) r["mark"] = "⒞ 가 「암호」 — 표시만";
        // ⬛ [낡은 줄 · 1차] r["validity"] = why.Count > 0 ? "무효" : (cMissing && id != "U1" ? "미측정(설치 실패)" : "유효");
        r["validity"] = why.Count > 0 ? "무효" : (cMissing && id != "U1" ? c : "유효");
        r["validityWhy"] = why.Count > 0 ? string.Join(" · ", why) : null;
        if (r["b"]!.GetValue<string>() != a && id is not ("E7" or "E7a" or "E7b" or "E7c" or "U1"))
            r["bMark"] = "⒝ 어긋남 — 표시만";
    }

    private static (byte[]? edited, string where) ReplaceTrailerEncrypt(byte[] src, string repl)
    {
        var needle = Encoding.ASCII.GetBytes("/Encrypt");
        var trailerAt = LastIndexOf(src, Encoding.ASCII.GetBytes("trailer"));
        var searchFrom = trailerAt;
        var where = "trailer";
        if (trailerAt < 0)
        {
            searchFrom = LastIndexOf(src, Encoding.ASCII.GetBytes("/XRef"));
            where = "xref 흐름 사전";
            if (searchFrom < 0) return (null, "");
            // 사전 시작(<<)까지 뒤로
            var back = searchFrom;
            while (back > 1 && !(src[back - 1] == '<' && src[back] == '<')) back--;
            searchFrom = Math.Max(0, back - 1);
        }
        var at = src.AsSpan(searchFrom).IndexOf(needle);
        if (at < 0) return (null, "");
        at += searchFrom;
        // `/EncryptMetadata` 같은 더 긴 이름은 건너뜀 — 끝 글자가 이름 글자면 다음 자리
        while (at >= 0 && at + needle.Length < src.Length && IsNameChar(src[at + needle.Length]))
        {
            var next = src.AsSpan(at + 1).IndexOf(needle);
            if (next < 0) return (null, "");
            at = at + 1 + next;
        }
        var outBytes = Concat(src.AsSpan(0, at).ToArray(), Encoding.ASCII.GetBytes(repl), src.AsSpan(at + needle.Length).ToArray());
        return (outBytes, where);
    }

    private static bool IsNameChar(byte b) => !(b is 0 or 9 or 10 or 12 or 13 or 32 or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%');

    private static int LastIndexOf(byte[] hay, byte[] needle) => hay.AsSpan().LastIndexOf(needle);

    private static byte[] Concat(params byte[][] parts)
    {
        var len = parts.Sum(p => p.Length);
        var o = new byte[len];
        var i = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, o, i, p.Length); i += p.Length; }
        return o;
    }

    /// <summary>스캔 꼴 그림 — 흰 바탕 · 가로 줄 · 칸 상자(양식지 흉내 · 저작권 없음).</summary>
    public static byte[] ScanLikePng(int w, int h)
    {
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var line = y % 40 == 0 || x % 120 == 0 || (y > 60 && y < 64);
                var v = line ? (byte)90 : (byte)245;
                var i = ((y * w) + x) * 3;
                rgb[i] = v; rgb[i + 1] = v; rgb[i + 2] = v;
            }
        }
        return PngLite.EncodeRgb(w, h, rgb);
    }

    private static byte[] BuildPlainTextPdf(string text)
    {
        var b = new PdfDocumentBuilder();
        var font = b.AddStandard14Font(Standard14Font.Helvetica);
        var page = b.AddPage(595, 842);
        page.AddText(text, 12, new PdfPoint(72, 720), font);
        return b.Build();
    }

    private static byte[] BuildNoisePdf(int pages, int side)
    {
        var b = new PdfDocumentBuilder();
        for (var n = 0; n < pages; n++)
        {
            var rgb = RandomNumberGenerator.GetBytes(side * side * 3);
            var png = PngLite.EncodeRgb(side, side, rgb, System.IO.Compression.CompressionLevel.NoCompression);
            var page = b.AddPage(595, 842);
            page.AddPng(png, new PdfRectangle(20, 100, 575, 655));
        }
        return b.Build();
    }

    public sealed record QpdfResult(bool Missing, int Code, string Out);

    /// <summary>
    /// 🔴 2차(PM §6-a 3 · CTO §4-2 조건 7) — qpdf 명령은 ⑨ 를 재는 잡(고침 R1 ㉮ · 고침 R3 ⑨ⓐⓑⓒ)에서만 부른다.
    /// 그 잡의 워크플로 단계가 환경값 PRINT_QPDF=1 을 준다. 그 밖(Z1 · 자체시험 · 이 PC)은 부르지 않고 「이 잡 qpdf 0」.
    /// </summary>
    public static bool QpdfAllowed => Environment.GetEnvironmentVariable("PRINT_QPDF") == "1";

    public static QpdfResult Qpdf(string[] args)
    {
        if (!QpdfAllowed) return new QpdfResult(true, -1, "");
        try
        {
            var psi = new ProcessStartInfo("qpdf") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return new QpdfResult(true, -1, "");
            var o = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(60_000))
            {
                p.Kill(entireProcessTree: true);
                return new QpdfResult(false, -2, o);
            }
            return new QpdfResult(false, p.ExitCode, o);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Console.Error.WriteLine($"qpdf 실행 불가 {ex.GetType().Name} — ⒞ 미측정(설치 실패)");
            return new QpdfResult(true, -1, "");
        }
    }
}
