using System.Data;
using System.Reflection;
using HitPan.Application.DTOs.DataReset;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>DataResetClosedGate — 비DB</b>(20261127작 W-S-01 갈래 B · 설계 §4 G-B1 ~ G-B4 · G-B5 는 <c>MdbOverwriteReseedGateTests.G_OW11</c>).
/// </summary>
/// <remarks>
/// <para>DB 를 쓰지 않는다 — 이 PC 로컬에서도 돈다. DB 쪽(D-B1 · D-B2)은 CI db-gate(<c>DataResetParentOnlyGateDbTests.BetaClosed</c> · <c>EmployeeAccountLinkGateDbTests.ResetClosed</c>).</para>
/// <para>
/// 「닫았다」 를 글자로 재지 않는다 — 운영 구현(<see cref="DataResetBetaAvailability"/>)을 넣은 진짜 <see cref="DataResetService"/> 에
/// <b>손대면 던지는 DB</b> · <b>부른 횟수를 세는 백업</b> · <b>모으는 감사</b>를 넣고 부른다. 첫 문장이 아니면 DB 가 던지거나 백업이 센다.
/// </para>
/// </remarks>
public sealed class DataResetClosedGateTests
{
    /// <summary>시험 DB 가 불렸다는 증언(이 예외가 보이면 닫힘이 DB 보다 뒤에 있다).</summary>
    private sealed class DbTouchedException(string member) : Exception($"닫힌 판인데 DB 를 만졌다: {member}");

    /// <summary>손대면 던지는 DB.</summary>
    private static IDbConnection ThrowingDb() =>
        DataResetParentOnlyGateTests.FakeProxy<IDbConnection>.Create(m => throw new DbTouchedException(m.Name));

    /// <summary>모으는 감사 — (actionType, reason) 목록.</summary>
    private sealed class CollectingAudit : IAuditService
    {
        public List<(string Action, string? Reason)> Rows { get; } = new();

        public Task LogAsync(string actionType, string entityType, string? entityId = null,
            string? beforeJson = null, string? afterJson = null, string? reason = null,
            IDbTransaction? tx = null, CancellationToken ct = default)
        {
            Rows.Add((actionType, reason));
            return Task.CompletedTask;
        }
    }

    private static (DataResetService Svc, Func<int> BackupCalls, CollectingAudit Audit) ClosedService()
    {
        var backupCalls = 0;
        var backup = DataResetParentOnlyGateTests.FakeProxy<IBackupService>.Create(m =>
        {
            backupCalls++;
            throw new InvalidOperationException($"닫힌 판인데 백업을 불렀다: {m.Name}");
        });
        var audit = new CollectingAudit();
        var svc = new DataResetService(ThrowingDb(), backup, audit, NullLogger<DataResetService>.Instance, new DataResetBetaAvailability());
        return (svc, () => backupCalls, audit);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 소스를 읽을 수 없다.");
    }

    /// <summary>어셈블리의 형 목록 — 일부 형이 못 읽혀도(플랫폼 의존) 읽힌 형으로 센다(#15 — 못 읽은 수는 출력에 남김).</summary>
    private static IEnumerable<Type> LoadableTypes(Assembly a)
    {
        try
        {
            return a.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Console.Error.WriteLine($"[G-B4] {a.GetName().Name} 형 일부를 못 읽음: {ex.LoaderExceptions.Length}건");
            return ex.Types.Where(t => t is not null).Cast<Type>();
        }
    }

    // ══ G-B1 — ResetAllAsync 첫 문장 거절 ══

    [Fact(DisplayName = "G-B1 🔴 운영 구현(닫힘) ResetAllAsync — Success=false · DenyCode=closed · 닫힘 안내 · DB 0 · 백업 0 · 감사 data_reset_denied 1(베타 판 닫힘: closed)")]
    public async Task GB1_첫문장_거절()
    {
        var (svc, backupCalls, audit) = ClosedService();

        var res = await svc.ResetAllAsync(new DataResetRequest { Password = "gb1", ConfirmText = "초기화" }, "gb1-tenant", "gb1-user");

        Assert.False(res.Success, "🔴 닫힌 판인데 초기화가 성공했다.");
        Assert.Equal(DataResetGateCodes.Closed, res.DenyCode);
        Assert.Equal(DataResetService.ClosedMessage, res.Error);
        Assert.Null(res.BackupId);
        Assert.True(backupCalls() == 0, $"🔴 닫힌 판인데 백업을 {backupCalls()}번 불렀다(닫힘은 강제 백업보다 앞이어야 한다).");
        var row = Assert.Single(audit.Rows);
        Assert.Equal("data_reset_denied", row.Action);
        Assert.Equal(DataResetService.ClosedDenyReasonPrefix + DataResetGateCodes.Closed, row.Reason);
    }

    // ══ G-B2 — 판정 함수 2곳도 DB 앞에서 Closed ══

