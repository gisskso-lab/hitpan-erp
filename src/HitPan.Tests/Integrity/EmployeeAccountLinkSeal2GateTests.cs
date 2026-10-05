using System.Text.Json;
using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 10/5 작5 서버 보정(작업지시서 §8-7) 게이트의 <b>DB 불필요 몫</b>. DB 몫은 <c>EmployeeAccountLinkGateDbTests.Seal2</c>.
/// <list type="bullet">
/// <item><b>G-E4s</b>(P2-04) — 실물 <c>CreateForEmployeeAsync</c> 원문에 장치 셋(좌석 잠금 첫 문장 · 사원 행 FOR UPDATE 가 INSERT 앞 · 조건부 연결 UPDATE + 0행 거절)이
///   순서대로 있다. 같은 판정기를 장치 하나씩 뺀 사본에 돌려 잡는지 함께 확인(대조군).
///   이 게이트는 <b>로컬에서 도는 몫</b>이다 — 동작 게이트 G-E4b·G-E4c 는 DB 가 있어야 돌고(개발 PC SKIP),
///   조건부 UPDATE 는 앞의 두 잠금이 있는 한 동작으로 떼어 잴 수 없다(잠금이 먼저 줄 세운다 · 개발명세서 §7).</item>
/// <item><b>G-E20</b>(P3-17) — 계정 생성 감사기록 JSON 이 이름 칸의 따옴표로 모양이 바뀌지 않는다.</item>
/// </list>
/// </summary>
public sealed class EmployeeAccountLinkSeal2GateTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HitPan.sln"))) return dir.Parent!.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다.");
    }

    private static string UserServiceSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "HitPan.Application", "Services", "UserService.cs")).Replace("\r\n", "\n");

    /// <summary>
    /// <c>CreateForEmployeeAsync</c> 본문(주석 줄 제외)에서 장치 셋을 찾는다. 빠진 장치마다 사유 한 줄. 빈 목록 = 통과.
    /// </summary>
    internal static List<string> CheckCreateForEmployeeDevices(string source)
    {
        var problems = new List<string>();
        var start = source.IndexOf("public async Task<string> CreateForEmployeeAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("ListLinkableEmployeesAsync(", start < 0 ? 0 : start, StringComparison.Ordinal);
        if (start < 0 || end < 0) { problems.Add("함수 본문을 못 찾음"); return problems; }

        var body = string.Join("\n", source[start..end].Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        int At(string s) => body.IndexOf(s, StringComparison.Ordinal);
        var begin = At("_db.BeginTransaction()");
        var acquire = At("AccountSeatGuard.AcquireAsync(");
        var ensure = At("AccountSeatGuard.EnsureSeatAsync(");
        var forUpdate = At("FOR UPDATE");
        var insertUsers = At("INSERT INTO users");
        var guarded = At("AND (user_id IS NULL OR user_id = '' OR user_id = @DeadId)");
        var zeroRefused = At("if (linked == 0)");

        if (begin < 0) problems.Add("트랜잭션 없음");
        if (acquire < 0 || ensure < 0) problems.Add("좌석 장치 없음(AcquireAsync·EnsureSeatAsync)");
        else if (begin >= 0 && (acquire < begin
                 || body[(begin + "_db.BeginTransaction()".Length)..acquire].Contains("_db.", StringComparison.Ordinal)))
            problems.Add("좌석 잠금이 트랜잭션 첫 문장이 아님");
        if (forUpdate < 0) problems.Add("사원 행 FOR UPDATE 없음");
        else if (insertUsers >= 0 && forUpdate > insertUsers) problems.Add("사원 행 FOR UPDATE 가 users INSERT 뒤");
        else if (ensure >= 0 && forUpdate < ensure) problems.Add("사원 행 잠금이 좌석 잠금 앞(잠금 순서 P3-10 위반)");
        if (guarded < 0 || zeroRefused < 0) problems.Add("조건부 연결 UPDATE 또는 0행 거절 없음");
        return problems;
    }

    [Fact(DisplayName = "G-E4s 🔴 P2-04 구조 — 실물 CreateForEmployeeAsync 에 좌석 잠금(첫 문장)·사원 행 FOR UPDATE(INSERT 앞·좌석 뒤)·조건부 연결 UPDATE+0행 거절 · 대조군(장치 하나씩 뺀 사본 셋을 같은 판정기가 각각 잡는다)")]
    public void E4s_CreateForEmployee_Devices_In_Order()
    {
        var src = UserServiceSource();
        var real = CheckCreateForEmployeeDevices(src);
        Assert.True(real.Count == 0, "P2-04 — 장치 빠짐: " + string.Join(" · ", real));

        // 🔴 대조군 — 사본에서 장치 하나씩 빼면 같은 판정기가 잡는다
        static string DropLines(string s, string needle) =>
            string.Join("\n", s.Split('\n').Where(l => !l.Contains(needle, StringComparison.Ordinal)));
        var startIdx = src.IndexOf("public async Task<string> CreateForEmployeeAsync(", StringComparison.Ordinal);
        var head = src[..startIdx];
        var tail = src[startIdx..];

        var noSeat = head + DropLines(DropLines(tail, "AccountSeatGuard.AcquireAsync("), "AccountSeatGuard.EnsureSeatAsync(");
        Assert.Contains(CheckCreateForEmployeeDevices(noSeat), p => p.StartsWith("좌석 장치 없음", StringComparison.Ordinal));

        var firstForUpdate = tail.IndexOf("FOR UPDATE", StringComparison.Ordinal);
        var noRowLock = head + tail.Remove(firstForUpdate, "FOR UPDATE".Length);
        Assert.Contains(CheckCreateForEmployeeDevices(noRowLock), p => p.StartsWith("사원 행 FOR UPDATE 없음", StringComparison.Ordinal));

        var noGuard = head + DropLines(tail, "AND (user_id IS NULL OR user_id = '' OR user_id = @DeadId)");
        Assert.Contains(CheckCreateForEmployeeDevices(noGuard), p => p.StartsWith("조건부 연결 UPDATE", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "G-E20 🔴 P3-17 — 계정 생성 감사기록 JSON: 이름에 따옴표·가짜 키(x\",\"role\":\"TenantAdmin)를 넣어도 키 5개 · role 은 실제 값 하나 · 이름은 원문 그대로 · 한글은 escape 없이 · 대조군(옛 문자열 붙이기는 role 키가 둘)")]
    public void E20_Audit_Json_Not_Forgeable()
    {
        const string evil = "x\",\"role\":\"TenantAdmin";
        var json = UserService.AuditCreateJson("a2001", evil, "User", "tenant_user", "E-1");
        using (var doc = JsonDocument.Parse(json))
        {
            var props = doc.RootElement.EnumerateObject().ToList();
            Assert.Equal(new[] { "email", "user_name", "role", "account_type", "employee_id" }, props.Select(p => p.Name).ToArray());
            Assert.Equal("User", doc.RootElement.GetProperty("role").GetString());
            Assert.Equal(evil, doc.RootElement.GetProperty("user_name").GetString());
        }

        // 대표 경로(사원 연결 없음) — 키 4개(옛 모양 그대로)
        var plain = UserService.AuditCreateJson("a2002", "홍길동", "TenantAdmin", "tenant_admin", null);
        using (var doc = JsonDocument.Parse(plain))
            Assert.Equal(new[] { "email", "user_name", "role", "account_type" }, doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Contains("홍길동", plain, StringComparison.Ordinal);

        // 🔴 대조군 — P3-17 전 문자열 붙이기(UserService ⬛ 줄 그대로)는 같은 이름으로 role 키가 둘이 된다(기록 모양 변조)
        var (loginId, userName, roleStr, accountType, employeeId) = ("a2001", evil, "User", "tenant_user", "E-1");
        var old = $"{{\"email\":\"{loginId}\",\"user_name\":\"{userName}\",\"role\":\"{roleStr}\",\"account_type\":\"{accountType}\",\"employee_id\":\"{employeeId}\"}}";
        using var oldDoc = JsonDocument.Parse(old);
        Assert.Equal(2, oldDoc.RootElement.EnumerateObject().Count(p => p.Name == "role"));
        Assert.Contains(oldDoc.RootElement.EnumerateObject(), p => p.Name == "role" && p.Value.GetString() == "TenantAdmin");
    }
}
