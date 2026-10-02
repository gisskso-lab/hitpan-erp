using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HitPan.API.Services.LocalSwap;

namespace HitPan.Tests.LocalSwap;

/// <summary>
/// 20260930작1 I-API — 교체 일꾼(<c>local-swap.ps1</c>)을 <b>실제로 돌리는</b> 시험 대역 폴더.
/// </summary>
/// <remarks>
/// 🟢 일꾼의 <c>-TestRoot</c> 모드를 쓴다 — 예약 작업·서비스·프로세스 종료·상태 확인·이벤트로그가 전부 <c>{root}</c> 아래 대역 파일로 바뀐다
/// (일꾼 머리말). 실제 예약 작업·서비스·설치 폴더 무접촉.
/// 되돌리기(rollback) 한 판: 지금 1.3.48 · 재료 ① <c>prev</c> = 1.3.47(해시 목록 포함).
/// </remarks>
internal sealed class LocalSwapWorkerRig : IDisposable
{
    public const string From = "1.3.48";
    public const string To = "1.3.47";

    public string Root { get; }
    public string App => Path.Combine(Root, "app");
    public string Work => Path.Combine(App, "rollback");
    public string TasksDir => Path.Combine(Root, "tasks");
    public string Ticket { get; private set; } = LocalSwapLauncher.NewTicket();

    /// <summary>일꾼 <c>-Mode</c> — 기본 rollback(작1 봉합 K2 가 update 판을 더했다).</summary>
    public string Mode { get; private set; } = SwapModes.Rollback;

    private LocalSwapWorkerRig(string root) => Root = root;

