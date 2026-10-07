using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace HitPan.Tests.Installer;

/// <summary>
/// 🔴 <b>G-T6-1 · G-T6-2 · G-T6-3</b> — 20261007작12 2차수 T-6
/// (작업지시서 §6-3 · §7-1 · §7-4 · 설계 <c>docs/설계/백오피스/20261007_설계_작12_2차수_T6_설치실패문구_도달.md</c>).
/// <para>
/// 과녁: 설치 실패 화면에 고객이 보던 글자가 <b><c>{\</c> 두 글자</b>였다. 원인은
/// <c>installer/HitPan-Universal.iss</c> 의 PS <c>catch</c> 가 서버 응답 <b>본문 전체</b>(이미 JSON)를
/// 다시 JSON <c>message</c> 문자열 안에 싼 것(이중 포장) 하나다. 401·423·500·400 전부 같았다.
/// </para>
/// <para>
/// 🟢 <b>글자 검사가 아니다</b> — <c>.iss</c> 에서 PS 블록을 <b>복원</b>해 <c>powershell.exe</c> 로
/// <b>그대로 실행</b>하고, 로컬 <see cref="HttpListener"/> 가 423/401/500 한글 JSON 을 실제로 돌려준다.
/// G-T6-2 의 입력은 <b>G-T6-1 이 만든 실물 <c>ResponseFile</c></b> 이다(손으로 쓴 fixture 금지 ·
/// 1차수 G-7 함정 · 선행검증 V-A).
/// </para>
/// <para>
/// 🔴 <b>차선임을 숨기지 않는다</b> — 고객이 보는 최종 문자열을 내는 <b>실물 Pascal 파서</b>는 돌리지 못했다.
/// 이 PC 에 <b>ISCC 가 없다</b>(측정 2026-10-07 · 설치는 #29 사전승인 대상이라 PM 이 임의로 깔지 않는다).
/// 그래서 설계 §3-4 차선으로 내려왔다: 아래 <c>…Replica</c> 둘은 <b>복제본</b>이고,
/// <c>.iss</c> 의 Pascal 본문을 <b>해시로 고정</b>해 <c>.iss</c> 가 바뀌면 FAIL 시킨다(시험 갱신 강제).
/// 🚫 <b>복제본이 실물과 같다는 증명은 아니다.</b>
/// </para>
/// <para>
/// 🔴 조용한 초록 판별: <see cref="HttpListener"/> 가 못 서면 <b>스킵이 아니라 FAIL</b> ·
/// 왕복마다 걸린 시간과 PowerShell 종료코드를 단언한다(너무 빠르면 안 돈 것).
/// </para>
/// <para>⚠️ Windows 전용(<c>powershell.exe</c> 5.1 · <see cref="HttpListener"/>) ⇒ CI <c>build</c> 잡(windows-latest).</para>
/// </summary>
public sealed class InstallerBootstrapMessageGateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hp_t6_" + Guid.NewGuid().ToString("N")[..8]);

    // 🔴 .iss 의 Pascal 본문(JsonHexDigit + ExtractJsonString) 정규화 해시.
    //    .iss 를 고치면 이 게이트가 FAIL 한다 ⇒ 아래 복제본도 같이 고쳐라(설계 §3-4-1).
    private const string PascalBodyHashPinned = "1a67aba80e30b673f52d60f22ea33f072f1682a84fe8eb045abebda25df1628a";

    private static readonly string[] NineNames =
    {
        "message", "tenantCode", "companyName", "primary", "api", "tunnelToken", "tunnelId", "token", "tokenKey"
    };

    // 423 서버 message 실물 (InstallerBootstrapController.cs:164·:187 — 작업지시서 §7-2)
    private const string Msg423 = "시리얼 입력 5회 실패로 잠시 중지되었습니다. 1시간 뒤 다시 시도해주세요.";
    private const string Msg401 = "올바르지 않은 시리얼이거나 승인되지 않은 계정입니다.";
    private const string Msg500 = "부트스트랩 처리 중 오류가 발생했습니다.";
    // 🔴 쉼표를 품은 message — S-2(새 파서)를 빼면 **여기서만** 떨어진다(설계 §3-3)
    private const string MsgComma = "PC 정보가 누락되었습니다, 설치를 다시 시작해주세요.";

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[정리실패] 임시 폴더 {_root} 삭제 실패 — 사람이 지워야 한다: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[정리실패] 임시 폴더 {_root} 삭제 권한 없음 — 사람이 지워야 한다: {ex.Message}");
        }
    }

    // ============================================================
    // 레포 · .iss 읽기
    // ============================================================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new XunitException("HitPan.sln 을 못 찾았다.");
    }

    private static string IssText() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "installer", "HitPan-Universal.iss"), Encoding.UTF8);

    /// <summary>설치본이 실제로 부르는 것은 <c>powershell.exe</c>(Windows PowerShell 5.1)다 — pwsh 로 바꾸지 않는다.</summary>
    private const string PowerShellExe = "powershell.exe";

    // ============================================================
    // .iss PS 블록 복원기 (갈래 ⓐ′ — 작업지시서 §7-1 로 1순위)
    // ============================================================

    /// <summary>
    /// <c>PsScript := '…' + #13#10 + … ;</c> 를 토큰 단위로 읽어 **진짜 PowerShell 소스**로 되돌린다.
    /// 문자열 리터럴(<c>''</c> → <c>'</c>) · <c>#NN</c> 글자상수 · 식별자(변수 3개) · <c>//</c> 주석 · <c>+</c> 만 안다.
    /// 모르는 글자·식별자가 나오면 <b>FAIL</b> 한다(모양이 바뀐 것을 조용히 넘기지 않는다).
    /// </summary>
    private static string RestorePsBlock(string iss, IDictionary<string, string> vars, string apiBase)
    {
        // 🔴 ⓒ 블록 선택을 **단언**한다 — `.iss` 에 `PsScript :=` 블록이 여럿이고(측정: 4개),
        //    그중 하나는 `'}';` 로 끝나지도 않고 `ExpandConstant` 가 들어 있어 **순진한 뽑기는 두 블록을 삼킨다**.
        var blockStarts = new List<int>();
        for (int k = iss.IndexOf("PsScript :=", StringComparison.Ordinal); k >= 0;
                 k = iss.IndexOf("PsScript :=", k + 1, StringComparison.Ordinal))
            blockStarts.Add(k);
        if (blockStarts.Count < 2)
            throw new XunitException($"`PsScript :=` 블록이 {blockStarts.Count}개다 — .iss 모양이 바뀌었다. 게이트를 갱신하라.");

        int anchor = iss.IndexOf("/api/installer/bootstrap", StringComparison.Ordinal);
        if (anchor < 0) throw new XunitException(".iss 에 /api/installer/bootstrap 이 없다 — 게이트 과녁이 사라졌다.");
        int start = iss.LastIndexOf("PsScript :=", anchor, StringComparison.Ordinal);
        if (start < 0) throw new XunitException("부트스트랩 PsScript := 블록을 못 찾았다.");
        // 고른 블록이 **부트스트랩 블록**인지: 다음 블록 시작 전에 앵커가 있어야 한다.
        int next = blockStarts.FirstOrDefault(p => p > start, -1);
        if (next >= 0 && anchor > next)
            throw new XunitException("블록 선택이 틀렸다 — 앵커가 다음 PsScript 블록 뒤에 있다.");
        Console.WriteLine($"[복원기] PsScript 블록 {blockStarts.Count}개 중 위치 {start} 선택 (앵커 {anchor})");

        string ps = EvalPascalExpr(iss, start + "PsScript :=".Length, vars);
        if (!ps.Contains("{#BackofficeApi}", StringComparison.Ordinal))
            throw new XunitException("복원한 PS 에 {#BackofficeApi} 가 없다 — 블록을 잘못 집었다.");
        if (!ps.Contains("Invoke-RestMethod", StringComparison.Ordinal))
            throw new XunitException("복원한 PS 에 Invoke-RestMethod 가 없다 — 블록을 잘못 집었다.");
        return ps.Replace("{#BackofficeApi}", apiBase, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pascal 문자열 식(리터럴 · <c>#NN</c> 글자상수 · 식별자 · <c>+</c> · <c>//</c> 주석)을 평가한다.
    /// <c>;</c> <c>,</c> <c>)</c> 에서 끝난다. 모르는 글자·식별자는 FAIL(모양 변경을 조용히 넘기지 않는다).
    /// </summary>
    private static string EvalPascalExpr(string iss, int from, IDictionary<string, string> vars)
    {
        int i = from;
        var sb = new StringBuilder();
        while (i < iss.Length)
        {
            char c = iss[i];
            if (c is ' ' or '\t' or '\r' or '\n') { i++; continue; }
            if (c == '/' && i + 1 < iss.Length && iss[i + 1] == '/')
            {
                while (i < iss.Length && iss[i] != '\n') i++;
                continue;
            }
            if (c == '+') { i++; continue; }
            // 식의 끝 — PsScript 는 ';' 로, MsgBox 첫 인자는 ',' 로 끝난다(리터럴 밖에만 나온다).
            if (c is ';' or ',' or ')') break;
            if (c == '\'')
            {
                i++;
                while (i < iss.Length)
                {
                    if (iss[i] == '\'')
                    {
                        if (i + 1 < iss.Length && iss[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                        i++; break;
                    }
                    sb.Append(iss[i]); i++;
                }
                continue;
            }
            if (c == '#')
            {
                i++;
                var num = new StringBuilder();
                while (i < iss.Length && char.IsDigit(iss[i])) { num.Append(iss[i]); i++; }
                if (num.Length == 0) throw new XunitException($"# 뒤에 숫자가 없다 (위치 {i}).");
                sb.Append((char)int.Parse(num.ToString()));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var id = new StringBuilder();
                while (i < iss.Length && (char.IsLetterOrDigit(iss[i]) || iss[i] == '_')) { id.Append(iss[i]); i++; }
                string name = id.ToString();
                if (!vars.TryGetValue(name, out string? v))
                    throw new XunitException($"복원기가 모르는 식별자 '{name}' — .iss PS 블록 모양이 바뀌었다. 게이트를 갱신하라.");
                sb.Append(v);
                continue;
            }
            throw new XunitException($"복원기가 모르는 글자 '{c}' (위치 {i}) — .iss PS 블록 모양이 바뀌었다.");
        }

        return sb.ToString();
    }

    // ============================================================
    // 🔴 .iss 의 **실패 분기 세 줄**을 읽어 고객 대화상자 글자를 만든다
    //    (W-3 호출 자리 · W-4 머리글 · Pascal 고정 한글 문장까지 묶는다.
    //     2026-10-07 실측: 이게 없으면 W-3 을 되돌려도 게이트가 **0건 FAIL** 이었다.)
    // ============================================================

    private static string FailureBranch(string iss)
    {
        int s = iss.IndexOf("if Pos('\"success\":true', RawResponse) = 0 then begin", StringComparison.Ordinal);
        if (s < 0) throw new XunitException(".iss 에서 실패 분기를 못 찾았다 — 게이트 과녁이 사라졌다.");
        int e = iss.IndexOf("\n  end;", s, StringComparison.Ordinal);
        if (e < 0) throw new XunitException("실패 분기의 end; 를 못 찾았다.");
        return iss.Substring(s, e - s);
    }

    /// <summary>부트스트랩 함수 영역 — 쌍둥이 <c>CallLicenseClaimApi</c>(손대지 않는다)를 안 삼키게 잘라 둔다.</summary>
    private static string BootstrapRegion(string iss)
    {
        int s = iss.IndexOf("if Pos('\"success\":true', RawResponse) = 0 then begin", StringComparison.Ordinal);
        if (s < 0) throw new XunitException(".iss 에서 부트스트랩 실패 분기를 못 찾았다.");
        int e = iss.IndexOf("\nfunction CallLicenseClaimApi", s, StringComparison.Ordinal);
        if (e < 0) throw new XunitException("쌍둥이 CallLicenseClaimApi 경계를 못 찾았다 — 영역을 잘못 자르면 쌍둥이를 재게 된다.");
        return iss.Substring(s, e - s);
    }

    /// <summary>
    /// 🔴 <c>.iss</c> 가 그 이름에 <b>실제로 부르는</b> 파서로 값을 뽑는다(복제본 dispatch).
    /// 호출 자리를 옛 함수로 되돌리면 게이트가 갈린다 — 2026-10-07 에 이게 없어서 0건 FAIL 이었다.
    /// </summary>
    private static string ShownValue(string iss, string responseJson, string name)
    {
        return ParserFor(iss, name) switch
        {
            "ExtractJsonString" => ExtractJsonStringReplica(responseJson, name),
            "ExtractJsonValue" => ExtractJsonValueReplica(responseJson, name),
            var other => throw new XunitException($"모르는 파서 '{other}' — 복제본을 더하고 게이트를 갱신하라."),
        };
    }

    /// <summary>
    /// 🔴 <c>.iss</c> 가 그 이름에 <b>실제로 부르는 파서 이름</b>. 호출 자리를 옛 함수로 되돌리면 값이 갈린다.
    /// G-T6-5 의 실물 harness 도 이 판정을 먼저 보고 과녁을 고정한다(harness 는 한 파서만 싣는다).
    /// </summary>
    private static string ParserFor(string iss, string name)
    {
        string region = BootstrapRegion(iss);
        var m = Regex.Match(region, @"(Extract\w+)\(RawResponse, '" + Regex.Escape(name) + @"'\)");
        if (!m.Success) throw new XunitException($"부트스트랩에서 '{name}' 추출 줄을 못 찾았다 — 게이트를 갱신하라.");
        return m.Groups[1].Value;
    }

    private static string ShownMessage(string iss, string responseJson) => ShownValue(iss, responseJson, "message");

    /// <summary>
    /// 응답이 왔지만 알아볼 수 있는 <c>message</c> 가 없을 때 <c>.iss</c> 가 쓰는 <b>고정 한글 문장</b>을 평가한다.
    /// 🔴 보안 결재(폴백 ⓑ 삭제)의 지지대다 — 없어지면 FAIL.
    /// </summary>
    private static string FixedFallbackSentence(string iss)
    {
        string block = FailureBranch(iss);
        const string anchor = "if G_CompanyName = '' then";
        int a = block.IndexOf(anchor, StringComparison.Ordinal);
        if (a < 0) throw new XunitException("실패 분기의 고정 한글 폴백 줄을 못 찾았다 — 보안 결재(ⓑ 삭제)의 지지대다.");
        int assign = block.IndexOf("G_CompanyName :=", a + anchor.Length, StringComparison.Ordinal);
        if (assign < 0) throw new XunitException("고정 한글 폴백의 대입식을 못 찾았다.");
        string s = EvalPascalExpr(block, assign + "G_CompanyName :=".Length,
            new Dictionary<string, string>(StringComparer.Ordinal));
        if (s.Length == 0) throw new XunitException("고정 한글 폴백이 빈 문자열이다 — 고객이 아무 글자도 못 본다.");
        return s;
    }

    /// <summary>고객이 실제로 읽는 대화상자 글자 = <c>.iss</c> 의 MsgBox 식 + 고정 한글 폴백까지 평가한 결과.</summary>
    private static string CustomerDialogText(string iss, string responseJson)
    {
        string block = FailureBranch(iss);
        string shown = ShownMessage(iss, responseJson);

        if (shown.Length == 0) shown = FixedFallbackSentence(iss);

        int mb = block.IndexOf("MsgBox(", StringComparison.Ordinal);
        if (mb < 0) throw new XunitException("실패 분기의 MsgBox 를 못 찾았다.");
        return EvalPascalExpr(block, mb + "MsgBox(".Length,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["G_CompanyName"] = shown });
    }

    /// <summary>
    /// 🔴 <b>음성 대조군용</b> — 복원한 PS 의 <b>새 catch 조립부만</b> 옛 조립식 한 줄로 되돌린다.
    /// TLS·Invoke-RestMethod·ErrorDetails 집는 줄은 전부 실물 그대로 ⇒ 하네스가 같은 길을 간다.
    /// 못 찾으면 FAIL(하네스 고장을 초록으로 넘기지 않는다).
    /// </summary>
    private static string MakeOldAssembly(string restoredPs, string responseFile)
    {
        string oldBlock =
            "  try {\r\n" +
            "    [System.IO.File]::WriteAllText(\"" + responseFile + "\", '{\"success\":false,\"message\":\"' + " +
            "($msg -replace '\"', '\\\"') + '\"}', [System.Text.Encoding]::UTF8);\r\n" +
            "  } catch { }";

        var rx = new Regex(@"^  \$human = '';.*?^  \} catch \{ \}", RegexOptions.Multiline | RegexOptions.Singleline);
        if (!rx.IsMatch(restoredPs))
            throw new XunitException("음성대조군 — 새 catch 조립부를 못 찾았다(하네스 고장). .iss 가 바뀌었으면 게이트를 갱신하라.");
        return rx.Replace(restoredPs, _ => oldBlock, 1);
    }

    /// <summary>
    /// 🔴 <b>G-T6-4 음성 대조군용</b> — 삭제한 폴백 <b>ⓑ</b>(<c>$_.ErrorDetails.Message</c> 원문)를 되살린다.
    /// ⓑ 가 살아 있으면 <c>message</c> 키 없는 본문이 고객 글자로 새야 하고, 그때 G-T6-4 가 FAIL 해야 한다.
    /// </summary>
    private static string MakeFallbackB(string restoredPs)
    {
        const string initLine = "  $human = '';";
        const string catchLine = "catch { $human = ''; }";
        if (!restoredPs.Contains(initLine, StringComparison.Ordinal) ||
            !restoredPs.Contains(catchLine, StringComparison.Ordinal))
            throw new XunitException("ⓑ 대조군 — 폴백 줄을 못 찾았다(하네스 고장). .iss 가 바뀌었으면 게이트를 갱신하라.");
        return restoredPs
            .Replace(initLine, "  $human = $msg;", StringComparison.Ordinal)
            .Replace(catchLine, "catch { $human = $msg; }", StringComparison.Ordinal);
    }

    // ============================================================
    // 로컬 HTTP 서버 — 못 서면 스킵이 아니라 FAIL
    // ============================================================

    private sealed class FakeBackoffice : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly int _status;
        private readonly string _body;
        private readonly string _contentType;
        private int _hits;

        public string BaseUrl { get; }
        public int Hits => Volatile.Read(ref _hits);

        public FakeBackoffice(int status, string bodyJson, string contentType)
        {
            _status = status;
            _body = bodyJson;
            _contentType = contentType;

            int port = FreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                // 🔴 스킵하지 않는다 — 못 쟀으면 FAIL 이다(작업지시서 P-e).
                throw new XunitException($"HttpListener 가 서지 못했다 ({BaseUrl}) — 이 게이트는 스킵하지 않는다: {ex.Message}");
            }
            _ = Task.Run(LoopAsync);
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException ex)
                {
                    Console.Error.WriteLine($"[FakeBackoffice] 수신 종료: {ex.Message}");
                    return;
                }
                catch (ObjectDisposedException ex)
                {
                    Console.Error.WriteLine($"[FakeBackoffice] 수신기 닫힘: {ex.Message}");
                    return;
                }

                Interlocked.Increment(ref _hits);
                try
                {
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        _ = await reader.ReadToEndAsync().ConfigureAwait(false);
                    }
                    byte[] bytes = Encoding.UTF8.GetBytes(_body);
                    ctx.Response.StatusCode = _status;
                    ctx.Response.ContentType = _contentType;
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes, _cts.Token).ConfigureAwait(false);
                    ctx.Response.OutputStream.Close();
                }
                catch (HttpListenerException ex)
                {
                    Console.Error.WriteLine($"[FakeBackoffice] 응답 실패: {ex.Message}");
                }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"[FakeBackoffice] 응답 스트림 사고: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Close(); }
            catch (ObjectDisposedException ex) { Console.Error.WriteLine($"[FakeBackoffice] 이미 닫힘: {ex.Message}"); }
            _cts.Dispose();
        }
    }

    // ============================================================
    // 왕복 1회 — .iss 에서 뽑은 PS 를 실제로 실행
    // ============================================================

    private sealed record Trip(string ResponseJson, int ExitCode, long ElapsedMs, int Hits, string PsSource);

    private Trip RoundTrip(string caseName, int status, string serverBodyJson,
                          Func<string, string, string>? transform = null, string contentType = "application/json; charset=utf-8")
    {
        if (!OperatingSystem.IsWindows())
            throw new XunitException("G-T6 은 Windows 러너(CI build 잡)에서만 잰다 — 여기서 돌면 안 된다.");

        string dir = Path.Combine(_root, caseName);
        Directory.CreateDirectory(dir);
        string requestFile = Path.Combine(dir, "bootstrap-request.json");
        string responseFile = Path.Combine(dir, "bootstrap-response.json");
        string errorFile = Path.Combine(dir, "bootstrap-error.txt");
        string psFile = Path.Combine(dir, "bootstrap-call.ps1");

        File.WriteAllText(requestFile,
            "{\"licenseKey\":\"HITP00000000000000GT\",\"machineFingerprint\":\"GATE-T6\"," +
            "\"hostname\":\"GATE-T6\",\"installerVersion\":\"0.0.0-gate\"}", new UTF8Encoding(false));

        using var server = new FakeBackoffice(status, serverBodyJson, contentType);

        var vars = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RequestFile"] = requestFile,
            ["ResponseFile"] = responseFile,
            ["ErrorFile"] = errorFile,
        };
        string ps = RestorePsBlock(IssText(), vars, server.BaseUrl);
        if (transform is not null) ps = transform(ps, responseFile);
        // 🔴 BOM 을 반드시 붙인다 — Windows PowerShell 5.1 은 BOM 없는 .ps1 을 **ANSI** 로 읽는다.
        //    이 PC 의 임시 경로에 한글(사용자 이름)이 들어 있어, BOM 없이 쓰면 경로가 깨져 서버에 한 번도 못 닿는다
        //    (2026-10-07 실측: 왕복 0회 · 게이트 10건 FAIL). 설치본은 Inno SaveStringToFile(...,False)=ANSI 로 쓰고
        //    고객 PC 의 ANSI=CP949 라 맞아떨어진다 ⇒ ⚠️ 하네스와 출하본의 **파일 인코딩은 같지 않다**(개발명세서 §6).
        File.WriteAllText(psFile, ps, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var psi = new ProcessStartInfo(PowerShellExe)
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{psFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var sw = Stopwatch.StartNew();
        using var proc = Process.Start(psi) ?? throw new XunitException("powershell.exe 를 띄우지 못했다.");
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(90_000)) throw new XunitException("PowerShell 이 90초 안에 안 끝났다.");
        sw.Stop();

        Console.WriteLine($"[{caseName}] 상태 {status} · 종료코드 {proc.ExitCode} · {sw.ElapsedMilliseconds}ms · 왕복 {server.Hits}회" +
                          (transform is not null ? " · 변종(음성대조군)" : ""));
        if (stderr.Length > 0) Console.WriteLine($"[{caseName}] stderr: {stderr.Trim()}");
        if (stdout.Length > 0) Console.WriteLine($"[{caseName}] stdout: {stdout.Trim()}");

        // 🔴 조용한 초록 판별 — 왕복 0회면 아무것도 안 돈 것이다.
        if (server.Hits != 1) throw new XunitException($"[{caseName}] 서버 왕복이 1회가 아니다({server.Hits}회) — 안 돈 것이다.");
        if (sw.ElapsedMilliseconds < 100)
            throw new XunitException($"[{caseName}] {sw.ElapsedMilliseconds}ms 는 powershell.exe 가 실제로 돈 시간이 아니다.");
        if (!File.Exists(responseFile))
            throw new XunitException($"[{caseName}] ResponseFile 이 없다 — PS 가 안 돌았거나 catch 가 깨졌다.");

        return new Trip(File.ReadAllText(responseFile, Encoding.UTF8), proc.ExitCode, sw.ElapsedMilliseconds, server.Hits, ps);
    }

    private static string ErrorBody(string message) =>
        "{\"success\":false,\"message\":\"" + message.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"}";

    // ============================================================
    // G-T6-1 — 실물 PS 가 만든 ResponseFile 을 단언
    // ============================================================

    /// <summary>G-T6-1 — 423·401·500 실제 왕복 후 <c>ResponseFile</c> 에 서버 message 가 **한 겹으로** 들어 있나.</summary>
    [Theory]
    [InlineData(423, Msg423)]
    [InlineData(401, Msg401)]
    [InlineData(500, Msg500)]
    public void G_T6_1_실패응답은_서버message를_한겹으로만_담는다(int status, string serverMessage)
    {
        var trip = RoundTrip($"g1_{status}", status, ErrorBody(serverMessage));

        Assert.Equal(1, trip.ExitCode);
        // (b) 이중 포장 흔적 0건
        Assert.DoesNotContain("{\\\"", trip.ResponseJson, StringComparison.Ordinal);
        // (a) message 값이 서버 message 와 문자 단위 동일
        Assert.Equal(serverMessage, ExtractJsonStringReplica(trip.ResponseJson, "message"));
        Assert.StartsWith("{\"success\":false", trip.ResponseJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 G-T6-1 <b>음성 대조군</b> — 옛 조립식을 같은 하네스에 먹이면 <c>{\"</c> 가 다시 나와야 한다.
    /// 재현이 안 되면 하네스가 고장난 것이므로 FAIL 이다.
    /// </summary>
    [Fact]
    public void G_T6_1_음성대조군_옛조립식은_이중포장을_재현한다()
    {
        var trip = RoundTrip("g1_neg_423", 423, ErrorBody(Msg423), MakeOldAssembly);

        Assert.Equal(1, trip.ExitCode);
        Assert.Contains("{\\\"", trip.ResponseJson, StringComparison.Ordinal);
        // 옛 파서가 옛 파일에서 뽑으면 고객이 봤던 두 글자가 그대로 나온다.
        string seen = ExtractJsonValueReplica(trip.ResponseJson, "message");
        Assert.Equal("{\\", seen);
        Assert.Equal(2, seen.Length);
    }

    // ============================================================
    // G-T6-2 — 고객이 보는 최종 문자열 (입력 = G-T6-1 이 만든 실물 파일)
    // ============================================================

    /// <summary>
    /// G-T6-2 — 실물 <c>ResponseFile</c> 에서 <b>고객이 보는 최종 문자열</b>을 뽑는다.
    /// 423 이면 「1시간 뒤」가 들어 있어야 한다.
    /// ⚠️ 파서는 <b>복제본</b>이다(ISCC 없음) — 실물 Pascal 증명이 아니다.
    /// </summary>
    [Theory]
    [InlineData(423, Msg423)]
    [InlineData(401, Msg401)]
    [InlineData(500, Msg500)]
    public void G_T6_2_고객이_보는_글자가_서버문구와_같다(int status, string serverMessage)
    {
        AssertPascalBodyUnchanged();

        var trip = RoundTrip($"g2_{status}", status, ErrorBody(serverMessage));
        string iss = IssText();
        string shown = ShownMessage(iss, trip.ResponseJson);
        string dialog = CustomerDialogText(iss, trip.ResponseJson);

        Assert.Equal(serverMessage, shown);
        Assert.NotEqual(2, shown.Length);
        // 고객이 읽는 글자 전체 — 머리글(W-4)까지 .iss 에서 평가한 결과다
        Assert.Contains("설치를 계속할 수 없습니다", dialog, StringComparison.Ordinal);
        Assert.Contains(serverMessage, dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("시리얼 인증 실패", dialog, StringComparison.Ordinal);
        if (status == 423) Assert.Contains("1시간 뒤", dialog, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 G-T6-2 — <b>쉼표 케이스</b>. S-2(새 파서)를 빼면 <b>여기서만</b> 떨어진다:
    /// 옛 파서는 값 안의 쉼표에서 조용히 자르고, 새 파서는 안 자른다.
    /// </summary>
    [Fact]
    public void G_T6_2_쉼표를_품은_문구는_새파서만_안_자른다()
    {
        AssertPascalBodyUnchanged();

        var trip = RoundTrip("g2_comma", 400, ErrorBody(MsgComma));

        // 🔴 .iss 가 실제로 부르는 파서로 뽑는다 — 호출 자리를 옛 함수로 되돌리면 여기서 FAIL 한다.
        string withNew = ShownMessage(IssText(), trip.ResponseJson);
        string withOld = ExtractJsonValueReplica(trip.ResponseJson, "message");

        Assert.Equal(MsgComma, withNew);
        Assert.Contains(MsgComma, CustomerDialogText(IssText(), trip.ResponseJson), StringComparison.Ordinal);
        // 음성 대조군 — 옛 파서로는 잘린다(이 케이스가 S-2 의 유일한 검출기다)
        Assert.NotEqual(MsgComma, withOld);
        Assert.True(withOld.Length < MsgComma.Length, $"옛 파서가 자르지 않았다 — 이 케이스는 검출기가 아니다: '{withOld}'");
    }

    // ============================================================
    // G-T6-4 — message 키 없는 본문은 고객 글자로 한 조각도 새지 않는다
    //   ([3-V] 보안 병렬검증 P1 · PM 결재 2026-10-07 · 폴백 ⓑ 삭제를 동작으로 묶는 자리)
    // ============================================================

    /// <summary>프록시·Cloudflare 류 오류 HTML — <c>message</c> 키가 없고, 새면 바로 알아볼 수 있는 글자들을 심었다.</summary>
    private const string HtmlBody =
        "<html><head><title>502 Bad Gateway</title></head><body><h1>nginx/1.24.0</h1>" +
        "<p>Host: back-internal-07.hitpan.local</p><p>Ray ID: 8f3c9a21b7de4411</p>" +
        "<p>Upstream: 10.41.7.203:5080</p></body></html>";

    private static readonly string[] HtmlLeakTokens =
    {
        "nginx", "back-internal-07", "8f3c9a21b7de4411", "10.41.7.203", "Bad Gateway", "html",
    };

    /// <summary>
    /// 🔴 G-T6-4 — <c>message</c> 키가 없는 본문(502 HTML)은 고객이 보는 글자에 <b>한 조각도</b> 섞이지 않는다.
    /// 설치본은 빈 <c>message</c> 를 쓰고, 고객에게는 <c>.iss</c> 의 <b>고정 한글 문장</b>이 보인다.
    /// 원문은 <c>ErrorFile</c> 로만 남는다(그 줄은 무접촉).
    /// </summary>
    [Fact]
    public void G_T6_4_message키_없는_본문은_고객글자로_안_샌다()
    {
        var trip = RoundTrip("g4_html", 502, HtmlBody, contentType: "text/html; charset=utf-8");

        Assert.Equal(1, trip.ExitCode);
        string iss = IssText();
        Assert.Equal("", ShownMessage(iss, trip.ResponseJson));
        Assert.Contains("\"message\":\"\"", trip.ResponseJson, StringComparison.Ordinal);

        // 🔴 응답 파일에도, 고객이 읽는 글자에도 본문이 한 조각도 없어야 한다
        string dialog = CustomerDialogText(iss, trip.ResponseJson);
        foreach (string token in HtmlLeakTokens)
        {
            Assert.DoesNotContain(token, trip.ResponseJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(token, dialog, StringComparison.OrdinalIgnoreCase);
        }
        // 🔴 「안 샌다」만으로는 모자라다 — **고정 한글 문장이 실제로 떴는지**를 같이 단언한다(PM 결재 2026-10-07).
        string fixedSentence = FixedFallbackSentence(iss);
        Assert.NotEqual(0, fixedSentence.Length);
        Assert.NotEqual("알 수 없는 오류", fixedSentence);
        Assert.Contains("설치를 계속할 수 없습니다", dialog, StringComparison.Ordinal);
        Assert.Contains(fixedSentence, dialog, StringComparison.Ordinal);
        // ⓒ(.NET 예외 글자)도 이 길로는 안 온다 — 본문이 있으면 ⓒ 를 쓰지 않는다
        Assert.DoesNotContain("502", dialog, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 G-T6-4 <b>음성 대조군</b> — 삭제한 폴백 <b>ⓑ</b> 를 되살리면 그 HTML 이 실제로 새야 한다.
    /// 안 새면 G-T6-4 는 아무것도 재지 않는다는 뜻이다.
    /// </summary>
    [Fact]
    public void G_T6_4_음성대조군_폴백b를_되살리면_본문이_샌다()
    {
        var trip = RoundTrip("g4_neg_html", 502, HtmlBody, (ps, _) => MakeFallbackB(ps), "text/html; charset=utf-8");

        string shown = ShownMessage(IssText(), trip.ResponseJson);
        Assert.NotEqual("", shown);
        Assert.Contains("back-internal-07", shown, StringComparison.Ordinal);
        Assert.Contains("8f3c9a21b7de4411", shown, StringComparison.Ordinal);
    }

    // ============================================================
    // G-T6-3 — 성공 경로 9개 이름 무회귀 + 민감도 대조군
    // ============================================================

    // 🔴 상호에 `&` `<` `>` `'` `"` `\` 와 한글·공백을 전부 넣는다 — PS 5.1 ConvertTo-Json 이
    //    `& < > '` 를 \uXXXX 로, `"` `\` 를 \" \\ 로 바꿔 보낸다. 옛 파서는 그걸 못 풀어 글자가 깨졌다.
    // 🔴 2026-10-07 봉합차수 — [4] §7-3 이 독립 측정에 쓴 **쉼표 포함 38자**로 맞춘다(머지조건 2).
    //    옛 파서는 값 끝을 `"` `,` `}` 로 봤으므로 **쉼표 축이 빠지면 음성 대조군이 약해진다**.
    private const string NastyCompany = "히트판 & 공영정보 <주>, 'ERP' \"큰\" \\역슬래시\\ 테스트상사";

    private const string SuccessBody =
        "{\"success\":true,\"tenantCode\":\"T0042\"," +
        "\"companyName\":\"히트판 & 공영정보 <주>, 'ERP' \\\"큰\\\" \\\\역슬래시\\\\ 테스트상사\"," +
        "\"domain\":{\"primary\":\"test1234.hitpan.kr\",\"api\":\"api-test1234.hitpan.kr\"," +
        "\"tunnelToken\":\"TTOKEN-abc123\",\"tunnelId\":\"11111111-2222-3333-4444-555555555555\"}," +
        "\"bootstrap\":{\"token\":\"BTOKEN-xyz789\",\"tokenKey\":\"BKEY-456def\"}}";

    private static readonly Dictionary<string, string> SuccessExpected = new(StringComparer.Ordinal)
    {
        ["tenantCode"] = "T0042",
        ["companyName"] = NastyCompany,
        ["primary"] = "test1234.hitpan.kr",
        ["api"] = "api-test1234.hitpan.kr",
        ["tunnelToken"] = "TTOKEN-abc123",
        ["tunnelId"] = "11111111-2222-3333-4444-555555555555",
        ["token"] = "BTOKEN-xyz789",
        ["tokenKey"] = "BKEY-456def",
    };

    /// <summary>G-T6-3 — 성공 응답에서 <b>9개 이름이 전부 그대로</b> 뽑히나(이름 집합 전수 대조 · 숫자로 세지 않는다).</summary>
    [Fact]
    public void G_T6_3_성공경로_9개이름_전수_무회귀()
    {
        var trip = RoundTrip("g3_success", 200, SuccessBody);

        Assert.Equal(0, trip.ExitCode);
        Assert.Contains("\"success\":true", trip.ResponseJson, StringComparison.Ordinal);

        // 🔴 이름 집합으로 대조한다 — 숫자가 맞는다고 같은 집합이 아니다.
        //    그리고 값은 **.iss 가 그 이름에 실제로 부르는 파서**로 뽑는다(되돌리면 갈린다).
        string iss = IssText();
        var got = NineNames.ToDictionary(n => n, n => ShownValue(iss, trip.ResponseJson, n), StringComparer.Ordinal);
        Assert.Equal(NineNames.OrderBy(x => x, StringComparer.Ordinal),
                     got.Keys.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var kv in SuccessExpected) Assert.Equal(kv.Value, got[kv.Key]);
        // 성공 응답에는 message 가 없다 ⇒ 빈 문자열
        Assert.Equal("", got["message"]);

        // 🔴 음성 대조군 — 옛 파서로는 `&` `<` `>` `'` 가 \uXXXX 로 남아 **깨진다**.
        //    이게 안 갈리면 「9개 전부 바르게 뽑힌다」는 아무것도 재지 않는 말이다.
        string withOld = ExtractJsonValueReplica(trip.ResponseJson, "companyName");
        Assert.NotEqual(NastyCompany, withOld);
        Console.WriteLine($"[G-T6-3] 옛 파서가 내는 상호: '{withOld}' / 새 파서: '{got["companyName"]}'");
    }

    /// <summary>
    /// 🔴 <b>대리쌍(이모지)</b> — 조용히 지나가지 않는다. 실제로 재서 **되는지 안 되는지를 고정**한다.
    /// 새 함수의 <c>\uXXXX</c> 해제는 <b>1..255 만</b> 글자로 바꾼다(Unicode Inno 의 <c>Chr()</c> 를 못 쟀다 ⇒ ISCC 없음).
    /// 그 밖은 원문 글자를 남긴다. 이모지가 <c>\u</c> 로 escape 되어 오면 **복원되지 않는다**는 뜻이다.
    /// </summary>
    [Fact]
    public void G_T6_3_대리쌍_이모지_처리를_고정한다()
    {
        const string emojiCompany = "히트판😀상사";
        string body =
            "{\"success\":true,\"tenantCode\":\"T0042\",\"companyName\":\"" + emojiCompany + "\"," +
            "\"domain\":{\"primary\":\"a.b\",\"api\":\"c.d\",\"tunnelToken\":\"t\",\"tunnelId\":\"u\"}," +
            "\"bootstrap\":{\"token\":\"v\",\"tokenKey\":\"w\"}}";

        var trip = RoundTrip("g3_emoji", 200, body);
        string got = ShownValue(IssText(), trip.ResponseJson, "companyName");
        bool escaped = trip.ResponseJson.Contains("\\ud83d", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"[G-T6-3 대리쌍] 응답에 \\u escape {(escaped ? "있음" : "없음")} · 뽑힌 값 '{got}'");

        if (escaped)
        {
            // 🔴 고정: escape 되어 오면 **복원하지 않는다**(1..255 밖). 원문 글자가 남는다 — 조용히 사라지지 않는다.
            Assert.NotEqual(emojiCompany, got);
            Assert.Contains("\\ud83d", got, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("히트판", got, StringComparison.Ordinal);
        }
        else
        {
            // 🟢 고정: PS 5.1 ConvertTo-Json 이 대리쌍을 escape 하지 않으면 그대로 왕복한다.
            Assert.Equal(emojiCompany, got);
        }
    }

    /// <summary>
    /// 🔴 G-T6-3 <b>민감도 대조군</b> — 성공 응답을 <b>한 글자</b> 망가뜨리면 위 단언이 실제로 FAIL 하나.
    /// 안 갈리면 G-T6-3 은 아무것도 재지 않는다는 뜻이다.
    /// </summary>
    [Fact]
    public void G_T6_3_민감도대조군_한글자_망가뜨리면_갈린다()
    {
        var trip = RoundTrip("g3_sens", 200, SuccessBody);

        // tunnelToken → tunnelXoken (한 글자)
        string mutated = trip.ResponseJson.Replace("tunnelToken", "tunnelXoken", StringComparison.Ordinal);
        Assert.NotEqual(trip.ResponseJson, mutated);

        Assert.Equal("", ExtractJsonValueReplica(mutated, "tunnelToken"));
        Assert.NotEqual(SuccessExpected["tunnelToken"], ExtractJsonValueReplica(mutated, "tunnelToken"));
        // 나머지는 그대로여야 한다 — 대조군이 과하게 번지지 않는다는 확인
        Assert.Equal(SuccessExpected["tenantCode"], ExtractJsonValueReplica(mutated, "tenantCode"));
    }

    // ============================================================
    // G-T6-5 — 🔴 **실물 Inno Pascal** 로 뽑는다 (갈래 ⓑ1 · ISCC)
    //   왜 필수인가: Inno 의 LoadStringsFromFile 이 **UTF-8 BOM 파일을 어떤 인코딩으로 읽는지**
    //   아무도 모른다(U1). 틀리면 42자 한글이 고객 화면에서 깨지고 「닿았다」가 통째로 무너진다.
    //   복제본으로는 못 잰다 — ISCC 말고는 잴 길이 없다.
    //   🚫 이 PC 에 ISCC 를 깔지 않는다(#29) ⇒ 로컬은 표식 남기고 지나가고, CI 잡이 재는 유일한 경로다.
    // ============================================================

    private static string? FindIscc()
    {
        string? env = Environment.GetEnvironmentVariable("ISCC_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        foreach (string p in new[]
        {
            @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            @"C:\Program Files\Inno Setup 6\ISCC.exe",
        })
            if (File.Exists(p)) return p;
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string d in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string p = Path.Combine(d.Trim(), "ISCC.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// 🔴 <b>G-T6-5</b> — <c>.iss</c> 에서 Pascal 함수 텍스트를 <b>한 글자도 안 바꾸고 들어내</b> 전용 harness
    /// <c>.iss</c> 를 만들고, <b>진짜 ISCC</b> 로 컴파일해 <b>진짜 Inno Pascal</b> 로 돌린다. 복제본이 아니다.
    /// 입력은 <b>G-T6-1 이 만든 실물 응답 파일</b>이고, 출하본과 같은 <c>LoadStringsFromFile</c> 로 읽는다.
    /// <c>[Files]</c> 0건 · <c>InitializeSetup</c> 이 <c>Result := False</c> 로 끝내 **아무것도 설치하지 않는다**.
    /// ⚠️ <b>이 PC 에서는 한 번도 안 돌았다</b>(ISCC 없음 · 설치 금지) — CI 가 유일한 계측 경로다.
    /// <para>
    /// 🟢🟢 <b>1차 계측(CI run 37613228947) 결과 U1 이 열렸다</b> — 실물 Inno Pascal 이 BOM 붙은 UTF-8
    /// 응답 파일을 <c>LoadStringsFromFile</c> 로 읽어 <b>42자 한글을 정확히</b> 냈다. 그 증거는 아이러니하게도
    /// <b>이 시험의 실패 메시지</b>였다(<c>got.Length &gt;= 2</c> 단언 버그 — 아래 주석).
    /// </para>
    /// </summary>
    [Fact]
    public void G_T6_5_실물_Inno_Pascal_이_같은_글자를_낸다()
    {
        string? iscc = FindIscc();
        bool required = Environment.GetEnvironmentVariable("HITPAN_REQUIRE_ISCC") == "1";
        if (iscc is null)
        {
            if (required)
                throw new XunitException("HITPAN_REQUIRE_ISCC=1 인데 ISCC.exe 를 못 찾았다 — 이 게이트는 스킵하지 않는다.");
            string mark = Path.Combine(AppContext.BaseDirectory, "iscc-skips.log");
            File.AppendAllText(mark,
                $"{DateTime.Now:s} G-T6-5 미계측 — ISCC 없음(이 PC 에 깔지 않는다 · #29). " +
                "실물 Inno Pascal · LoadStringsFromFile 인코딩(U1) 은 CI 잡에서만 재진다.\n");
            Console.Error.WriteLine("[G-T6-5 미계측] ISCC 없음 — 복제본 결과는 실물 증명이 아니다.");
            return;
        }
        if (!required)
        {
            // 🔴 [4] N5 — ISCC 가 있어도 **전용 단계가 아니면 재지 않는다**.
            //    전체 시험(`dotnet test src/HitPan.sln`)에 섞여 있으면 이 게이트 하나가 깨질 때
            //    모든 PR 의 required `build` 가 빨강이 된다. 계측은 HITPAN_REQUIRE_ISCC=1 을 주는
            //    전용 단계에서만 하고, 그 단계는 ISCC 가 없으면 스킵이 아니라 FAIL 한다.
            string optout = Path.Combine(AppContext.BaseDirectory, "iscc-optout.log");
            File.AppendAllText(optout,
                $"{DateTime.Now:s} G-T6-5 미계측(의도) — ISCC 는 있으나 HITPAN_REQUIRE_ISCC≠1. " +
                $"전용 단계가 잰다(ISCC={iscc}).\n");
            Console.Error.WriteLine("[G-T6-5 미계측(의도)] 전용 단계(HITPAN_REQUIRE_ISCC=1)에서만 잰다 — N5.");
            return;
        }

        // 🔴 [4] N4 — 시험본과 출하본의 Inno 판이 갈리면 이 게이트가 재는 것이 출하물이 아니다.
        //    출하는 6.7.1(build-installer.yml · deploy-update.yml). 전용 단계가 그 값을 env 로 준다.
        // 🔴 봉합(10/7 · main CI run 37621671829 — 이 단언 하나로 main 과 모든 가지가 빨강이었다):
        //    앞선 식은 판을 **ISCC 가 뱉는 글자**에서 찾았다. 그게 틀렸다 —
        //      · ISCC 를 인수 없이 부르면 배너는 'Inno Setup 6 Command-Line Compiler' 로 끝나고
        //        **stdout+stderr 합본 어디에도 패치 판 번호(6.7.1)가 없다**(위 식이 합본 전체를 봤는데 FAIL — 실측).
        //      · 그래서 "배너 다음 줄을 읽자" 류의 봉합은 또 빨강이다. 글자에는 애초에 그 숫자가 없다.
        //      · 러너 ISCC 는 **실제로 6.7.1 이다** — 같은 run 의 choco 단계가
        //        'InnoSetup v6.7.1 already installed.' 라 찍었다. 즉 N4 는 사실로는 이미 충족이었고
        //        깨진 것은 **재는 방법**뿐이었다.
        //    ⇒ 1차 봉합은 판을 **바이너리 판 자원**(FileVersion·ProductVersion)에서 쟀다.
        // 🔴🔴 2차 봉합 (10/7 밤 · PR #485 run 37627205264 · [4] 반려 · 사장님 결재 「한 바퀴에 합친다」):
        //    그 1차 봉합도 빨갰다. 실측값이 답을 줬다 —
        //      FileVersion='0.0.0.0' · ProductVersion='0.0.0.0' · 배너='Inno Setup 6 Command-Line Compiler'
        //    ⇒ **ISCC.exe 는 판 자원을 아예 갖고 있지 않다.** 세 OR 가 전부 false 라
        //      러너가 진짜 6.7.1 이어도 **영구 빨강**이었다(= 늘 실패하는 체크 = 아무도 안 보는 체크).
        //    🔴 다음 수정이 **세 번째 추정**이 되면 안 된다. 그래서 이렇게 짰다:
        //      ① 판을 알 만한 **후보 출처를 전부 재서 값을 로그에 찍는다**(이 PC 엔 ISCC 가 없어
        //         어느 출처가 판을 아는지 여기서는 못 안다 — #29 설치 금지 ⇒ CI 가 유일한 계측 경로).
        //      ② 판을 **읽은 출처가 있는데 기대와 다르면 FAIL** — N4(시험본==출하본)는 그대로 지킨다.
        //      ③ 어느 출처도 못 읽으면 **미계측 표식 + 경고**를 남기고 **과녁 본체로 진행**한다.
        //         왜: 곁가지 보호장치 하나가 **본 과녁(실물 Inno Pascal · U1 인코딩)의 계측을
        //         main·가지·PR 세 run 내내 통째로 막고 있었다**(소요 325ms·200ms 가 증거).
        //         조용한 초록이 되지 않게 표식 파일을 남기고 CI 단계가 그걸 읽어 경고로 띄운다.
        //    판정식(DecideInnoVersion)은 **G-T6-5e 가 양성·음성으로 직접 잰다** — 헬퍼가 아니라 **합성식**을.
        string expectVer = Environment.GetEnvironmentVariable("HITPAN_ISCC_EXPECT_VERSION") ?? "";
        var probes = ProbeInnoVersions(iscc);
        foreach (var (src, val) in probes)
            Console.WriteLine($"[G-T6-5 판출처] {src} = '{val}'");

        var (vOk, vWhy) = DecideInnoVersion(probes, expectVer);
        Console.WriteLine($"[G-T6-5] ISCC={iscc} · 판 판정: {vWhy}");
        if (!vOk) throw new XunitException($"{vWhy}\nISCC={iscc}\n" + ProbeReport(probes));
        if (vWhy.StartsWith(UnmeasuredMark, StringComparison.Ordinal))
        {
            // 🔴 조용한 초록 방지 — 통과시키되 **흔적을 남긴다.** CI 전용 단계가 이 파일을 읽어 경고로 띄운다.
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "iscc-version-unmeasured.log"),
                $"{DateTime.Now:s} {vWhy}\n{ProbeReport(probes)}\n");
            Console.Error.WriteLine($"[G-T6-5 판 미계측] {vWhy} — N4 는 이 바퀴에서 **안 재졌다**.");
        }

        var fail = RoundTrip("g5_423", 423, ErrorBody(Msg423));
        var ok = RoundTrip("g5_ok", 200, SuccessBody);
        string failFile = Path.Combine(_root, "g5_423", "bootstrap-response.json");
        string okFile = Path.Combine(_root, "g5_ok", "bootstrap-response.json");
        Assert.True(File.Exists(failFile) && File.Exists(okFile), "G-T6-1 이 만든 실물 응답 파일이 없다.");

        string iss = IssText();
        string lifted = PascalFunction(iss, "function JsonHexDigit(") + "\r\n" +
                        PascalFunction(iss, "function ExtractJsonString(") + "\r\n" +
                        PascalFunction(iss, "function JsonEscape(");
        AssertPascalBodyUnchanged();

        // 🔴 harness 는 ExtractJsonString 하나만 싣는다 ⇒ `.iss` 가 9개 이름 전부 그 함수를 부르는지
        //    먼저 단언한다. 한 자리라도 옛 함수로 돌아가면 **harness 가 재는 것이 출하 경로가 아니다**.
        var wrongParser = NineNames.Where(n => ParserFor(iss, n) != "ExtractJsonString").ToList();
        if (wrongParser.Count > 0)
            throw new XunitException(
                "`.iss` 가 이 이름들에 ExtractJsonString 을 안 부른다 ⇒ harness 과녁이 출하 경로와 다르다: " +
                string.Join(", ", wrongParser.Select(n => $"{n}→{ParserFor(iss, n)}")));

        string dir = Path.Combine(_root, "iscc");
        Directory.CreateDirectory(dir);
        string harness = Path.Combine(dir, "BootstrapJsonSelfTest.iss");
        File.WriteAllText(harness, BuildHarnessIss(lifted, dir),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var (cExit, cOut) = RunExe(iscc, $"/Q \"{harness}\"", dir);
        if (cExit != 0) throw new XunitException($"ISCC 컴파일 실패(exit {cExit}):\n{cOut}");
        string exe = Path.Combine(dir, "t6selftest.exe");
        Assert.True(File.Exists(exe), "ISCC 가 harness EXE 를 안 만들었다.");

        // 🔴 423 = message 만 있고 companyName 은 **없다**(빈 문자열이 정답) · success = 9개 중 8개 값 + 빈 message.
        var expected423 = NineNames.ToDictionary(n => n, n => n == "message" ? Msg423 : "", StringComparer.Ordinal);
        var expectedOk = NineNames.ToDictionary(n => n, n => n == "message" ? "" : SuccessExpected[n], StringComparer.Ordinal);

        // 🔴 [4] 머지조건 1·2 — 한 경우가 터져도 **나머지가 돈다**. 앞선 차수는 423 에서 터져
        //    success(성공 경로 9개 값)에 아예 도달하지 못했고, 그래서 N3 이 미측정으로 남았다.
        var failures = new List<string>();
        foreach (var (name, input, exp) in new[]
        {
            ("423", failFile, expected423),
            ("success", okFile, expectedOk),
        })
        {
            try
            {
                MeasureOneWithRealPascal(exe, dir, name, input, exp);
            }
            catch (Exception ex)
            {
                failures.Add($"[{name}] {ex.Message}");
                Console.Error.WriteLine($"[G-T6-5 {name}] 터졌다 — 다음 경우는 계속 잰다: {ex.Message}");
            }
        }
        Console.WriteLine($"[G-T6-5] 실물 ISCC={iscc} · 판 판정='{vWhy}' · 실패 {fail.ElapsedMs}ms · 성공 {ok.ElapsedMs}ms");
        if (failures.Count > 0)
            throw new XunitException($"실물 Inno Pascal 경우 {failures.Count}/2 가 틀렸다:\n" + string.Join("\n---\n", failures));
    }

    /// <summary>
    /// 한 경우를 <b>실물 Inno Pascal</b> 로 재고 <b>9개 이름의 값</b>을 대조한다.
    /// 🔴 줄 수를 세지 않는다 — harness 가 <c>name=value</c> 로 쓰고 <c>File.ReadAllText</c> 로 받는다.
    /// (앞선 차수는 <c>ReadAllLines</c> + <c>Length &gt;= 2</c> 였는데 <c>ReadAllLines</c> 가 <b>끝의 빈 줄을 안 센다</b>.
    ///  423 입력엔 <c>companyName</c> 이 없어 둘째 줄이 빈 줄 ⇒ <b>제품이 맞는데 FAIL</b> = 민감도 역전.)
    /// </summary>
    private static void MeasureOneWithRealPascal(
        string exe, string dir, string name, string input, Dictionary<string, string> expected)
    {
        string outFile = Path.Combine(dir, $"out-{name}.txt");
        var (rExit, rOut) = RunExe(exe, $"/SILENT /IN=\"{input}\" /OUT=\"{outFile}\"", dir);
        if (!File.Exists(outFile))
            throw new XunitException($"harness 가 결과를 안 남겼다(exit {rExit}): {rOut}");

        var got = ParseHarnessOutput(File.ReadAllText(outFile, Encoding.ASCII));

        // 이름 집합 전수 대조 — 개수로 세지 않는다(값이 빈 이름도 줄이 남는다).
        Assert.Equal(NineNames.OrderBy(x => x, StringComparer.Ordinal),
                     got.Keys.OrderBy(x => x, StringComparer.Ordinal));

        var diff = new List<string>();
        foreach (string n in NineNames)
        {
            Console.WriteLine($"[G-T6-5 {name}] {n} = '{got[n]}' (기대 '{expected[n]}')");
            if (!string.Equals(got[n], expected[n], StringComparison.Ordinal))
                diff.Add($"{n}: 실물='{got[n]}'({got[n].Length}자) ≠ 기대='{expected[n]}'({expected[n].Length}자)");
        }
        // 🔴 U1 — Inno 가 BOM 붙은 UTF-8 응답 파일을 제대로 읽었나. 틀리면 한글이 깨져 여기서 갈린다.
        if (diff.Count > 0)
            throw new XunitException($"실물 Pascal 값 {NineNames.Length - diff.Count}/{NineNames.Length} 일치 · 어긋난 것:\n  " +
                                     string.Join("\n  ", diff));
        Console.WriteLine($"[G-T6-5 {name}] 🟢 실물 Inno Pascal {NineNames.Length}/{NineNames.Length} 값 일치");
    }

    /// <summary>
    /// harness 출력(<c>name=value</c> 줄들)을 이름→값 으로 받는다.
    /// 🔴 <b>줄 수를 세지 않는다.</b> 모든 줄이 <c>name=</c> 로 시작하므로 <b>빈 값도 줄이 남는다</b> ⇒
    /// 값이 없는 이름(423 의 <c>companyName</c>)이 조용히 사라지지 않는다.
    /// </summary>
    private static Dictionary<string, string> ParseHarnessOutput(string raw)
    {
        var got = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in raw.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) throw new XunitException($"harness 출력 줄이 'name=value' 모양이 아니다: '{line}'");
            got[line[..eq]] = UnescapeAsciiJson(line[(eq + 1)..]);
        }
        return got;
    }

    /// <summary>
    /// harness <c>.iss</c> 텍스트를 만든다. <b>ISCC 없이도</b> 모양을 잴 수 있게 따로 빼 뒀다(G-T6-5c).
    /// 🔴 CI 가 유일한 계측 경로라, 템플릿 오타 하나가 CI 한 바퀴를 태운다.
    /// </summary>
    private static string BuildHarnessIss(string lifted, string outDir)
    {
        // 🔴 9개 이름을 Pascal 배열로 박아 넣는다(Inno Pascal 은 배열 리터럴이 없다).
        var names = new StringBuilder();
        names.Append($"SetArrayLength(Names, {NineNames.Length});\r\n");
        for (int i = 0; i < NineNames.Length; i++)
            names.Append($"  Names[{i}] := '{NineNames[i]}';\r\n");
        return HarnessTemplate
            .Replace("OUTDIR_PLACEHOLDER", outDir, StringComparison.Ordinal)
            .Replace("LIFTED_PLACEHOLDER", lifted, StringComparison.Ordinal)
            .Replace("NAMES_PLACEHOLDER", names.ToString().TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>G-T6-5c</b> — harness <c>.iss</c> 텍스트의 모양을 ISCC 없이 고정한다.
    /// 치환 안 된 PLACEHOLDER · 빠진 이름 · <c>[Files]</c> 생김 · <c>Result := False</c> 사라짐을 잡는다.
    /// 🚫 컴파일 증명이 아니다 — 실물 Pascal 은 CI 전용 단계만 잴 수 있다.
    /// </summary>
    [Fact]
    public void G_T6_5c_harness_iss_텍스트가_모양을_갖춘다()
    {
        string iss = IssText();
        string lifted = PascalFunction(iss, "function JsonHexDigit(") + "\r\n" +
                        PascalFunction(iss, "function ExtractJsonString(") + "\r\n" +
                        PascalFunction(iss, "function JsonEscape(");
        string text = BuildHarnessIss(lifted, @"C:\tmp\hp-t6");

        Assert.DoesNotContain("PLACEHOLDER", text, StringComparison.Ordinal);
        Assert.Contains($"SetArrayLength(Names, {NineNames.Length});", text, StringComparison.Ordinal);
        for (int i = 0; i < NineNames.Length; i++)
            Assert.Contains($"Names[{i}] := '{NineNames[i]}';", text, StringComparison.Ordinal);
        Assert.Contains("JsonEscape(ExtractJsonString(Raw, Names[I]))", text, StringComparison.Ordinal);
        Assert.Contains("function JsonHexDigit(", text, StringComparison.Ordinal);
        Assert.Contains("function ExtractJsonString(", text, StringComparison.Ordinal);
        Assert.Contains("function JsonEscape(", text, StringComparison.Ordinal);
        // 🔴 아무것도 설치하지 않는다 — [Files] 0건 + InitializeSetup 이 False 로 끝낸다.
        Assert.DoesNotContain("[Files]", text, StringComparison.Ordinal);
        Assert.Contains("Result := False;", text, StringComparison.Ordinal);

        // 🔴 harness 는 ExtractJsonString 하나만 싣는다 ⇒ `.iss` 가 9개 이름 전부 그 함수를 불러야
        //    harness 가 재는 것이 출하 경로다. 한 자리라도 옛 함수로 되돌아가면 여기서 갈린다.
        //    (ISCC 없는 PC 에서도 도는 자리에 둔다 — G-T6-5 안에만 두면 이 축이 CI 에서만 깨어난다.)
        var wrong = NineNames.Where(n => ParserFor(iss, n) != "ExtractJsonString").ToList();
        Assert.True(wrong.Count == 0,
            "`.iss` 가 이 이름들에 ExtractJsonString 을 안 부른다 ⇒ harness 과녁이 출하 경로와 다르다: " +
            string.Join(", ", wrong.Select(n => $"{n}→{ParserFor(iss, n)}")));
        Console.WriteLine($"[G-T6-5c] harness 길이 {text.Length}자 · 이름 {NineNames.Length}개 · dispatch 9/9 ExtractJsonString");
    }

    /// <summary>
    /// 🔴 <b>G-T6-5d</b> — 봉합 자체의 과녁. harness 출력에서 <b>빈 값이 살아 돌아오나</b>.
    /// 앞선 차수는 <c>File.ReadAllLines</c> + <c>Length &gt;= 2</c> 였고, 423 입력의 <c>companyName</c> 이
    /// 빈 값이라 <b>끝의 빈 줄이 세어지지 않아</b> 1줄 ⇒ <b>제품이 맞는데 FAIL</b> 했다
    /// (CI run 37613228947 · <c>Total 2261 / Failed 1</c>). 민감도가 거꾸로였다.
    /// 아래 음성 대조군이 그 옛 방식을 같은 글자로 재현한다.
    /// </summary>
    [Fact]
    public void G_T6_5d_빈값도_이름줄로_살아온다_옛방식은_삼켰다()
    {
        // 423 모양 — message 만 값이 있고 나머지 8개는 빈 값이다.
        string raw = string.Concat(NineNames.Select(n => n + "=" + (n == "message" ? "x" : "") + "\r\n"));
        var got = ParseHarnessOutput(raw);

        Assert.Equal(NineNames.OrderBy(x => x, StringComparer.Ordinal),
                     got.Keys.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal("x", got["message"]);
        Assert.Equal("", got["companyName"]);

        // 🔴 음성 대조군 — 옛 방식(값만 줄바꿈으로 이어 쓰고 ReadAllLines 로 센다)은 끝의 빈 줄을 삼킨다.
        //    이게 안 갈리면 「단언을 고쳤다」가 아무것도 재지 않는 말이다.
        string old = Path.Combine(Path.GetTempPath(), "hp_t6_oldstyle_" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        try
        {
            File.WriteAllText(old, "x\r\n", Encoding.ASCII);   // message + CRLF + 빈 companyName
            string[] oldLines = File.ReadAllLines(old, Encoding.ASCII);
            Assert.Single(oldLines);                            // 2줄이 아니다 ⇒ 옛 단언 `>= 2` 는 FAIL 했다
            Console.WriteLine($"[G-T6-5d] 옛 방식 ReadAllLines = {oldLines.Length}줄 / 새 방식 이름 = {got.Count}개");
        }
        finally
        {
            try { File.Delete(old); }
            catch (IOException ex) { Console.Error.WriteLine($"[G-T6-5d] 임시파일 삭제 실패(무해): {ex.Message}"); }
        }
    }

    /// <summary>
    /// 🔴 <b>G-T6-5b</b> — G-T6-5 의 <b>과녁 문자열 모양</b>을 고정한다(ISCC 없는 PC 에서도 돈다).
    /// 423 문장은 <b>42자</b>, 성공 상호는 <b>38자</b>이고 <c>&amp; &lt; &gt; ' " \</c> 쉼표·공백·한글 <b>9축</b>을 전부 품는다.
    /// 과녁이 물러지면(축이 빠지면) 실물 Pascal 측정이 쉬운 글자만 재게 된다.
    /// </summary>
    [Fact]
    public void G_T6_5b_과녁문자열이_42자_38자_9축을_품는다()
    {
        Assert.Equal(42, Msg423.Length);
        Assert.Equal(38, NastyCompany.Length);
        foreach (string axis in new[] { "&", "<", ">", "'", "\"", "\\", ",", " ", "히트판" })
            Assert.Contains(axis, NastyCompany, StringComparison.Ordinal);
        Console.WriteLine($"[G-T6-5b] 423={Msg423.Length}자 · 상호={NastyCompany.Length}자 '{NastyCompany}'");
    }

    private const string HarnessTemplate = @"[Setup]
AppName=HitPan T6 SelfTest
AppVersion=0.0
DefaultDirName={tmp}\hp-t6-selftest
CreateAppDir=no
Uninstallable=no
DisableProgramGroupPage=yes
OutputDir=OUTDIR_PLACEHOLDER
OutputBaseFilename=t6selftest

[Code]
LIFTED_PLACEHOLDER

function InitializeSetup(): Boolean;
var
  Lines: TArrayOfString;
  Names: TArrayOfString;
  Raw, Written: String;
  I: Integer;
begin
  Raw := '';
  if LoadStringsFromFile(ExpandConstant('{param:IN}'), Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do Raw := Raw + Lines[I];
  NAMES_PLACEHOLDER
  Written := '';
  for I := 0 to GetArrayLength(Names) - 1 do
    Written := Written + Names[I] + '=' + JsonEscape(ExtractJsonString(Raw, Names[I])) + #13#10;
  SaveStringToFile(ExpandConstant('{param:OUT}'), Written, False);
  Result := False;
end;
";

    /// <summary>판 미계측 표식 — 이 글자로 시작하는 판정은 "통과시켰지만 안 쟀다"는 뜻이다.</summary>
    private const string UnmeasuredMark = "판 미계측";

    /// <summary>
    /// 🔴 Inno 판 대조 — 기대값 <c>6.7.1</c> 이 4자리 표기 <c>6.7.1.0</c> 과 맞물리게 <b>접두 일치</b>로 본다.
    /// 단 접두 뒤가 <c>.</c> 일 때만 같다고 본다 — 그래야 <c>6.7.10</c> 을 <c>6.7.1</c> 로 보지 않는다.
    /// ⚠️ 이 헬퍼만으로는 <b>잘린 기대값</b>(<c>"6"</c>·<c>"6.7"</c>)을 못 막는다(V-3) —
    /// 그 축은 <see cref="DecideInnoVersion"/> 이 기대값 <b>모양</b>으로 막는다.
    /// </summary>
    private static bool IsSameInnoVersion(string actual, string expect)
    {
        if (actual.Length == 0 || expect.Length == 0) return false;
        if (string.Equals(actual, expect, StringComparison.Ordinal)) return true;
        return actual.StartsWith(expect + ".", StringComparison.Ordinal);
    }

    /// <summary>판 모양인가 — <c>0.0.0.0</c> 처럼 전부 0 인 값은 "판 자원 없음"이지 판이 아니다.</summary>
    private static bool LooksLikeVersion(string v)
    {
        if (!Regex.IsMatch(v, @"^\d+(\.\d+)+$")) return false;
        return v.Split('.').Any(p => p.TrimStart('0').Length > 0);
    }

    /// <summary>글자 더미에서 첫 판 토큰을 뽑는다(없으면 빈 문자열).</summary>
    private static string FirstVersionToken(string text)
    {
        var m = Regex.Match(text ?? "", @"\d+\.\d+(?:\.\d+)*");
        return m.Success ? m.Value : "";
    }

    /// <summary>
    /// 🔴 판을 알 만한 <b>후보 출처를 전부</b> 재서 (출처, 값) 으로 돌려준다. 못 읽은 것도 사유와 함께 남긴다 —
    /// <b>숨기면 다음 사람이 또 추정한다.</b> 이 PC 에는 ISCC 가 없어(#29) 어느 출처가 판을 아는지
    /// 여기서는 알 수 없다 ⇒ CI 한 바퀴가 유일한 계측 경로다.
    /// </summary>
    private static List<(string Source, string Value)> ProbeInnoVersions(string iscc)
    {
        var list = new List<(string Source, string Value)>();
        void Add(string src, Func<string> read)
        {
            string v;
            try { v = (read() ?? "").Trim(); }
            catch (Exception ex) { v = $"(못 읽음: {ex.GetType().Name})"; }
            list.Add((src, v.Length == 0 ? "(빈값)" : v));
        }

        string dir = Path.GetDirectoryName(iscc) ?? "";
        string compil = Path.Combine(dir, "Compil32.exe");
        Add("ISCC.exe FileVersion", () => FileVersionInfo.GetVersionInfo(iscc).FileVersion ?? "");
        Add("ISCC.exe ProductVersion", () => FileVersionInfo.GetVersionInfo(iscc).ProductVersion ?? "");
        Add("Compil32.exe FileVersion", () => File.Exists(compil)
            ? FileVersionInfo.GetVersionInfo(compil).FileVersion ?? "" : "(파일 없음)");
        Add("Compil32.exe ProductVersion", () => File.Exists(compil)
            ? FileVersionInfo.GetVersionInfo(compil).ProductVersion ?? "" : "(파일 없음)");
        Add("ISCC 인수없음 출력", () => FirstVersionToken(RunExe(iscc, "", Path.GetTempPath()).Output));
        Add("ISCC /? 출력", () => FirstVersionToken(RunExe(iscc, "/?", Path.GetTempPath()).Output));
        Add("choco 패키지 nuspec", ChocoInnoVersion);
        Add("설치제거 레지스트리 DisplayVersion", RegistryInnoVersion);
        Add("whatsnew.htm 첫 판 토큰", () =>
        {
            string p = Path.Combine(dir, "whatsnew.htm");
            if (!File.Exists(p)) return "(파일 없음)";
            using var sr = new StreamReader(p);
            var buf = new char[8192];
            int n = sr.Read(buf, 0, buf.Length);
            return FirstVersionToken(new string(buf, 0, n));
        });
        return list;
    }

    /// <summary>choco 가 기록한 innosetup 패키지 판 — 워크플로 3곳이 거는 핀이 실제로 무엇이 됐는지.</summary>
    private static string ChocoInnoVersion()
    {
        string root = Environment.GetEnvironmentVariable("ChocolateyInstall") ?? @"C:\ProgramData\chocolatey";
        string lib = Path.Combine(root, "lib");
        if (!Directory.Exists(lib)) return "(choco lib 없음)";
        foreach (string d in Directory.GetDirectories(lib, "innosetup*"))
            foreach (string f in Directory.GetFiles(d, "*.nuspec"))
            {
                var m = Regex.Match(File.ReadAllText(f), @"<version>\s*([^<\s]+)\s*</version>",
                                    RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value;
            }
        return "(nuspec 없음)";
    }

    /// <summary>설치 제거 항목의 DisplayVersion — 설치기가 적어 둔 판.</summary>
    private static string RegistryInnoVersion()
    {
        foreach (string key in new[]
        {
            @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        })
        {
            var (exit, outp) = RunExe("reg", $"query \"{key}\" /v DisplayVersion", Path.GetTempPath());
            if (exit != 0) continue;
            var m = Regex.Match(outp, @"DisplayVersion\s+REG_SZ\s+(\S+)");
            if (m.Success) return m.Groups[1].Value;
        }
        return "(레지스트리 없음)";
    }

    /// <summary>사람이 읽을 수 있게 출처 전부를 줄로 편다 — 실패 메시지에 그대로 붙는다.</summary>
    private static string ProbeReport(IEnumerable<(string Source, string Value)> probes) =>
        string.Join("\n", probes.Select(p => $"  {p.Source} = '{p.Value}'"));

    /// <summary>
    /// 🔴 <b>판 판정식</b> — G-T6-5 가 실제로 쓰는 합성 판단. 1차 봉합은 헬퍼만 시험했고
    /// <b>합성식에는 대조군이 0건</b>이었다(P1-2). 이제 G-T6-5e 가 이 함수를 직접 잰다.
    /// 규칙: ① 기대값 없음 = <b>FAIL</b>(스킵 아님 · P1-1) ② 기대값이 잘렸으면 <b>FAIL</b>(V-3)
    /// ③ 읽힌 판이 하나라도 기대와 같으면 통과 ④ 읽혔는데 전부 다르면 <b>FAIL</b>(N4 유지)
    /// ⑤ 아무 출처도 판을 모르면 <b>미계측으로 통과</b>하되 표식을 남긴다(과녁 본체를 막지 않는다).
    /// </summary>
    private static (bool Ok, string Why) DecideInnoVersion(
        IReadOnlyList<(string Source, string Value)> probes, string expect)
    {
        if (expect.Length == 0)
            return (false, "HITPAN_ISCC_EXPECT_VERSION 이 비었다 — 전용 단계가 기대 판을 안 줬다(설정이 시험에 안 닿았다). 스킵하지 않는다(P1-1).");
        if (!Regex.IsMatch(expect, @"^\d+\.\d+\.\d+"))
            return (false, $"기대 판 모양이 아니다('{expect}') — 최소 `주.부.패치` 세 자리여야 한다. 잘린 기대값은 접두 일치로 아무 판이나 받는다(V-3).");

        var read = probes.Where(p => LooksLikeVersion(p.Value)).ToList();
        if (read.Count == 0)
            return (true, $"{UnmeasuredMark} — 판을 아는 출처가 하나도 없다(후보 {probes.Count}곳 전부 확인). N4 는 이 바퀴에서 안 재졌다.");

        var hit = read.FirstOrDefault(p => IsSameInnoVersion(p.Value, expect));
        if (hit.Source is not null)
            return (true, $"기대({expect}) 일치 — 출처 '{hit.Source}' = '{hit.Value}' (읽힌 출처 {read.Count}곳)");

        return (false, $"ISCC 판이 기대({expect})와 다르다 — 시험본·출하본 Inno 판 갈림(N4). 판을 읽은 출처 {read.Count}곳이 전부 기대와 다르다.");
    }

    /// <summary>
    /// 🔴 <b>G-T6-5e</b> — G-T6-5 가 실제로 쓰는 <b>합성 판정식</b>(<see cref="DecideInnoVersion"/>)을 잰다.
    /// <b>이 PC 에는 ISCC 가 없어</b>(#29 · 설치 금지) 본체는 못 돈다 ⇒ 판정식의 FAIL 경로를 여기서 증명한다.
    /// 1차 봉합의 이 시험은 <b>헬퍼만</b> 쟀고 합성식엔 대조군이 0건이었다([3-V] P1-2 · [4] V-3).
    /// </summary>
    [Fact]
    public void G_T6_5e_판_판정식이_양성음성을_가른다()
    {
        static List<(string Source, string Value)> P(params (string, string)[] xs) =>
            xs.Select(x => (x.Item1, x.Item2)).ToList();

        // 양성 — 출처 하나만 판을 알아도 통과한다(ISCC.exe 가 0.0.0.0 이어도)
        Assert.True(DecideInnoVersion(P(("ISCC.exe FileVersion", "0.0.0.0"), ("choco", "6.7.1")), "6.7.1").Ok,
            "다른 출처가 판을 아는데도 막는다.");
        Assert.True(DecideInnoVersion(P(("레지스트리", "6.7.1.0")), "6.7.1").Ok,
            "4자리 표기를 다르다고 본다.");

        // 🔴 음성 1 — 읽힌 판이 다르면 막는다 (N4 유지)
        Assert.False(DecideInnoVersion(P(("choco", "6.6.2")), "6.7.1").Ok, "다른 판을 통과시킨다 — N4 가 죽는다.");
        // 🔴 음성 2 — 6.7.10 을 6.7.1 로 보지 않는다 (1차 봉합의 글자 보조항 면죄부 제거 · P1-2)
        Assert.False(DecideInnoVersion(P(("choco", "6.7.10")), "6.7.1").Ok, "6.7.10 을 6.7.1 로 봤다.");
        // 🔴 음성 3 — 기대값이 비면 스킵이 아니라 FAIL (조용한 초록 · P1-1)
        // 🔴 대조실험 B 가 알려준 것(10/7 밤): `.Ok` 만 보면 이 음성은 **아무것도 안 지킨다** —
        //    P1-1 봉합을 빼도 아래 V-3 모양 검사가 빈 기대값을 대신 막아 시험이 그냥 통과했다.
        //    두 보호장치를 가르려면 **사유까지** 단언해야 한다. 이게 「게이트는 글자가 아니라 동작」이다.
        var empty = DecideInnoVersion(P(("choco", "6.7.1")), "");
        Assert.False(empty.Ok, "기대값이 없는데 통과시킨다 — 단언이 사라진다.");
        Assert.Contains("HITPAN_ISCC_EXPECT_VERSION", empty.Why, StringComparison.Ordinal);
        // 🔴 음성 4·5 — 잘린 기대값은 아무 판이나 받는다 (V-3)
        Assert.False(DecideInnoVersion(P(("choco", "6.9.9.9")), "6").Ok, "기대값 '6' 으로 6.9.9.9 를 받는다.");
        Assert.False(DecideInnoVersion(P(("choco", "6.7.9")), "6.7").Ok, "기대값 '6.7' 로 6.7.9 를 받는다.");

        // 🔴 미계측 — 아무 출처도 판을 모르면 통과하되 **반드시 표식 문구**가 붙는다(조용한 초록 방지)
        var un = DecideInnoVersion(
            P(("ISCC.exe FileVersion", "0.0.0.0"), ("Compil32.exe FileVersion", "(파일 없음)"), ("ISCC 출력", "(빈값)")),
            "6.7.1");
        Assert.True(un.Ok, "판을 못 읽었다고 과녁 본체까지 막는다 — 곁가지가 본 계측을 막은 것이 10/7 사고다.");
        Assert.StartsWith(UnmeasuredMark, un.Why);

        // 헬퍼 축(1차 봉합에서 세운 것 — 유지)
        Assert.True(IsSameInnoVersion("6.7.1.0", "6.7.1"));
        Assert.False(IsSameInnoVersion("6.7.10", "6.7.1"));
        Assert.False(IsSameInnoVersion("", "6.7.1"));

        Console.WriteLine("[G-T6-5e] 합성 판정식 양성 2 · 음성 5 · 미계측 표식 1 — FAIL 경로 증명됨.");
    }

    private static (int Exit, string Output) RunExe(string exe, string args, string cwd)
    {
        var psi = new ProcessStartInfo(exe)
        {
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new XunitException($"{exe} 를 못 띄웠다.");
        string so = p.StandardOutput.ReadToEnd();
        string se = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(180_000)) throw new XunitException($"{exe} 가 180초 안에 안 끝났다.");
        return (p.ExitCode, (so + se).Trim());
    }

    /// <summary>출하본 <c>JsonEscape</c> 가 낸 순수 ASCII 를 되돌린다 — 파일 인코딩 변수를 없앤 뒤 비교한다.</summary>
    private static string UnescapeAsciiJson(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                if (n == 'u' && i + 5 < s.Length)
                {
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 2, 4), 16));
                    i += 5; continue;
                }
                if (n is '"' or '\\') { sb.Append(n); i++; continue; }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    // ============================================================
    // 🔴 복제본 고정 — .iss 가 바뀌면 FAIL (설계 §3-4-1 차선 조건)
    // ============================================================

    /// <summary>
    /// <c>.iss</c> 의 <c>JsonHexDigit</c> + <c>ExtractJsonString</c> 본문을 정규화해 해시로 고정한다.
    /// 🔴 <c>.iss</c> 를 고치면 FAIL 한다 ⇒ 이 파일의 복제본도 같이 고쳐라.
    /// 🚫 이것은 「복제본이 실물과 같다」의 증명이 아니다(ISCC 없음 · 설계 §3-4).
    /// </summary>
    [Fact]
    public void G_T6_2b_복제본은_iss_본문에_고정된다() => AssertPascalBodyUnchanged();

    private static void AssertPascalBodyUnchanged()
    {
        string iss = IssText();
        string body = PascalFunction(iss, "function JsonHexDigit(") + "\n" +
                      PascalFunction(iss, "function ExtractJsonString(");
        string normalized = string.Join("\n",
            body.Replace("\r", "", StringComparison.Ordinal)
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();

        Assert.True(hash == PascalBodyHashPinned,
            $".iss 의 Pascal 파서 본문이 바뀌었다. 이 파일의 복제본을 같이 고치고 PascalBodyHashPinned 를 갱신하라.\n" +
            $"  지금 해시: {hash}\n  고정 해시: {PascalBodyHashPinned}");
    }

    private static string PascalFunction(string iss, string signaturePrefix)
    {
        int s = iss.IndexOf(signaturePrefix, StringComparison.Ordinal);
        if (s < 0) throw new XunitException($".iss 에 '{signaturePrefix}' 가 없다 — 봉합이 사라졌다.");
        int e = iss.IndexOf("\nend;", s, StringComparison.Ordinal);
        if (e < 0) throw new XunitException($"'{signaturePrefix}' 의 end; 를 못 찾았다.");
        return iss.Substring(s, e + "\nend;".Length - s);
    }

    // ============================================================
    // 🔴 복제본 (Pascal 전사) — 실물 Pascal 이 아니다
    // ============================================================

    /// <summary>🔴 <c>.iss</c> <c>ExtractJsonValue</c> 의 <b>복제본</b>. 옛 동작(값 끝 = <c>"</c>·<c>,</c>·<c>}</c>)을 그대로 옮겼다.</summary>
    internal static string ExtractJsonValueReplica(string json, string key)
    {
        string searchKey = "\"" + key + "\":";
        int start = json.IndexOf(searchKey, StringComparison.Ordinal);
        if (start < 0) return "";
        start += searchKey.Length;
        while (start < json.Length && (json[start] == ' ' || json[start] == '"')) start++;
        if (start + 4 <= json.Length && string.CompareOrdinal(json, start, "null", 0, 4) == 0) return "";
        int end = start;
        while (end < json.Length && json[end] != '"' && json[end] != ',' && json[end] != '}') end++;
        string r = json.Substring(start, end - start);
        while (r.Length > 0 && (r[^1] == '"' || r[^1] == ' ')) r = r[..^1];
        return r;
    }

    /// <summary>🔴 <c>.iss</c> <c>ExtractJsonString</c>(T-6 신설)의 <b>복제본</b>. 해시로 <c>.iss</c> 에 고정돼 있다.</summary>
    internal static string ExtractJsonStringReplica(string json, string key)
    {
        string searchKey = "\"" + key + "\":";
        int p = json.IndexOf(searchKey, StringComparison.Ordinal);
        if (p < 0) return "";
        p += searchKey.Length;
        while (p < json.Length && json[p] == ' ') p++;
        if (p >= json.Length) return "";
        if (json[p] != '"') return "";
        p++;

        var sb = new StringBuilder();
        while (p < json.Length)
        {
            char c = json[p];
            if (c == '"') return sb.ToString();
            if (c == '\\')
            {
                if (p + 1 >= json.Length) return sb.ToString();
                p++;
                c = json[p];
                if (c == 'n') sb.Append('\n');
                else if (c == 'r') sb.Append('\r');
                else if (c == 't') sb.Append('\t');
                else if (c == 'b') sb.Append('\b');
                else if (c == 'f') sb.Append('\f');
                else if (c == 'u')
                {
                    int code = 0;
                    bool bad = false;
                    for (int i = 1; i <= 4; i++)
                    {
                        if (p + i >= json.Length) { bad = true; continue; }
                        int d = HexDigitReplica(json[p + i]);
                        if (d < 0) bad = true; else code = code * 16 + d;
                    }
                    if (bad) return sb.ToString();
                    if (code >= 1 && code <= 255) sb.Append((char)code);
                    else sb.Append("\\u").Append(json.AsSpan(p + 1, 4));
                    p += 4;
                }
                else sb.Append(c);
            }
            else sb.Append(c);
            p++;
        }
        return sb.ToString();
    }

    private static int HexDigitReplica(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }
}
