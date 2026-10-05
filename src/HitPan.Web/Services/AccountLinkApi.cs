using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HitPan.Web.Models;
using Microsoft.Extensions.Logging;

namespace HitPan.Web.Services;

/// <summary>계정을 만들 수 있는 사원 한 명(GET api/users/linkable-employees · 409 same_name_employee 후보). 서버 LinkableEmployeeDto 와 짝(#12).</summary>
public sealed class LinkableEmployeeModel
{
    public string EmployeeId { get; set; } = "";
    public string EmpNo { get; set; } = "";
    public string EmpName { get; set; } = "";
    public string? DeptName { get; set; }
    public string? Position { get; set; }
    /// <summary>사원 직무(employees.role 원값). 권한 판정(<see cref="AccountLinkLabels.IsGeneralEmployeeRole"/>)에만 쓴다 — 화면에 직무 이름으로 보이지 않는다(작5 §8-9).</summary>
    public string? Role { get; set; }
}

/// <summary>내 「직원 계정 관리」 단계(GET api/users/my-level). 0 없음 · 1 조회 · 2 계정설정 · 3 구독계정추가.</summary>
public sealed class UsersLevelModel
{
    public int Level { get; set; }
    public bool IsAdmin { get; set; }
}

/// <summary>[구독계정추가] 창이 읽는 숫자(GET api/users/seat-subscription · 3단계 전용).</summary>
public sealed class SeatSubscriptionModel
{
    public int Active { get; set; }
    public int BaseLimit { get; set; }
    public int Extra { get; set; }
    public int Limit { get; set; }
    public string Tier { get; set; } = "basic";
    public decimal ExtraAccountMonthlyWon { get; set; }
}

/// <summary>기존 사원에게 계정 만들기 요청(POST api/users/for-employee). 역할은 보내지 않는다 — 출입증 역할은 사원 직무가 정한다(R-3).</summary>
public sealed class CreateForEmployeeModel
{
    public string EmployeeId { get; set; } = "";
    public string LoginId { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>비우면 사원 이름.</summary>
    public string? UserName { get; set; }
}

/// <summary>호출 결과. <paramref name="Code"/> 는 서버 응답의 code(화면 분기용 · 고객에게 보이지 않는다).</summary>
public sealed record AccountLinkCallResult(bool Ok, bool SeatFull, string? Code, string? Error, IReadOnlyList<LinkableEmployeeModel> Candidates)
{
    public bool SameNameEmployee => string.Equals(Code, AccountLinkApi.SameNameCode, StringComparison.Ordinal);
}

/// <summary>화면 표기 — 계정 상태 · 사원 직무. 한 곳에서만 정한다(사원관리·직원계정 같은 말).</summary>
public static class AccountLinkLabels
{
    /// <summary>서버 accountStatus → 화면 글자. 빈 값(호출자에게 숨김)은 빈 문자열.</summary>
    public static string StatusLabel(string? status) => status switch
    {
        "owner" => "대표",
        "active" => "사용중",
        "suspended" => "사용중지",
        "none" => "미등록",
        _ => ""
    };

    // ⬛ [작5 §8-9 사장님 결재 10/5] RoleLabel(직무 코드 → 「관리자」·「매니저」·「인사 담당」·「기타 직무」 …)은 화면에서 걷었다.
    //    직무 이름은 히트판이 정하지 않는다(고객 권한 · 헌법 #11 과 같은 결). 계정 만들기 · 기존 사원 고르기 · 같은 이름 질문은
    //    고객이 입력한 부서·직급(DeptPositionLine)만 보여 준다. 권한 판정(IsGeneralEmployeeRole · 서버 409)은 그대로.
    //    옛 본문(작5 §8-5 신설 · §8-8 V5-17 확장)은 커밋 bbe9e48c 의 이 파일에 있다 — 쓰는 곳이 0 이 되어 걷었다.