    /// <summary>되돌리기 한 판을 차린다. <paramref name="requestedAtUtc"/> = 요청서 발급 시각.</summary>
    public static LocalSwapWorkerRig Rollback(DateTime requestedAtUtc)
    {
        var rig = new LocalSwapWorkerRig(Path.Combine(Path.GetTempPath(), "hp-lsw-" + Guid.NewGuid().ToString("N")));
        Part(rig.App, "api", From);
        Part(rig.App, "web", null);
        Part(rig.App, "watchdog", From);

        var prev = Path.Combine(rig.Work, "prev");
        Part(prev, "api", To);
        Part(prev, "web", null);
        Part(prev, "watchdog", To);
        File.WriteAllText(Path.Combine(prev, "version.txt"), To);
        File.WriteAllText(Path.Combine(prev, "replaced-by.txt"), From);
        var sb = new StringBuilder();
        foreach (var f in Directory.EnumerateFiles(prev, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(prev, f);
            if (!rel.Contains(Path.DirectorySeparatorChar)) continue; // 목록 파일 셋은 빼고 세 폴더 안만
            sb.Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))).ToLowerInvariant()).Append("  ").Append(rel).Append('\n');
        }
        File.WriteAllText(Path.Combine(prev, "sha256.txt"), sb.ToString());

        var request = new SwapRequest
        {
            Ticket = rig.Ticket,
            Mode = SwapModes.Rollback,
            State = SwapStates.Requested,
            From = From,
            To = To,
            Material = new SwapMaterial { Kind = SwapMaterialKinds.Prev, Path = prev },
            AppRoot = rig.App,
            Slot = 1,
            ApiPort = 5257,
            RequestedBy = "gate-user",
            RequestedAt = requestedAtUtc,
            Entry = SwapEntries.Menu,
        };
        File.WriteAllText(Path.Combine(rig.Work, LocalSwapLauncher.RequestFileName), request.ToJson(), new UTF8Encoding(false));

        Directory.CreateDirectory(Path.Combine(rig.Root, "services"));
        File.WriteAllText(Path.Combine(rig.Root, "services", "HitPanWatchdog"), "Running");
        // API 런처가 등록해 둔 1회용 작업(대역) — 일꾼이 끝에서 스스로 지워야 한다(G-SV)
        Directory.CreateDirectory(rig.TasksDir);
        File.WriteAllText(Path.Combine(rig.TasksDir, LocalSwapLauncher.TaskName), "task");
        return rig;
    }

    private static void Part(string root, string part, string? version)
    {
        var dir = Path.Combine(root, part);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, part + ".bin"), part + "-" + (version ?? "web") + "-" + root.Length);
        if (version is not null) File.WriteAllText(Path.Combine(dir, ".testversion"), version);
    }

    /// <summary>레포의 일꾼 원본 글자.</summary>
    public static string OriginalScript() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.API", "Rollback", LocalSwapLauncher.ScriptFileName));

    /// <summary>일꾼을 돌린다. <paramref name="scriptText"/> = 대조군 사본(없으면 원본) · <paramref name="failAt"/> = 실패 주입(S1·S6·S7 …).</summary>
    public int Run(string? scriptText = null, string? failAt = null) => Run(scriptText, failAt, null);

    /// <summary>일꾼을 돌린다 — <paramref name="ticketArg"/> = <c>-Ticket</c> 인자를 바꿔 넣는다(없으면 요청서와 같은 번호 · 작1 봉합 K1 G-R2).</summary>
    public int Run(string? scriptText, string? failAt, string? ticketArg)
    {
        var script = Path.Combine(Root, "script", LocalSwapLauncher.ScriptFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, scriptText ?? OriginalScript(), new UTF8Encoding(false));
        if (failAt is not null) File.WriteAllText(Path.Combine(Root, "fail-at.txt"), failAt);

        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-Mode", Mode, "-Ticket", ticketArg ?? Ticket, "-TestRoot", Root })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("powershell 을 못 띄웠다");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(180_000))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException("일꾼이 180초 안에 안 끝났다");
        }
        Output = stdout.Result + stderr.Result;
        return p.ExitCode;
    }

    public string Output { get; private set; } = "";

    public SwapRequest Final() =>
        SwapRequest.FromJson(File.ReadAllText(Path.Combine(Work, LocalSwapLauncher.RequestFileName)))
        ?? throw new InvalidOperationException("요청서를 못 읽었다");

    /// <summary>끝난 뒤 남은 <c>HitPan-*</c> 작업(대역 폴더의 파일 이름).</summary>
    public string[] LeftoverTasks() => Directory.Exists(TasksDir)
        ? Directory.GetFiles(TasksDir).Select(Path.GetFileName).Where(n => n!.StartsWith("HitPan", StringComparison.OrdinalIgnoreCase)).Select(n => n!).ToArray()
        : Array.Empty<string>();

    // ── 작1 봉합 K1 도우미(G-S7a · G-R2 · G-S4) — 기존 시험 무변경 · 덧붙이기만 ──

    public string RequestPath => Path.Combine(Work, LocalSwapLauncher.RequestFileName);
    public string SwapLockPath => Path.Combine(Work, LocalSwapLauncher.LockFileName);
    public string UpdateLockPath => Path.Combine(App, "update.lock");

    /// <summary>요청서 한 칸을 바꿔 쓴다(API 를 건너뛴 모양 — schema·ticket·state 등).</summary>
    public void SetRequestField(string name, System.Text.Json.Nodes.JsonNode? value)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(RequestPath))!.AsObject();
        node[name] = value;
        File.WriteAllText(RequestPath, node.ToJsonString(), new UTF8Encoding(false));
    }

    /// <summary>대역 워치독 서비스 상태(<c>{root}\services\HitPanWatchdog</c>).</summary>
    public string ServiceState()
    {
        var f = Path.Combine(Root, "services", "HitPanWatchdog");
        return File.Exists(f) ? File.ReadAllText(f).Trim() : "Missing";
    }

    /// <summary><c>calls.log</c> 에서 <paramref name="contains"/> 를 품은 마지막 줄(없으면 빈 글자).</summary>
    public string LastCall(string contains) =>
        Calls().Split('\n').Select(l => l.TrimEnd('\r')).LastOrDefault(l => l.Contains(contains, StringComparison.Ordinal)) ?? "";

    /// <summary><paramref name="dir"/>\<paramref name="part"/> 의 판 표식(web 은 판 표식이 없어 파일 글자를 돌려준다).</summary>
    public static string PartMark(string dir, string part)
    {
        var v = Path.Combine(dir, part, ".testversion");
        if (File.Exists(v)) return File.ReadAllText(v).Trim();
        var bin = Path.Combine(dir, part, part + ".bin");
        return File.Exists(bin) ? File.ReadAllText(bin) : "(없음)";
    }

    // ── 작1 봉합 K2 도우미(G-CF1~CF4 · G-P3w) — 기존 시험 무변경 · 덧붙이기만 ──

    /// <summary>옮겨 싣는 고객 자료 폴더(일꾼 <c>$CarryDirs</c> 와 같아야 한다 · 사장님 결재 S-4).</summary>
    public static readonly string[] CarryDirs = { "chat-files", "HitpanBackup" };

    public string WdStaging => Path.Combine(Root, "wdstaging");
    public string ManualStaging => Path.Combine(App, "manual", "staging");
    public string SeenPath => Path.Combine(Work, "versions-seen.txt");

    /// <summary>프로그램 세 폴더(지금 판 <paramref name="from"/>)·대역 서비스·1회용 작업만 있는 판.</summary>
    private static LocalSwapWorkerRig Bare(string from)
    {
        var rig = new LocalSwapWorkerRig(Path.Combine(Path.GetTempPath(), "hp-lsw-" + Guid.NewGuid().ToString("N")));
        Part(rig.App, "api", from);
        Part(rig.App, "web", null);
        Part(rig.App, "watchdog", from);
        Directory.CreateDirectory(rig.Work);
        Directory.CreateDirectory(Path.Combine(rig.Root, "services"));
        File.WriteAllText(Path.Combine(rig.Root, "services", "HitPanWatchdog"), "Running");
        Directory.CreateDirectory(rig.TasksDir);
        File.WriteAllText(Path.Combine(rig.TasksDir, LocalSwapLauncher.TaskName), "task");
        return rig;
    }

    /// <summary>세 폴더(판 <paramref name="version"/>)를 담은 zip.</summary>
    public static void MakeZip(string zipPath, string version)
    {
        var src = zipPath + ".src";
        Part(src, "api", version);
        Part(src, "web", null);
        Part(src, "watchdog", version);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        System.IO.Compression.ZipFile.CreateFromDirectory(src, zipPath);
        Directory.Delete(src, recursive: true);
    }

    private void WriteRequest(string mode, string from, string to, SwapMaterial material, DateTime requestedAtUtc)
    {
        Mode = mode;
        var request = new SwapRequest
        {
            Ticket = Ticket,
            Mode = mode,
            State = SwapStates.Requested,
            From = from,
            To = to,
            Material = material,
            AppRoot = App,
            Slot = 1,
            ApiPort = 5257,
            RequestedBy = "gate-user",
            RequestedAt = requestedAtUtc,
            Entry = SwapEntries.Menu,
        };
        File.WriteAllText(RequestPath, request.ToJson(), new UTF8Encoding(false));
    }

    /// <summary>되돌리기 — 재료 ② 워치독 받은 zip(<c>{root}\wdstaging\hitpan-{to}.zip</c>). rig 가 요청서를 직접 쓴다(API 를 건너뛴 모양).</summary>
    public static LocalSwapWorkerRig RollbackZip(DateTime requestedAtUtc, string from = From, string to = To)
    {
        var rig = Bare(from);
        var zip = Path.Combine(rig.WdStaging, "hitpan-" + to + ".zip");
        MakeZip(zip, to);
        rig.WriteRequest(SwapModes.Rollback, from, to, new SwapMaterial { Kind = SwapMaterialKinds.StagingZip, Path = zip }, requestedAtUtc);
        return rig;
    }

    /// <summary>수동 업데이트 — 재료 <c>{app}\manual\staging\hitpan-{to}.zip</c>(해시 포함).</summary>
    public static LocalSwapWorkerRig UpdateZip(DateTime requestedAtUtc, string from, string to)
    {
        var rig = Bare(from);
        rig.NextUpdate(requestedAtUtc, from, to);
        return rig;
    }

    /// <summary>같은 판 위에서 수동 업데이트 한 번 더 — 새 번호 · 새 zip · 새 요청서.</summary>
    public void NextUpdate(DateTime requestedAtUtc, string from, string to)
    {
        Ticket = LocalSwapLauncher.NewTicket();
        var zip = Path.Combine(ManualStaging, "hitpan-" + to + ".zip");
        MakeZip(zip, to);
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant();
        WriteRequest(SwapModes.Update, from, to, new SwapMaterial { Kind = SwapMaterialKinds.ManualZip, Path = zip, Sha256 = sha }, requestedAtUtc);
        File.WriteAllText(Path.Combine(TasksDir, LocalSwapLauncher.TaskName), "task");
    }

    /// <summary><c>{app}\rollback\prev</c> 를 차린다 — 판 <paramref name="version"/> · 바꾼 판 <paramref name="replacedBy"/>.</summary>
    public void MakePrev(string version, string replacedBy)
    {
        var prev = Path.Combine(Work, "prev");
        Part(prev, "api", version);
        Part(prev, "web", null);
        Part(prev, "watchdog", version);
        File.WriteAllText(Path.Combine(prev, "version.txt"), version);
        File.WriteAllText(Path.Combine(prev, "replaced-by.txt"), replacedBy);
    }

    /// <summary>시드 — <c>{app}\api\chat-files\T1\202609\{guid}.*</c> 3개 · <c>{app}\api\HitpanBackup\*.sql</c> 2개. 돌려주는 값 = api 기준 상대경로 → SHA-256.</summary>
    public Dictionary<string, string> SeedCarry()
    {
        var api = Path.Combine(App, "api");
        var chat = Path.Combine(api, "chat-files", "T1", "202609");
        var backup = Path.Combine(api, "HitpanBackup");
        Directory.CreateDirectory(chat);
        Directory.CreateDirectory(backup);
        foreach (var ext in new[] { ".png", ".pdf", ".txt" })
            File.WriteAllBytes(Path.Combine(chat, Guid.NewGuid().ToString("N") + ext), RandomNumberGenerator.GetBytes(64));
        File.WriteAllText(Path.Combine(backup, "hitpan_20260929_0300.sql"), "-- backup 1 " + Guid.NewGuid());
        File.WriteAllText(Path.Combine(backup, "hitpan_20260930_0300.sql"), "-- backup 2 " + Guid.NewGuid());
        return CarryFiles(api);
    }

    /// <summary><paramref name="apiDir"/> 아래 옮겨 싣는 폴더 두 개의 파일 — api 기준 상대경로 → SHA-256(없으면 빈 사전).</summary>
    public static Dictionary<string, string> CarryFiles(string apiDir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in CarryDirs)
        {
            var d = Path.Combine(apiDir, c);
            if (!Directory.Exists(d)) continue;
            foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
                map[Path.GetRelativePath(apiDir, f)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
        }
        return map;
    }

    /// <summary>S6W 가 쓰는 새 첨부(api 기준 상대경로).</summary>
    public string S6wRelPath => Path.Combine("chat-files", "T1", "202609", "s6w-" + Ticket + ".bin");

    // ── 작1 봉합2 A1 도우미(G-LO2 · G-LO2b · G-LO2c · G-LO3 · G-OLD1 · 설계 §15) — 기존 시험 무변경 · 덧붙이기만 ──

    /// <summary>지난 교체가 남긴 <c>{app}\api.rbk</c> 를 차린다 — <c>chat-files</c> 1 · <c>HitpanBackup</c> 1(<paramref name="withCarry"/> 일 때) · 프로그램 파일(dll) 1.
    /// 돌려주는 값 = 옮겨 싣는 파일의 api 기준 상대경로 → SHA-256(<see cref="CarryFiles"/> 와 같은 모양).</summary>
    public Dictionary<string, string> SeedLeftoverRbk(bool withCarry = true)
    {
        var rbk = Path.Combine(App, "api.rbk");
        Directory.CreateDirectory(rbk);
        File.WriteAllText(Path.Combine(rbk, "HitPan.Old.dll"), "old-dll-" + Guid.NewGuid());
        if (withCarry)
        {
            var chat = Path.Combine(rbk, "chat-files", "T1", "202609");
            var backup = Path.Combine(rbk, "HitpanBackup");
            Directory.CreateDirectory(chat);
            Directory.CreateDirectory(backup);
            File.WriteAllBytes(Path.Combine(chat, "lo-" + Guid.NewGuid().ToString("N") + ".png"), RandomNumberGenerator.GetBytes(64));
            File.WriteAllText(Path.Combine(backup, "hitpan_20260928_0300.sql"), "-- leftover backup " + Guid.NewGuid());
        }
        return CarryFiles(rbk);
    }

    /// <summary><paramref name="dir"/> 아래 모든 파일 — 상대경로 → SHA-256(폴더가 없으면 빈 사전).</summary>
    public static Dictionary<string, string> AllFiles(string dir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir)) return map;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            map[Path.GetRelativePath(dir, f)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
        return map;
    }

    /// <summary><c>calls.log</c> 에서 <paramref name="contains"/> 를 품은 줄 수.</summary>
    public int CountCalls(string contains) =>
        Calls().Split('\n').Count(l => l.Contains(contains, StringComparison.Ordinal));

    // ── 작1 봉합2 A2 도우미(G-LO1 · G-MX2 · G-CF3b · 설계 §15) — 기존 시험 무변경 · 덧붙이기만 ──

    /// <summary>일꾼 기록에서 <paramref name="contains"/> 를 품은 줄 수.</summary>
    public int CountLog(string contains) =>
        Log().Split('\n').Count(l => l.Contains(contains, StringComparison.Ordinal));

    /// <summary>요청서 <c>parts_after</c> 칸(설계 15-2) — 칸이 없으면 <c>Present=false</c>.
    /// 🔴 런처 쪽 <c>SwapRequest.PartsAfter</c> 는 B1 갈래가 더한다 — 여기선 요청서 JSON 을 글자 그대로 읽는다.</summary>
    public (bool Present, string? Api, string? Watchdog) PartsAfter()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(RequestPath))!.AsObject();
        if (!node.TryGetPropertyValue("parts_after", out var pa) || pa is not System.Text.Json.Nodes.JsonObject o) return (false, null, null);
        return (true, o["api"]?.GetValue<string>(), o["watchdog"]?.GetValue<string>());
    }

    /// <summary><c>{app}\{part}\.testversion</c> — 없으면 null(일꾼 <c>Get-PartVersion</c> 시험 모드와 같은 출처).</summary>
    public string? LiveVersion(string part)
    {
        var f = Path.Combine(App, part, ".testversion");
        return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
    }

    // ── 작1 봉합3 C 도우미(G-PV1~PV4 · G-PV3b · 설계 §16) — 기존 시험 무변경 · 덧붙이기만 ──

    public string PrevDir => Path.Combine(Work, "prev");

    /// <summary>보관 표식 <c>{app}\rollback\prev-saving.txt</c>(<c>from|to|ticket</c> 한 줄 · 일꾼 <c>Get-PrevSavingPath</c> 와 같은 자리).</summary>
    public string PrevSavingPath => Path.Combine(Work, "prev-saving.txt");

    /// <summary>
    /// 옛 판 <paramref name="oldVersion"/> 의 보관이 반쯤 멈춘 모양(M6) — <c>prev\api</c> 만 옛 판 · <c>{app}\web.rbk</c>·<c>watchdog.rbk</c> = 옛 판 ·
    /// 표식 <c>old|markTo|옛 번호</c>(<paramref name="markTo"/> 가 없으면 지금 api 판). 부분마다 파일 둘(맨 위 + <c>lib\</c> 아래) · 옛 <c>prev</c> 는 먼저 지운다.
    /// 돌려주는 값 = <c>prev</c> 기준 상대경로(<c>api\api.bin</c> …) → SHA-256, 세 부분 전부.
    /// </summary>
    public Dictionary<string, string> SeedHalfPrev(string oldVersion, string? markTo = null)
    {
        if (Directory.Exists(PrevDir)) Directory.Delete(PrevDir, recursive: true);
        var gen = Path.Combine(Root, "oldgen");
        foreach (var p in new[] { "api", "web", "watchdog" })
        {
            Part(gen, p, p == "web" ? null : oldVersion);
            var lib = Path.Combine(gen, p, "lib");
            Directory.CreateDirectory(lib);
            File.WriteAllText(Path.Combine(lib, p + "-lib.dll"), p + "-lib-" + oldVersion + "-" + Guid.NewGuid());
        }
        var seed = AllFiles(gen);
        Directory.CreateDirectory(PrevDir);
        Directory.Move(Path.Combine(gen, "api"), Path.Combine(PrevDir, "api"));
        Directory.Move(Path.Combine(gen, "web"), Path.Combine(App, "web.rbk"));
        Directory.Move(Path.Combine(gen, "watchdog"), Path.Combine(App, "watchdog.rbk"));
        Directory.Delete(gen, recursive: true);
        File.WriteAllText(PrevSavingPath, oldVersion + "|" + (markTo ?? LiveVersion("api")) + "|" + LocalSwapLauncher.NewTicket());
        return seed;
    }

    /// <summary>살아 있는 세 폴더의 파일 — <c>{part}\상대경로</c> → SHA-256(업데이트 전 = 곧 보관될 옛 판).</summary>
    public Dictionary<string, string> LiveGeneration()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in new[] { "api", "web", "watchdog" })
            foreach (var kv in AllFiles(Path.Combine(App, p)))
                map[Path.Combine(p, kv.Key)] = kv.Value;
        return map;
    }

    /// <summary><c>prev</c> 안 세 부분의 파일만(목록 파일 셋 제외) — 상대경로 → SHA-256.</summary>
    public Dictionary<string, string> PrevParts() =>
        AllFiles(PrevDir).Where(kv => kv.Key.Contains(Path.DirectorySeparatorChar))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    // ── 작1 봉합4 D 도우미(G-PV5 · G-PV6 · 설계 §17) — 기존 시험 무변경 · 덧붙이기만 ──

    /// <summary>
    /// 살아 있는 <c>{app}\{part}</c> 에 파일 둘을 더한다 — 맨 위 <c>z-extra.css</c> · <c>lib\{part}-lib.dll</c>.
    /// 폴더 열거 순서 = <c>{part}.bin</c> → <c>z-extra.css</c> → <c>lib\…</c>(D-0 실측) ⇒ 마지막 파일을 쥐면 앞 두 파일은 옮겨지는 부분 이동이 난다.
    /// </summary>
    public void AddLiveFiles(string part)
    {
        var dir = Path.Combine(App, part);
        File.WriteAllText(Path.Combine(dir, "z-extra.css"), part + "-extra-" + Guid.NewGuid());
        var lib = Path.Combine(dir, "lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, part + "-lib.dll"), part + "-lib-" + Guid.NewGuid());
    }

    public string Calls()
    {
        var f = Path.Combine(Root, "calls.log");
        return File.Exists(f) ? File.ReadAllText(f) : "";
    }

    public string Log()
    {
        var f = Path.Combine(Work, "logs", "swap-" + Ticket + ".log");
        return File.Exists(f) ? File.ReadAllText(f) : "(일꾼 기록 없음) " + Output;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[LocalSwapWorkerRig] 임시 폴더 정리 실패: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[LocalSwapWorkerRig] 임시 폴더 정리 실패: {ex.Message}");
        }
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("레포 루트(src/HitPan.sln)를 못 찾았다");
    }
}