    [Fact(DisplayName = "G-B2 🔴 운영 구현 GetResetGateAsync · GetResetGateInTransactionAsync → Closed · DB 0 / 대조: 열린 가짜면 DB 로 간다(던짐)")]
    public async Task GB2_판정_닫힘()
    {
        var (svc, backupCalls, _) = ClosedService();

        Assert.Equal(DataResetGate.Closed, await svc.GetResetGateAsync("gb2-tenant", "gb2-user"));
        // 셋째 겹 — 트랜잭션도 연결도 만지지 않는다(null 을 넘겨도 닫힘에서 끝난다).
        Assert.Equal(DataResetGate.Closed, await svc.GetResetGateInTransactionAsync(null!, "gb2-tenant", "gb2-user"));
        Assert.Equal(0, backupCalls());

        // 대조 — 같은 손대면 던지는 DB 에 열린 가짜를 주면 판정이 DB 로 간다(위 Closed 가 깃발에서 왔다는 증거).
        var open = new DataResetService(ThrowingDb(), null!, new CollectingAudit(), NullLogger<DataResetService>.Instance,
            DataResetAvailabilityForTest.OpenForTest);
        await Assert.ThrowsAsync<DbTouchedException>(() => open.GetResetGateAsync("gb2-tenant", "gb2-user"));
        await Assert.ThrowsAsync<DbTouchedException>(() => open.ResetAllAsync(new DataResetRequest(), "gb2-tenant", "gb2-user"));
    }

    // ══ G-B3 — 계약 글자 · [반증 S-2] 두 칸 ══

    [Fact(DisplayName = "G-B3 🔴 계약 — 서버 Closed == 화면 Closed == \"closed\" · ShowResetLink(closed)=거짓 · 운영 IsOpen=거짓 · ⓐ ToCode(Closed)=closed · ⓑ GateMessage(Closed)=닫힘 안내(기본 칸 「대표 계정이 아닙니다」 아님)")]
    public void GB3_계약()
    {
        Assert.Equal("closed", DataResetGateCodes.Closed);
        Assert.Equal(DataResetGateCodes.Closed, DataResetMenuRule.Closed);
        Assert.False(DataResetMenuRule.ShowResetLink(DataResetMenuRule.Closed), "🔴 「closed」 인데 사이드바 메뉴가 보인다.");
        Assert.False(new DataResetBetaAvailability().IsOpen, "🔴 운영 구현이 열려 있다 — 베타 판 서버 닫음이 안 됐다(되돌림은 대표 확인 뒤에만).");

        // ⓐ ToCode 기본 칸(_ => NotParent)으로 떨어지면 my-level 이 not_parent 를 줘 「닫힘」 이 사라진다.
        Assert.Equal(DataResetGateCodes.Closed, DataResetGateCodes.ToCode(DataResetGate.Closed));

        // ⓑ GateMessage 기본 칸(_ => NotParentMessage)으로 떨어지면 대표에게 「대표 계정이 아닙니다」 가 뜬다.
        var gm = typeof(DataResetService).GetMethod("GateMessage", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(gm);
        var msg = gm!.Invoke(null, new object[] { DataResetGate.Closed }) as string;
        Assert.Equal(DataResetService.ClosedMessage, msg);
        Assert.NotEqual(DataResetService.NotParentMessage, msg);
    }

    // ══ G-B4 — 배선 · 셋째 길 없음(글자 · 한계 기록) ══

    [Fact(DisplayName = "G-B4 🔴 Program.cs(API) 운영 구현 등록 1줄(싱글턴) · 운영 구현체 1개 · ResetAllAsync 운영 호출자 = DataResetController · MigrationController 2곳뿐(셋째 길이 생기면 빨강)")]
    public void GB4_배선_호출자()
    {
        var root = RepoRoot();
        var program = File.ReadAllLines(Path.Join(root, "src", "HitPan.API", "Program.cs"));
        var reg = program.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                                  && l.Contains("IDataResetAvailability", StringComparison.Ordinal)).ToList();
        var line = Assert.Single(reg);
        Assert.Contains("AddSingleton<IDataResetAvailability, DataResetBetaAvailability>()", line, StringComparison.Ordinal);

        // 운영 어셈블리(Application · Infrastructure · API) 안 구현체 = DataResetBetaAvailability 하나.
        var impls = new[] { typeof(DataResetService).Assembly, typeof(HitPan.API.Controllers.DataResetController).Assembly,
                            typeof(HitPan.Infrastructure.Security.HashService).Assembly }
            .Distinct()
            .SelectMany(LoadableTypes)
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDataResetAvailability).IsAssignableFrom(t))
            .Select(t => t.Name).ToList();
        Assert.Equal(new[] { nameof(DataResetBetaAvailability) }, impls);

        // ResetAllAsync 를 부르는 운영 파일(Web · Tests · bin · obj 밖) — 정의 2파일을 빼면 정확히 둘.
        var src = Path.Join(root, "src");
        var callers = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(p =>
            {
                var rel = Path.GetRelativePath(src, p).Replace('\\', '/');
                return !rel.StartsWith("HitPan.Tests/", StringComparison.Ordinal)
                    && !rel.StartsWith("HitPan.Web/", StringComparison.Ordinal)
                    && !rel.Contains("/bin/", StringComparison.Ordinal)
                    && !rel.Contains("/obj/", StringComparison.Ordinal);
            })
            .Where(p => File.ReadAllText(p).Contains(".ResetAllAsync(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "DataResetController.cs", "MigrationController.cs" }, callers);
    }
}
