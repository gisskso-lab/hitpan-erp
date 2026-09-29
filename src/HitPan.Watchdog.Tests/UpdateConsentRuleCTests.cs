using System.Text.RegularExpressions;
using HitPan.Watchdog;
using HitPan.Watchdog.AutoUpdate;
using HitPan.Watchdog.Stages;
using HitPan.Watchdog.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitPan.Watchdog.Tests;

/// <summary>
/// 🔴 20260929작3 갈래 W 게이트 — G-W1 ~ G-W8 (작업지시서 §5 · 설계 §3·§5·§6).
///
/// 「[예] 한 번 = 시도 한 번」(규칙 C) 을 <b>실제 Worker 인스턴스</b>로 잰다.
///   · Worker 는 DI 그래프(IntegrationLoopTests 와 같은 등록)로 만든다 — 흉내 Worker 가 아니다.
///   · DB·프로세스를 건드리는 바깥 호출 자리(Seam)에만 대역을 끼운다. 판단 코드는 운영 그대로다.
///   · 폴더 복원은 <b>임시 폴더로만</b> 한다(헌법 #39 — 설치본·운영 DB·서비스 접촉 0).
///   · SQL 자체(정렬·자가생성 DDL·기동 UPDATE)는 DB 가 있어야 잰다 ⇒
///     HitPan.Tests/Integrity/WatchdogApplyStatusDdlGateTests(db-gate 잡)가 원문 SQL 을 격리 DB 에서 돌린다.
///
/// 음성 대조군(봉합 빼면 FAIL)은 개발명세서 §4 표에 실제로 돌린 결과를 적었다.
/// </summary>
public sealed class UpdateConsentRuleCTests : IDisposable
{
    private const string V = "1.3.47";
    private readonly List<string> _tempDirs = new();

    // ══════════════════════════════════════════════════════════════
    // 준비물
    // ══════════════════════════════════════════════════════════════

    /// <summary>로컬 DB 흉내 — 동의 표(id·action)와 버전당 1행 결과표(result·consent_id).</summary>
    private sealed class FakeLocalDb
    {
        public readonly List<(long Id, string Action)> Consents = new();
        public string? Result;
        public long? UsedConsentId;
        public bool FailInProgressWrite;
        public int ApplyCalls;
        public int RejectReports;
        public readonly List<string> Writes = new();

        /// <summary>운영 SQL 과 같은 뜻: 최신 1건 = id 가장 큰 것 · used = 결과행 consent_id(없음 0).</summary>
        public Task<ConsentUsage> Read(string version, CancellationToken ct)
        {
            if (Consents.Count == 0) return Task.FromResult(ConsentUsage.NoConsent);
            var latest = Consents.OrderByDescending(c => c.Id).First();
            var decision = latest.Action == "approve" ? ConsentDecision.Approve : ConsentDecision.Reject;
            return Task.FromResult(new ConsentUsage(decision, latest.Id, UsedConsentId ?? 0));
        }

        public Task<bool> Write(string version, string result, string? detail, long? consentId, CancellationToken ct)
        {
            Writes.Add(result);
            if (result == "in_progress" && FailInProgressWrite) return Task.FromResult(false);
            Result = result;
            if (consentId is { } cid) UsedConsentId = cid;   // 운영 UPSERT: 값 있을 때만 consent_id 갱신
            return Task.FromResult(true);
        }

        /// <summary>적용 대역 — 9/29 사건처럼 디스크 부족으로 blocked 를 남기고 false.</summary>
        public Task<bool> Apply(UpdateManifest m, CancellationToken ct)
        {
            ApplyCalls++;
            Result = "blocked";
            return Task.FromResult(false);
        }

        /// <summary>기동 정리 대역 — 운영 SQL(BuildCloseInterruptedSql)과 같은 뜻.</summary>
        public Task<bool> Close(bool restored, CancellationToken ct)
        {
            if (Result == "in_progress") Result = restored ? "rolled_back" : "failed";
            return Task.FromResult(true);
        }
    }