    /// <summary>
    /// 고객이 사원관리에서 직접 입력한 부서·직급 한 줄(작5 §8-9). 「부서 영업부 · 직급 대리」 — 없는 칸은 비운다(둘 다 없으면 빈 문자열).
    /// 히트판이 지은 이름은 섞지 않는다.
    /// </summary>
    public static string DeptPositionLine(string? deptName, string? position)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(deptName)) parts.Add($"부서 {deptName.Trim()}");
        if (!string.IsNullOrWhiteSpace(position)) parts.Add($"직급 {position.Trim()}");
        return string.Join(" · ", parts);
    }

    /// <summary>「직원 계정 관리」 단계 0~3 → 화면 글자(권한설정 4선택과 같은 말 · 작5 §8-5 U-2).</summary>
    public static string UsersLevelLabel(int level) => level switch
    {
        3 => "구독계정추가",
        2 => "계정설정",
        1 => "조회",
        _ => "없음"
    };

    /// <summary>
    /// 일반(직원) 직무인가 — 서버 <c>UserService.IsGeneralEmployeeRole</c>(P1-01)과 <b>같은 규칙</b>(작5 §8-5 U-3).
    /// 2단계 직원(대표·관리자 아님)은 일반 직무 사원에게만 계정을 만들 수 있다. 화면은 미리 막는 편의 · 서버 409 가 정본.
    /// </summary>
    /// <remarks>Web 은 Domain 을 참조하지 않으므로 <c>UserRole</c> 값을 <see cref="ServerUserRole"/> 로 옮겨 적었다(1·2·3·4 — 바뀌면 함께).
    /// 빈 값 · User · Readonly(숫자 3·4 포함) · <c>*_user</c>(admin 글자 없는 것)만 일반.</remarks>
    public static bool IsGeneralEmployeeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return true;
        var r = role.Trim();
        if (int.TryParse(r, out var n))
            return n == (int)ServerUserRole.User || n == (int)ServerUserRole.Readonly;
        if (Enum.TryParse<ServerUserRole>(r, ignoreCase: true, out var parsed))
            return parsed is ServerUserRole.User or ServerUserRole.Readonly;
        var lower = r.ToLowerInvariant();
        return lower.EndsWith("_user", StringComparison.Ordinal) && !lower.Contains("admin", StringComparison.Ordinal);
    }

    /// <summary>서버 <c>HitPan.Domain.Enums.UserRole</c> 값 그대로(판정 짝 맞춤용 · 화면 표시에 쓰지 않는다).</summary>
    private enum ServerUserRole
    {
        TenantAdmin = 1,
        Manager = 2,
        User = 3,
        Readonly = 4
    }
}

/// <summary>
/// 20261005작5 §8-8 — 임시 비밀번호 한 곳. [직원 계정 관리](Users.razor 두 칸)와 사원관리 [계정 만들기]가 같은 함수를 쓴다.
/// </summary>
/// <remarks>⬛ 종전: 두 화면이 각자 <c>new Random()</c> 으로 뽑았다(CodeQL cs/insecure-randomness 3건) — 문자표·12자는 그대로,
/// 뽑는 방법만 <see cref="System.Security.Cryptography.RandomNumberGenerator.GetInt32(int)"/> 로 바꿨다.</remarks>
public static class AccountTempPassword
{
    /// <summary>헷갈리는 글자(I·l·O·0·1)를 뺀 문자표 — 종전 두 화면과 같은 글자.</summary>
    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$";

    /// <summary>임시 비밀번호 길이 — 종전 그대로 12자.</summary>
    public const int Length = 12;

    /// <summary>임시 비밀번호 한 개.</summary>
    public static string New()
    {
        var buf = new char[Length];
        for (var i = 0; i < buf.Length; i++)
            buf[i] = Chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Chars.Length)];
        return new string(buf);
    }
}

