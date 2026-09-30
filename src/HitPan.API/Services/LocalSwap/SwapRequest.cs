using System.Text.Json;
using System.Text.Json.Serialization;

namespace HitPan.API.Services.LocalSwap;

/// <summary>
/// 교체 일꾼 요청서 <c>request.json</c> schema 1 — 계약 문서
/// <c>docs/설계/erp/20260930_계약_local-swap_request.md</c> §3 과 칸 하나하나가 같다.
/// 20260930작1 갈래 A 소유. 칸을 바꾸면 <see cref="CurrentSchema"/> 를 올린다(일꾼은 모르는 번호를 거부한다).
/// </summary>
/// <remarks>
/// 🔴 파일은 <b>ASCII 로만</b> 쓴다 — System.Text.Json 기본 인코더가 비 ASCII 를 <c>\uXXXX</c> 로 적는다.
/// 일꾼(PowerShell 5.1)이 한글 설치 경로도 깨지지 않게 읽는다.
/// </remarks>
public sealed class SwapRequest
{
    public const int CurrentSchema = 1;

    [JsonPropertyName("schema")] public int Schema { get; set; } = CurrentSchema;
    [JsonPropertyName("ticket")] public string Ticket { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = SwapStates.Requested;
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonPropertyName("material")] public SwapMaterial Material { get; set; } = new();
    [JsonPropertyName("app_root")] public string AppRoot { get; set; } = "";
    [JsonPropertyName("slot")] public int Slot { get; set; }
    [JsonPropertyName("api_port")] public int ApiPort { get; set; }
    [JsonPropertyName("requested_by")] public string RequestedBy { get; set; } = "";
    [JsonPropertyName("requested_at")] public DateTime RequestedAt { get; set; }
    [JsonPropertyName("entry")] public string Entry { get; set; } = SwapEntries.Menu;
    [JsonPropertyName("auto_state")] public string? AutoState { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
    [JsonPropertyName("step")] public string? Step { get; set; }
    [JsonPropertyName("log")] public string? Log { get; set; }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>요청서를 ASCII JSON 으로 만든다.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    /// <summary>요청서를 읽는다. 모양이 틀리면 null(호출부가 기록한다).</summary>
    public static SwapRequest? FromJson(string json) => JsonSerializer.Deserialize<SwapRequest>(json);

    /// <summary>마지막으로 상태가 바뀐 시각(일꾼이 안 건드렸으면 요청 시각).</summary>
    [JsonIgnore] public DateTime LastTouchedUtc => (UpdatedAt ?? RequestedAt).ToUniversalTime();
}

/// <summary>교체 재료. <c>kind</c> 별 허용 뿌리는 계약 §2(일꾼이 다시 잰다).</summary>
public sealed class SwapMaterial
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
}

/// <summary>모드 — 두 수동 동작이 한 틀(같은 일꾼·같은 작업 이름)을 쓴다(설계 §13-2).</summary>
public static class SwapModes
{
    public const string Update = "update";
    public const string Rollback = "rollback";
}

/// <summary>재료 종류.</summary>
public static class SwapMaterialKinds
{
    /// <summary><c>{app}\rollback\prev</c> — 수동 업데이트가 남긴 바로 앞 한 세대.</summary>
    public const string Prev = "prev";
    /// <summary>워치독이 받아 둔 <c>staging\hitpan-{V}.zip</c>(읽기만).</summary>
    public const string StagingZip = "staging_zip";
    /// <summary>수동 업데이트가 받은 zip(<c>{app}\manual\staging</c>).</summary>
    public const string ManualZip = "manual_zip";
}

/// <summary>입구 — 수동 사용 기록(설계 §13-5).</summary>
public static class SwapEntries
{
    public const string Menu = "menu";
    public const string Login = "login";
}

/// <summary>상태 — 계약 §5.</summary>
public static class SwapStates
{
    public const string Requested = "requested";
    public const string Running = "running";
    public const string Success = "success";
    public const string Refused = "refused";
    public const string Reverted = "reverted";
    public const string Broken = "broken";

    /// <summary>일꾼이 아직 끝내지 않은 상태인가.</summary>
    public static bool IsOpen(string? state) => state is Requested or Running;
}

/// <summary>사유 코드 — 계약 §6 표. 화면 문구는 갈래 F 가 이 코드로 고른다.</summary>
public static class SwapReasons
{
    public const string Ok = "ok";
    public const string NotWindows = "not_windows";
    public const string NoPreviousVersion = "no_previous_version";
    public const string RollbackChainBlocked = "rollback_chain_blocked";
    public const string UpdateInProgress = "update_in_progress";
    public const string SwapInProgress = "swap_in_progress";
    public const string Cooldown = "cooldown";
    public const string DiskLow = "disk_low";
    public const string TicketInvalid = "ticket_invalid";
    public const string ScriptMissing = "script_missing";
    public const string FolderUnsafe = "folder_unsafe";
    public const string TaskRegisterFailed = "task_register_failed";
    public const string RequestInvalid = "request_invalid";
    // 일꾼(스크립트)만 쓰는 코드 — 화면이 끝 상태를 읽을 때 만난다
    public const string MaterialInvalid = "material_invalid";
    public const string HashMismatch = "hash_mismatch";
    public const string SafetyNetFailed = "safety_net_failed";
    public const string StopFailed = "stop_failed";
    public const string SwapFailed = "swap_failed";
    public const string VerifyFailed = "verify_failed";
    public const string RevertFailed = "revert_failed";
}

/// <summary>런처에 넘기는 값 — 판·재료는 <b>서버가 계산한 값</b>만(요청 본문 유래 0 · 병렬이슈 04).</summary>
public sealed record SwapLaunchInput(
    string Mode,
    string From,
    string To,
    SwapMaterial Material,
    string RequestedBy,
    string Entry,
    string? AutoState,
    string? Ticket);

/// <summary>런처 결과. <see cref="Started"/> 가 true 면 1회용 작업이 돌기 시작했다(202).</summary>
public sealed record SwapLaunchResult(bool Started, string Reason, string? Ticket)
{
    public static SwapLaunchResult Refuse(string reason) => new(false, reason, null);
}