    private static UpdateManifest Manifest(string v = V) =>
        new(v, UpdateChannel.Major, "https://example.invalid/x.zip", "00", 1, DateTime.UtcNow, null, false, null);

    /// <summary>IntegrationLoopTests 와 같은 DI 등록으로 **실제 Worker** 를 만든다(호스트는 시작하지 않는다).</summary>
    private static Worker NewWorker(FakeLocalDb db, string? appRoot = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOptions<WatchdogOptions>()
            .Configure(o =>
            {
                o.MetaPingEndpoint = "http://127.0.0.1:9";
                o.MetaPingEmergencyEndpoint = "http://127.0.0.1:9";
                o.UpdateHistoryEndpoint = "http://127.0.0.1:9";
                o.HealthCheckUrl = "http://127.0.0.1:9";
                o.Processes = new ProcessesConfig
                {
                    Services = new List<string>(),
                    HttpEndpoints = new List<HttpEndpointConfig>()
                };
            });
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<WS28A_WindowsUpdate>();
        builder.Services.AddSingleton<WS28B_PostRebootCheck>();
        builder.Services.AddSingleton<WS28C_TunnelSecret>();
        builder.Services.AddSingleton<WS28D_ServiceReinstall>();
        builder.Services.AddSingleton<WS28E_ExternalHealthCheck>();
        builder.Services.AddSingleton<WS28F_CoolDown>();
        builder.Services.AddSingleton<WS28I_FourProcess>();
        builder.Services.AddSingleton<MetaPingClient>();
        builder.Services.AddSingleton<UpdateHistoryClient>();
        builder.Services.AddSingleton<UpdateSignatureVerifier>();
        builder.Services.AddSingleton<IUpdateClient, UpdateClient>();
        builder.Services.AddSingleton<WatchdogBackupRunner>();
        builder.Services.AddSingleton<UpdateLockFile>();
        builder.Services.AddSingleton<UpdateProcessGate>();
        builder.Services.AddSingleton<UpdateOrchestrator>();
        builder.Services.AddSingleton<WatchdogConsentReader>();
        builder.Services.AddSingleton<WatchdogStatusWriter>();
        builder.Services.AddSingleton<UpdateCheckStampFile>();
        builder.Services.AddSingleton<UpdateDiskSpaceGuard>();
        builder.Services.AddHostedService<Worker>();

        var host = builder.Build();
        var worker = host.Services.GetServices<IHostedService>().OfType<Worker>().Single();

        worker.ReadConsentUsageSeam = db.Read;
        worker.WriteApplyStatusSeam = db.Write;
        worker.ApplyUpdateSeam = db.Apply;
        worker.ReportConsentRejectedSeam = _ => db.RejectReports++;
        worker.CloseInterruptedAttemptsSeam = db.Close;
        worker.AppRootSeam = () => appRoot ?? throw new InvalidOperationException("시험이 appRoot 를 안 줬다 — 설치본 경로를 쓰면 안 된다");
        return worker;
    }