/// <summary>
/// 20261005작5 — 사원 ↔ 계정 연결 API 호출(설계 §3·§4·§5). <see cref="AccountSeatApi"/> 와 같은 모양:
/// DI 등록 없이 화면이 주입받은 HttpClient 로 만든다(Web Program.cs 무접촉).
/// </summary>
/// <remarks>판정은 서버가 정본이다. 이 단계 값은 버튼을 숨기는 편의일 뿐 — 서버가 [RequireUsersLevel] 로 다시 막는다.</remarks>
public sealed class AccountLinkApi(HttpClient http, ILogger logger)
{
    internal const string SameNameCode = "same_name_employee";
    private const string SeatFullCode = "account_seat_full";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>내 단계. 못 읽으면 null(화면은 0단계처럼 — 열어 주지 않는다).</summary>
    public async Task<UsersLevelModel?> GetMyLevelAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<UsersLevelModel>("api/users/my-level", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "직원 계정 관리 단계 조회 실패");
            return null;
        }
    }

    /// <summary>계정을 만들 수 있는 사원(재직 · 계정 없음). 못 읽으면 null — 「0명」과 다르다.</summary>
    public async Task<List<LinkableEmployeeModel>?> GetLinkableEmployeesAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<List<LinkableEmployeeModel>>("api/users/linkable-employees", Json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "계정 만들 사원 목록 조회 실패");
            return null;
        }
    }

    /// <summary>[구독계정추가] 창 숫자(3단계 전용 주소). 못 읽으면 null.</summary>
    public async Task<SeatSubscriptionModel?> GetSeatSubscriptionAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<SeatSubscriptionModel>("api/users/seat-subscription", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "구독계정추가 조회 실패");
            return null;
        }
    }

    /// <summary>기존 사원에게 계정 만들기(사원관리 [계정 만들기] · 직원계정 「기존 사원에서 고르기」 같은 주소).</summary>
    public Task<AccountLinkCallResult> CreateForEmployeeAsync(CreateForEmployeeModel model, CancellationToken ct = default) =>
        SendAsync(() => http.PostAsJsonAsync("api/users/for-employee", model, ct), ct);

    /// <summary>새 사원과 함께 만들기(대표·관리자만). 같은 이름 사원이 있으면 SameNameEmployee + 후보.</summary>
    public Task<AccountLinkCallResult> CreateWithNewEmployeeAsync(CreateUserModel model, CancellationToken ct = default) =>
        SendAsync(() => http.PostAsJsonAsync("api/users", model, ct), ct);

    private async Task<AccountLinkCallResult> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            using var res = await send().ConfigureAwait(false);
            if (res.IsSuccessStatusCode) return new(true, false, null, null, Array.Empty<LinkableEmployeeModel>());
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var (code, message, candidates) = ReadBody(body);
            var seatFull = res.StatusCode == HttpStatusCode.Conflict && string.Equals(code, SeatFullCode, StringComparison.Ordinal);
            if (res.StatusCode == HttpStatusCode.Forbidden && string.IsNullOrWhiteSpace(message))
                message = "이 기능을 쓸 권한이 없습니다. 대표님께 권한을 요청하세요.";
            return new(false, seatFull, code, message ?? "처리하지 못했습니다.", candidates);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "계정 만들기 호출 실패");
            return new(false, false, null, "연결이 고르지 않아 처리하지 못했습니다. 잠시 후 다시 해 주세요.", Array.Empty<LinkableEmployeeModel>());
        }
    }

    /// <summary>서버 JSON 에서 code·message·candidates 만 꺼낸다(개발 용어·코드는 화면에 안 낸다).</summary>
    private (string? Code, string? Message, IReadOnlyList<LinkableEmployeeModel> Candidates) ReadBody(string body)
    {
        IReadOnlyList<LinkableEmployeeModel> none = Array.Empty<LinkableEmployeeModel>();
        if (string.IsNullOrWhiteSpace(body)) return (null, null, none);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null, none);
            string? code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()
                         : root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            string? message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            var candidates = root.TryGetProperty("candidates", out var list) && list.ValueKind == JsonValueKind.Array
                ? (IReadOnlyList<LinkableEmployeeModel>)(list.Deserialize<List<LinkableEmployeeModel>>(Json) ?? new())
                : none;
            return (code, message, candidates);
        }
        catch (JsonException ex)
        {
            // 서버가 JSON 이 아닌 글자를 줬다 — 흔적만 남기고 일반 문구로(#15)
            logger.LogWarning(ex, "계정 만들기 응답 해석 실패");
            return (null, null, none);
        }
    }
}