    private static UpdateOrchestrator NewOrchestrator()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOptions<WatchdogOptions>().Configure(o =>
        {
            o.MetaPingEndpoint = "http://127.0.0.1:9";
            o.MetaPingEmergencyEndpoint = "http://127.0.0.1:9";
            o.UpdateHistoryEndpoint = "http://127.0.0.1:9";
            o.HealthCheckUrl = "http://127.0.0.1:9";
            o.Processes = new ProcessesConfig { Services = new List<string>(), HttpEndpoints = new List<HttpEndpointConfig>() };
        });
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<WS28I_FourProcess>();
        builder.Services.AddSingleton<MetaPingClient>();
        builder.Services.AddSingleton<UpdateHistoryClient>();
        builder.Services.AddSingleton<UpdateSignatureVerifier>();
        builder.Services.AddSingleton<IUpdateClient, UpdateClient>();
        builder.Services.AddSingleton<WatchdogBackupRunner>();
        builder.Services.AddSingleton<UpdateLockFile>();
        builder.Services.AddSingleton<UpdateProcessGate>();
        builder.Services.AddSingleton<WatchdogStatusWriter>();
        builder.Services.AddSingleton<UpdateDiskSpaceGuard>();
        builder.Services.AddSingleton<UpdateOrchestrator>();
        return builder.Build().Services.GetRequiredService<UpdateOrchestrator>();
    }

    /// <summary>임시 {app} — 폴더 이름만 만들고 안에 표시 파일 하나(어느 버전인지 가르려고).</summary>
    private string NewAppRoot(params (string Folder, string Tag)[] folders)
    {
        var root = Path.Combine(Path.GetTempPath(), "hp-w6-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        _tempDirs.Add(root);
        foreach (var (folder, tag) in folders)
        {
            var d = Path.Combine(root, folder);
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "tag.txt"), tag);
        }
        return root;
    }

    private static string? Tag(string root, string folder)
    {
        var p = Path.Combine(root, folder, "tag.txt");
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }

    private static void WriteMarker(string root) =>
        File.WriteAllText(UpdateFolderRecovery.MarkerPath(root), "2026-09-29T00:00:00Z|" + V);

    public void Dispose()
    {
        foreach (var d in _tempDirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); }
            catch (IOException ex) { Console.Error.WriteLine($"[정리실패] {d}: {ex.Message}"); }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // G-W1 — 같은 인스턴스: 실패한 [예] 뒤 새 [예]는 다시 시도한다 (N-UPD2)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W1 같은 워치독 인스턴스 — [예] id5 → blocked → [예] id6 → 적용 2회째")]
    public async Task GW1_같은_인스턴스_새_예는_재시도()
    {
        var db = new FakeLocalDb();
        var w = NewWorker(db);

        db.Consents.Add((5, "approve"));
        await w.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        Assert.Equal(1, db.ApplyCalls);
        Assert.Equal("blocked", db.Result);
        Assert.Equal(5, db.UsedConsentId);

        // 사장님이 다시 묻는 팝업에 [예] — 새 동의 id 6. 펜딩은 재발견으로 다시 선다(같은 인스턴스).
        db.Consents.Add((6, "approve"));
        await w.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        Assert.Equal(2, db.ApplyCalls);
        Assert.Equal(6, db.UsedConsentId);
    }

    [Fact(DisplayName = "G-W1b 같은 [예](id5)가 두 번 읽혀도 적용은 1회 — 동의 id 멱등")]
    public async Task GW1b_같은_동의는_한번만()
    {
        var db = new FakeLocalDb();
        var w = NewWorker(db);
        db.Consents.Add((5, "approve"));
        await w.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        await w.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        Assert.Equal(1, db.ApplyCalls);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W2 — 새 인스턴스(재기동): 옛 [예]로 묻지 않고 재시도하지 않는다 (B-3)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W2 재기동 — approve id5 + 결과행 consent_id=5 rolled_back → 적용 0 · 펜딩 해제")]
    public async Task GW2_재기동_옛_예는_재시도_안함()
    {
        var db = new FakeLocalDb { Result = "rolled_back", UsedConsentId = 5 };
        db.Consents.Add((5, "approve"));
        var w = NewWorker(db);   // 새 인스턴스 — 인메모리 집합이 비어 있다
        w.PendingConsentUpdateForTest = Manifest();

        await w.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);

        Assert.Equal(0, db.ApplyCalls);
        Assert.Null(w.PendingConsentUpdateForTest);
        Assert.DoesNotContain("in_progress", db.Writes);
    }

    [Fact(DisplayName = "G-W2b 옛 행(consent_id NULL=0) + approve id5 → 새 [예]로 1회(무회귀)")]
    public async Task GW2b_옛행_NULL은_0()
    {
        var db = new FakeLocalDb { Result = "blocked", UsedConsentId = null };
        db.Consents.Add((5, "approve"));
        var w = NewWorker(db);
        await w.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        Assert.Equal(1, db.ApplyCalls);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W3 — [나중에] 효력 유지 (#43 C-1)
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W3 approve5→reject6 → 적용 0 · 이어 approve7 → 적용 1")]
    public async Task GW3_나중에_효력_유지()
    {
        var db = new FakeLocalDb();
        db.Consents.Add((5, "approve"));
        db.Consents.Add((6, "reject"));
        var w = NewWorker(db);

        await w.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        Assert.Equal(0, db.ApplyCalls);
        Assert.Equal(1, db.RejectReports);

        db.Consents.Add((7, "approve"));
        await w.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        Assert.Equal(1, db.ApplyCalls);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W4 — in_progress 기록이 실패하면 적용하지 않는다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W4 in_progress 기록 실패 → 적용 0 · 펜딩 유지")]
    public async Task GW4_기록_실패면_적용_안함()
    {
        var db = new FakeLocalDb { FailInProgressWrite = true };
        db.Consents.Add((5, "approve"));
        var w = NewWorker(db);
        var m = Manifest();
        w.PendingConsentUpdateForTest = m;

        await w.ConsumeConsentForMajorAsync(m, CancellationToken.None);

        Assert.Equal(0, db.ApplyCalls);
        Assert.Same(m, w.PendingConsentUpdateForTest);

        // 다음 루프에 기록이 되면 그때 적용한다(같은 [예] — 아직 안 쓴 [예]다).
        db.FailInProgressWrite = false;
        await w.ConsumeConsentForMajorAsync(m, CancellationToken.None);
        Assert.Equal(1, db.ApplyCalls);
    }

    [Fact(DisplayName = "G-W4b 적용 예외 → failed 로 닫는다(in_progress 방치 0) · consent_id 유지")]
    public async Task GW4b_적용_예외면_failed()
    {
        var db = new FakeLocalDb();
        db.Consents.Add((5, "approve"));
        var w = NewWorker(db);
        w.ApplyUpdateSeam = (_, _) => throw new IOException("시험 — 적용 중 예외");

        await w.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);

        Assert.Equal("failed", db.Result);
        Assert.Equal(5, db.UsedConsentId);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W5 — 기동: 남은 in_progress 는 닫힌다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W5 기동 — in_progress 행 → failed (폴더 제자리)")]
    public async Task GW5_기동_in_progress_닫기()
    {
        var db = new FakeLocalDb { Result = "in_progress", UsedConsentId = 5 };
        var root = NewAppRoot(("web", "new"), ("api", "new"));
        var w = NewWorker(db, root);

        await w.RecoverInterruptedUpdateAtStartupAsync(CancellationToken.None);

        Assert.Equal("failed", db.Result);
        Assert.Equal(5, db.UsedConsentId);
    }

    [Fact(DisplayName = "G-W5b 기동 — 표식+R1 복원했으면 in_progress → rolled_back")]
    public async Task GW5b_복원했으면_rolled_back()
    {
        var db = new FakeLocalDb { Result = "in_progress", UsedConsentId = 5 };
        var root = NewAppRoot(("web.old", "old"), ("api", "old"));
        WriteMarker(root);
        var w = NewWorker(db, root);

        await w.RecoverInterruptedUpdateAtStartupAsync(CancellationToken.None);

        Assert.Equal("rolled_back", db.Result);
        Assert.Equal("old", Tag(root, "web"));
    }

    [Fact(DisplayName = "G-W5c 기동 정리 SQL — in_progress 만 · 고정 detail · consent_id 무접촉")]
    public void GW5c_기동_정리_SQL_모양()
    {
        var failed = WatchdogStatusWriter.BuildCloseInterruptedSql(false);
        var rolled = WatchdogStatusWriter.BuildCloseInterruptedSql(true);
        Assert.Contains("WHERE result='in_progress'", failed);
        Assert.Contains("result='failed'", failed);
        Assert.Contains("'업데이트 중 중단'", failed);
        Assert.Contains("result='rolled_back'", rolled);
        Assert.Contains("'업데이트 중 중단 — 이전 버전으로 되돌림'", rolled);
        Assert.DoesNotContain("consent_id", failed);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W6 — 폴더 복원(임시 폴더) · :351 가드
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W6 R1 — 표식 + web 없음·web.old → web 복원 · 표식 지움")]
    public void GW6_R1()
    {
        var root = NewAppRoot(("web.old", "old"), ("api", "old"));
        WriteMarker(root);
        var r = UpdateFolderRecovery.RecoverAtStartup(root, NullLogger.Instance);
        Assert.True(r.Restored);
        Assert.Equal("old", Tag(root, "web"));
        Assert.False(Directory.Exists(Path.Combine(root, "web.old")));
        Assert.False(File.Exists(UpdateFolderRecovery.MarkerPath(root)));
    }

    [Fact(DisplayName = "G-W6 R2→R3 — 표식 + api 없음·api.old · 새 web → api·web 둘 다 이전 버전")]
    public void GW6_R2_이어서_R3()
    {
        var root = NewAppRoot(("api.old", "old"), ("web", "new"), ("web.old", "old"));
        WriteMarker(root);
        var r = UpdateFolderRecovery.RecoverAtStartup(root, NullLogger.Instance);
        Assert.True(r.Restored);
        Assert.Equal("old", Tag(root, "api"));
        Assert.Equal("old", Tag(root, "web"));
        Assert.Equal("new", Tag(root, "web.failed"));
        Assert.Equal(FolderShape.Settled, r.After);
    }

    [Fact(DisplayName = "G-W6 R3 — 표식 + 새 web·옛 api·web.old → web 이전 버전")]
    public void GW6_R3()
    {
        var root = NewAppRoot(("web", "new"), ("web.old", "old"), ("api", "old"));
        WriteMarker(root);
        var r = UpdateFolderRecovery.RecoverAtStartup(root, NullLogger.Instance);
        Assert.True(r.Restored);
        Assert.Equal("old", Tag(root, "web"));
        Assert.Equal("new", Tag(root, "web.failed"));
    }

    [Fact(DisplayName = "G-W6 R4 — 표식 + 둘 다 .old → 손대지 않음")]
    public void GW6_R4_무변경()
    {
        var root = NewAppRoot(("web", "new"), ("web.old", "old"), ("api", "new"), ("api.old", "old"));
        WriteMarker(root);
        var r = UpdateFolderRecovery.RecoverAtStartup(root, NullLogger.Instance);
        Assert.False(r.Restored);
        Assert.Equal("new", Tag(root, "web"));
        Assert.Equal("new", Tag(root, "api"));
        Assert.Equal("old", Tag(root, "web.old"));
        Assert.Equal("old", Tag(root, "api.old"));
    }

    [Fact(DisplayName = "G-W6 🔴 병렬이슈 01 — 표식 없음 + R3 모양(성공 뒤 정리 잔재) → 무변경")]
    public void GW6_표식없음_R3_무변경()
    {
        // CleanupAfterSuccess 가 api.old 를 지운 뒤 web.old 삭제에 실패한 모양 — web·api 는 **정상 새 버전**이다.
        var root = NewAppRoot(("web", "new"), ("web.old", "old"), ("api", "new"));
        var r = UpdateFolderRecovery.RecoverAtStartup(root, NullLogger.Instance);
        Assert.False(r.Restored);
        Assert.Equal("new", Tag(root, "web"));
        Assert.Equal("old", Tag(root, "web.old"));
        Assert.False(Directory.Exists(Path.Combine(root, "web.failed")));
    }

    [Fact(DisplayName = "G-W6 :351 가드 — R1 모양에서 교체 전 정리가 유일한 web.old 를 지우지 않고 web 으로 되돌린다")]
    public void GW6_351_가드_유일한_구버전_보존()
    {
        var root = NewAppRoot(("web.old", "old"), ("api", "old"));
        var orch = NewOrchestrator();

        var ok = orch.ClearStaleOldDirsForSwap(root);

        Assert.True(ok);
        Assert.Equal("old", Tag(root, "web"));   // 가드가 없으면 web.old 가 지워져 구버전이 사라진다
    }

    [Fact(DisplayName = "G-W6 :351 — 정상 모양이면 종전대로 .old 를 치운다(무회귀)")]
    public void GW6_351_정상모양_무회귀()
    {
        var root = NewAppRoot(("web", "cur"), ("api", "cur"), ("web.old", "stale"), ("api.old", "stale"));
        var orch = NewOrchestrator();
        Assert.True(orch.ClearStaleOldDirsForSwap(root));
        Assert.False(Directory.Exists(Path.Combine(root, "web.old")));
        Assert.False(Directory.Exists(Path.Combine(root, "api.old")));
        Assert.Equal("cur", Tag(root, "web"));
    }

    [Fact(DisplayName = "G-W6 표식 수명 — 제자리면 지우고 제자리가 아니면 남긴다")]
    public void GW6_표식_수명()
    {
        var settled = NewAppRoot(("web", "old"), ("api", "old"), ("web.failed", "new"));
        WriteMarker(settled);
        UpdateFolderRecovery.ClearMarkerIfSettled(settled, NullLogger.Instance);
        Assert.False(File.Exists(UpdateFolderRecovery.MarkerPath(settled)));

        var broken = NewAppRoot(("web", "new"), ("web.old", "old"), ("api", "new"), ("api.old", "old"));
        WriteMarker(broken);
        UpdateFolderRecovery.ClearMarkerIfSettled(broken, NullLogger.Instance);
        Assert.True(File.Exists(UpdateFolderRecovery.MarkerPath(broken)));
    }

    // ══════════════════════════════════════════════════════════════
    // G-W7 — 보고 수: 이미 쓴 [예]는 0 · Reject 는 종전과 같다
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W7 이미 쓴 [예] → ReportConsentRejected 0회 · Reject → 호출당 1회(종전과 같음)")]
    public async Task GW7_보고_수()
    {
        var used = new FakeLocalDb { Result = "blocked", UsedConsentId = 5 };
        used.Consents.Add((5, "approve"));
        var w1 = NewWorker(used);
        await w1.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        await w1.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        Assert.Equal(0, used.RejectReports);
        Assert.Equal(0, used.ApplyCalls);

        var rej = new FakeLocalDb();
        rej.Consents.Add((6, "reject"));
        var w2 = NewWorker(rej);
        await w2.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        await w2.ConsumeConsentForMajorAsync(Manifest(), CancellationToken.None);
        Assert.Equal(2, rej.RejectReports);   // 종전: 소비 1회 = 보고 1회
    }

    // ══════════════════════════════════════════════════════════════
    // 판독 해석 — 모르면 적용 안 함 (PM 지시 9/29)
    // ══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "판독 해석 — 이상한 줄은 Error(0 으로 읽지 않는다)")]
    [InlineData("5\tapprove\tNULL")]
    [InlineData("5\tapprove")]
    [InlineData("x\tapprove\t0")]
    [InlineData("-5\tapprove\t0")]
    [InlineData("5\tapprove\t-1")]
    [InlineData("5\tapprove\t0\textra")]
    public void 판독_이상하면_Error(string raw) =>
        Assert.Equal(ConsentDecision.Error, WatchdogConsentReader.ParseLatestWithUsage(raw).Decision);

    [Fact(DisplayName = "판독 해석 — 정상 줄·빈 줄")]
    public void 판독_정상()
    {
        Assert.Equal(new ConsentUsage(ConsentDecision.Approve, 6, 5), WatchdogConsentReader.ParseLatestWithUsage("6\tapprove\t5\n"));
        Assert.Equal(new ConsentUsage(ConsentDecision.Reject, 7, 0), WatchdogConsentReader.ParseLatestWithUsage("7\treject\t0"));
        Assert.Equal(ConsentDecision.None, WatchdogConsentReader.ParseLatestWithUsage("").Decision);
        Assert.True(new ConsentUsage(ConsentDecision.Approve, 6, 5).IsFreshApprove);
        Assert.False(new ConsentUsage(ConsentDecision.Approve, 5, 5).IsFreshApprove);
    }

    [Fact(DisplayName = "판독 SQL — 정렬은 id DESC 만(시계 무관 · 병렬이슈 02)")]
    public void 판독_SQL_정렬()
    {
        var sql = WatchdogConsentReader.LatestWithUsageSqlFormat;
        Assert.Contains("ORDER BY c.id DESC LIMIT 1", sql);
        Assert.DoesNotContain("consented_at", sql);
    }

    // ══════════════════════════════════════════════════════════════
    // G-W8 (DB 없는 절반) — 자가생성 DDL 칸 = 출하 DDL 칸
    // ══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "G-W8 자가생성 DDL 칸 집합·순서 = 출하 DDL local_update_apply_status 칸")]
    public void GW8_자가생성DDL_출하DDL_칸_일치()
    {
        var clean = File.ReadAllText(Path.Combine(RepoRoot(), "installer", "hitpan_db_clean.sql"));
        var m = Regex.Match(clean, @"CREATE TABLE `local_update_apply_status` \((?<body>.*?)\) ENGINE", RegexOptions.Singleline);
        Assert.True(m.Success, "출하 DDL 에 local_update_apply_status 정의가 없다");

        var cleanCols = ColumnDefs(m.Groups["body"].Value);
        var selfBody = Regex.Match(WatchdogStatusWriter.ApplyStatusCreateSql, @"\((?<body>.*)\) ENGINE", RegexOptions.Singleline);
        Assert.True(selfBody.Success);
        var selfCols = ColumnDefs(selfBody.Groups["body"].Value.Replace(", ", ",\n"));

        Assert.Equal(cleanCols.Select(c => c.Name), selfCols.Select(c => c.Name));   // 순서까지
        Assert.Equal(cleanCols.Select(c => c.Type), selfCols.Select(c => c.Type));
        Assert.Contains(cleanCols, c => c.Name == "consent_id");

        // 옛 표 보강 ALTER 가 붙이는 칸 정의 = 출하 DDL 의 consent_id 정의(칸 순서는 끝 — 위에서 확인).
        var alter = Regex.Match(WatchdogStatusWriter.ApplyStatusAddConsentColumnSql, @"ADD COLUMN IF NOT EXISTS `consent_id` (?<def>[^;]+);");
        Assert.True(alter.Success);
        Assert.Equal(cleanCols.Last().Type, alter.Groups["def"].Value.Trim());
        Assert.Equal("consent_id", cleanCols.Last().Name);
    }

    private static List<(string Name, string Type)> ColumnDefs(string body)
    {
        var list = new List<(string, string)>();
        foreach (var raw in body.Split('\n'))
        {
            var t = raw.Trim().TrimEnd(',');
            var cm = Regex.Match(t, @"^`(?<c>[^`]+)`\s+(?<def>.+)$");
            if (!cm.Success) continue;
            var def = Regex.Replace(cm.Groups["def"].Value, @"\s+COMMENT\s+'.*$", "").Trim();
            list.Add((cm.Groups["c"].Value, def));
        }
        return list;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 출하 DDL 을 읽을 수 없다.");
    }
}
