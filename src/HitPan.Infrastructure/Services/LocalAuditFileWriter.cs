using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Infrastructure.Services;

/// <summary>
/// 🔴 W-G1-52 갈래 0 — <see cref="ILocalAuditFile"/> 의 유일한 구현체. 복원 대상 DB 밖 로컬 파일에 줄을 더하기만 한다.
/// </summary>
/// <remarks>
/// 🔴 <b>자리</b> = <c>%ProgramData%\HitPan\audit\restore-audit.log</c> — 앱이 회사 그림 · 양식지 · 기본 백업을 두는
///   같은 뿌리(<c>CompanyImagePaths</c> · <c>BackupService</c> 의 %ProgramData%\HitPan)다. 고객 PC 안 · 사용자 계정과 무관 ·
///   DB 데이터 폴더 밖이라 복원(표 지우고 붓기)이 이 파일을 건드리지 못한다.
///   🆕 고침 1(X1) — <c>audit</c> 폴더는 <see cref="LocalAuditFolderGuard"/> 가 보호 권한(SYSTEM · Administrators · 실행 계정)으로 만들고,
///   재분석 지점 · 남의 소유 · 넓은 그룹 권한이면 쓰기 전에 거절한다(파일 자신도).
///
/// 🔴 <b>줄 꼴</b>(탭 구분 · UTF-8 · BOM 없음 · 끝 <c>\n</c>):
///   <c>v1 \t 순번 \t 시각(UTC · 7자리 소수 · Z) \t 회사 \t 대표 user_id \t 동작 \t 결과 \t 묶음 id \t 덧말 \t 앞 줄 지문 \t 이 줄 지문</c>
///   이 줄 지문 = SHA-256(마지막 탭 앞 글자 전부의 UTF-8) 소문자 16진. 첫 줄의 앞 줄 지문 = 0 64개.
///
/// 🔴 <b>동시 쓰기</b> — 2겹: ① 같은 경로는 프로세스 안 한 줄 서기(경로별 SemaphoreSlim · 인스턴스가 여럿이어도 같음)
///   ② 파일은 <c>FileShare.Read</c> 로 열어 다른 프로세스의 쓰기를 막는다(읽기는 허용 · 막히면 잠깐 기다렸다 다시).
///   🆕 고침 1(X8) — 두 겹을 <b>하나씩</b> 빼도 시험이 잡는다(같은 프로세스 두 기록기 · 남의 쓰기 손잡이).
///
/// 🔴 <b>전원 끊김</b> — 줄 끝마다 <c>Flush(true)</c>(디스크까지) + <c>WriteThrough</c>. 마지막 줄이 줄바꿈 없이 깨져 있으면
///   다음 쓰기가 그 조각을 줄바꿈으로 닫고 <see cref="LocalAuditAction.TornTailNoted"/> 줄(조각 지문)을 남긴 뒤 이어 쓴다.
///   사슬 검사는 「깨진 조각 바로 뒤에 그 조각 지문을 적은 표시 줄」 일 때만 통과시킨다.
///   🆕 고침 1(X5 · F7 · F10) — 「끊긴 조각」 = <b>기록기가 쓰다 끊긴 꼴</b>(<c>v1</c> 로 시작 · 맞는 순번 · 앞 줄 지문의 앞부분 · 칸 글자 규칙 ·
///   끝에 0 바이트가 붙은 것 포함)만. 끝에 조각이 여럿이면(전원이 두 번 이상 끊김) 조각마다 차례로 표시 줄을 남긴다.
///   그 꼴이 아닌 쓰레기 줄은 줄바꿈이 붙든 말든 <c>tail_garbage</c>(빨강) — 다음 쓰기가 표시 줄로 덮어 주지 않는다.
///
/// 🔴 <b>네트워크 의존성 0</b>(#18 · #22 · 설계 §12 G11) — 이 형식은 파일 · 해시 · 로그 · 권한 형식만 쓴다. 시험이 IL 까지(부르는 HitPan 형식까지 전이로) 훑어 잰다.
///
/// ⚠️ 쓸 때마다 파일 전체를 읽는다 — 복원 · 비번 되찾기는 드문 일이라 파일이 작다(한 해 수십 줄 안팎 · 추정). 단순함을 고른 값.
/// ⚠️ 읽기(<see cref="VerifyChainAsync"/> · 미완 읽기)는 폴더 문지기를 거치지 않는다 — 폴더 보호 권한이 심기 · 바꿔치기를 막는다(X1).
/// </remarks>
public sealed class LocalAuditFileWriter : ILocalAuditFile
{
    private const string Version = "v1";
    private const int FieldCount = 11;
    private const int MaxIdLength = 64;
    private const int MaxDetailLength = 200;
    private const int ShareRetryDelayMs = 50;
    private const string TornPrefix = "torn-sha256:";
    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
    private static readonly string ZeroHash = new('0', 64);
    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // 🆕 고침 1(3-V F6) — 덧말 비밀 「모양」 거절. 다 막지는 못한다(계약 주석 · 호출자 책임).
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex MixedCaseLongRun = new("[A-Za-z0-9+=_-]{33,}", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ResidentNumberShape = new(@"(?<![0-9])[0-9]{2}(0[1-9]|1[0-2])(0[1-9]|[12][0-9]|3[01])-?[1-8][0-9]{6}(?![0-9])", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex MobilePhoneShape = new(@"(?<![0-9])01[016789]-?[0-9]{3,4}-?[0-9]{4}(?![0-9])", RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>경로별 한 줄 서기 — 같은 파일을 가리키는 인스턴스가 여럿이어도 한 자물쇠를 쓴다.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<LocalAuditFileWriter> _logger;
    private readonly SemaphoreSlim _gate;

    /// <summary>기본 자리 = <see cref="DefaultFilePath"/>. DI 는 이 생성자를 쓴다(Program.cs 에서 못박음).</summary>
    public LocalAuditFileWriter(ILogger<LocalAuditFileWriter> logger)
        : this(DefaultFilePath, logger)
    {
    }

    /// <summary>자리를 정해 만든다(시험용 · 절대 경로).</summary>
    public LocalAuditFileWriter(string filePath, ILogger<LocalAuditFileWriter> logger)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("감사 파일 경로가 비었습니다.", nameof(filePath));
        FilePath = Path.GetFullPath(filePath);
        _logger = logger;
        _gate = PathLocks.GetOrAdd(FilePath, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>기본 감사 파일 자리 — <c>%ProgramData%\HitPan\audit\restore-audit.log</c>.</summary>
    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HitPan", "audit", "restore-audit.log");

    /// <inheritdoc />
    public string FilePath { get; }

    /// <summary>
    /// 🆕 고침 1 — 공유 위반 때 다시 여는 횟수(× 50ms). ⬛ 고침 1 전 = 상수 <c>ShareRetryCount = 100</c>(약 5초) — 기본값 그대로.
    /// 시험이 「남의 쓰기 손잡이」 를 빨리 재려고 줄인다(internal · 시험 어셈블리만).
    /// </summary>
    internal int ShareRetryCount { get; set; } = 100;

    /// <summary>
    /// 🆕 고침 1(F8 시험) — 손잡이를 쥔 채 바이트를 쓰고 디스크까지 내려 쓰는 걸음. 기본 = <see cref="WriteAndFlush"/>.
    /// 시험이 「쓰기 뒤 던짐」 · 「쥔 채 기다림」 을 재려고 바꾼다(internal · 시험 어셈블리만 · 제품 코드는 바꾸지 않음).
    /// </summary>
    internal Action<FileStream, byte[]> WriteStep { get; set; } = WriteAndFlush;

    /// <inheritdoc />
    public async Task<LocalAuditEntry> AppendAsync(LocalAuditRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // ⬛ 고침 1 전: return AppendLocked(record);
            return await AppendLockedAsync(record, ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            // 🆕 고침 1(X7) — 쓰기 실패는 이름 있는 예외 하나로만.
            throw new LocalAuditWriteException("io", "감사 줄을 쓰지 못했습니다(디스크 · 파일 오류).", inner: ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new LocalAuditWriteException("access_denied", "감사 줄을 쓰지 못했습니다(권한 없음).", inner: ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ⬛ 고침 1 전(3-V F2 · tenantId 기본값 null = 모든 회사):
    // public async Task<IReadOnlyList<LocalAuditEntry>> ReadUnfinishedSwapsAsync(string? tenantId = null, CancellationToken ct = default)
    // { ... foreach (var e in scan.Entries) { if (tenantId is not null && !string.Equals(e.Record.TenantId, tenantId, ...)) continue; ... } }

    /// <inheritdoc />
    public Task<LocalAuditOpenReport> ReadUnfinishedSwapsAsync(string tenantId, CancellationToken ct = default) =>
        ReadOpenAsync(tenantId, swapsOnly: true, ct);

    /// <inheritdoc />
    public Task<LocalAuditOpenReport> ReadOpenOperationsAsync(string tenantId, CancellationToken ct = default) =>
        ReadOpenAsync(tenantId, swapsOnly: false, ct);

    private async Task<LocalAuditOpenReport> ReadOpenAsync(string tenantId, bool swapsOnly, CancellationToken ct)
    {
        if (!IsValidCallerId(tenantId)) throw new ArgumentException("회사 id 는 필수입니다(서버가 확정한 값 · #2).", nameof(tenantId));

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var data = await ReadAllForReadAsync(ct).ConfigureAwait(false);
            if (data is null) return new LocalAuditOpenReport(LocalAuditFileState.Missing, Array.Empty<LocalAuditEntry>(), 0, null);
            var scan = Scan(data);
            if (scan.Entries.Count == 0)
                return new LocalAuditOpenReport(LocalAuditFileState.Empty, Array.Empty<LocalAuditEntry>(), scan.UntrustedCount, scan.FirstUntrustedReason);

            var open = new Dictionary<string, LocalAuditEntry>(StringComparer.Ordinal);
            foreach (var (e, trusted) in scan.Entries)
            {
                if (!trusted) continue; // X3 — 지문 맞고 사슬 이어진 줄만 믿는다(못 믿는 줄은 UntrustedCount 로 셈)
                if (!string.Equals(e.Record.TenantId, tenantId, StringComparison.Ordinal)) continue; // X2 — 줄마다 회사 열쇠 대조
                var key = e.Record.TenantId + "\t" + e.Record.OpId;
                if (swapsOnly)
                {
                    if (e.Record.Action == LocalAuditAction.SwapStarted) open[key] = e;
                    else if (IsSwapTerminal(e.Record.Action)) open.Remove(key);
                }
                else
                {
                    if (IsOperationOpener(e.Record.Action)) open.TryAdd(key, e);
                    else if (IsOperationTerminal(e.Record.Action)) open.Remove(key);
                }
            }
            return new LocalAuditOpenReport(LocalAuditFileState.Present, open.Values.OrderBy(e => e.Seq).ToList(),
                scan.UntrustedCount, scan.FirstUntrustedReason);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<LocalAuditChainReport> VerifyChainAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var data = await ReadAllForReadAsync(ct).ConfigureAwait(false);
            // ⬛ 고침 1 전: 파일 없음 = Array.Empty → Ok=true · ValidLines=0 (3-V F4 「통째 지우기 = 초록」)
            if (data is null)
                return new LocalAuditChainReport(false, 0, null, "file_missing", false) { State = LocalAuditFileState.Missing };
            var scan = Scan(data);
            if (scan.Entries.Count == 0 && scan.FirstBadLine is null)
                return new LocalAuditChainReport(false, 0, null, "file_empty", scan.TailTorn) { State = LocalAuditFileState.Empty };
            return new LocalAuditChainReport(
                scan.FirstBadLine is null,
                scan.ValidLines,
                scan.FirstBadLine,
                scan.FirstBadReason,
                scan.TailTorn)
            {
                State = scan.Entries.Count == 0 ? LocalAuditFileState.Empty : LocalAuditFileState.Present,
                ExplainedTornLines = scan.ExplainedTorn,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsSwapTerminal(LocalAuditAction a) =>
        a is LocalAuditAction.SwapCompleted or LocalAuditAction.SwapFailed or LocalAuditAction.RecoveryResolved;

    /// <summary>🆕 고침 1(X4) — 복원 묶음을 여는(또는 이어 가는) 줄.</summary>
    private static bool IsOperationOpener(LocalAuditAction a) =>
        a is LocalAuditAction.RestoreStarted or LocalAuditAction.PreBackupCreated or LocalAuditAction.VerifyFailed or LocalAuditAction.SwapStarted;

    /// <summary>🆕 고침 1(X4) — 복원 묶음을 닫는 줄.</summary>
    private static bool IsOperationTerminal(LocalAuditAction a) =>
        a is LocalAuditAction.SwapCompleted or LocalAuditAction.SwapFailed or LocalAuditAction.RecoveryResolved
            or LocalAuditAction.RestoreAborted or LocalAuditAction.RestoreInterrupted;

    // ───────────────────────── 쓰기 ─────────────────────────

    private async Task<LocalAuditEntry> AppendLockedAsync(LocalAuditRecord record, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(FilePath);
        // ⬛ 고침 1 전(3-V F1): if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);  — 상속 권한 그대로 · 재분석 · 소유자 검사 0
        if (!string.IsNullOrEmpty(dir)) LocalAuditFolderGuard.EnsureFolder(dir);
        LocalAuditFolderGuard.EnsureExistingFileSafe(FilePath);

        using var fs = await OpenForWriteAsync(ct).ConfigureAwait(false);
        var data = new byte[fs.Length];
        fs.Position = 0;
        fs.ReadExactly(data);

        var scan = Scan(data);
        var sb = new StringBuilder();
        if (data.Length > 0 && data[^1] != (byte)'\n') sb.Append('\n'); // 깨진 조각(또는 줄바꿈 못 찍은 줄)을 닫는다

        var prev = scan.LastHash;
        var seq = scan.LastSeq + 1;
        var now = DateTime.UtcNow;

        // ⬛ 고침 1 전: if (scan.PendingTornId is not null) { 표시 줄 1개 } — 조각 하나만 설명(4 반증 P1 이중 끊김 = 영구 빨강)
        // 🆕 고침 1(X5) — 끝의 끊긴 조각 전부를 차례로 설명. 쓰레기(기록기 꼴 아님)가 하나라도 섞이면 표시하지 않는다(빨강 그대로 · F7 · F10).
        if (scan.TailTorn)
        {
            foreach (var tornId in scan.TrailingTornIds)
            {
                var marker = new LocalAuditRecord("-", "-", LocalAuditAction.TornTailNoted, LocalAuditResult.Noted, "-", TornPrefix + tornId);
                var markerEntry = Build(seq, now, marker, prev, out var markerLine);
                sb.Append(markerLine).Append('\n');
                prev = markerEntry.Hash;
                seq++;
                _logger.LogWarning("감사 파일 끝 줄이 깨져 있어 닫고 이어 씁니다(전원 끊김 흔적). 파일={Path} 조각지문={Torn}", FilePath, tornId);
            }
        }
        else if (scan.FirstBadReason == "tail_garbage")
        {
            _logger.LogWarning("감사 파일 끝에 기록기 꼴이 아닌 줄이 있어 표시 없이 이어 씁니다(사슬 빨강 유지). 파일={Path}", FilePath);
        }

        var entry = Build(seq, now, record, prev, out var line);
        sb.Append(line).Append('\n');

        var bytes = Utf8Strict.GetBytes(sb.ToString());
        var offset = fs.Length;
        // ⬛ 고침 1 전: fs.Position = fs.Length; fs.Write(bytes, 0, bytes.Length); fs.Flush(flushToDisk: true);
        try
        {
            WriteStep(fs, bytes);
        }
        catch (IOException ex)
        {
            // 🆕 고침 1(3-V F8) — 쓰기 · 내려쓰기 뒤 던짐이면 줄이 남았는지 다시 읽어 사실대로 알린다.
            var landed = LandedAt(offset, bytes);
            _logger.LogWarning(ex, "감사 줄 쓰기 중 오류 — 줄 남음={Landed} 파일={Path}", landed, FilePath);
            throw new LocalAuditWriteException(landed ? "flush_failed_line_present" : "write_failed",
                landed ? "감사 줄은 파일에 남았지만 디스크 내려쓰기를 확인하지 못했습니다." : "감사 줄을 쓰지 못했습니다(디스크 오류).",
                lineWritten: landed, inner: ex);
        }
        return entry;
    }

    /// <summary>🆕 고침 1 — 끝에 바이트를 쓰고 디스크까지 내려 쓴다(<c>Flush(true)</c>). 변이 N9 를 이 걸음 시험이 잡는다.</summary>
    internal static void WriteAndFlush(FileStream fs, byte[] bytes)
    {
        fs.Position = fs.Length;
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(flushToDisk: true);
    }

    /// <summary>🆕 고침 1(F8) — <paramref name="offset"/> 부터 <paramref name="bytes"/> 가 그대로 있는가(새 손잡이로 다시 읽음 · 못 읽으면 false).</summary>
    private bool LandedAt(long offset, byte[] bytes)
    {
        try
        {
            using var check = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (check.Length < offset + bytes.Length) return false;
            var got = new byte[bytes.Length];
            check.Position = offset;
            check.ReadExactly(got);
            return got.AsSpan().SequenceEqual(bytes);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "감사 줄 남음 확인 실패(못 남은 것으로 봄) 파일={Path}", FilePath);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "감사 줄 남음 확인 실패(못 남은 것으로 봄) 파일={Path}", FilePath);
            return false;
        }
    }

    private async Task<FileStream> OpenForWriteAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                if (attempt >= ShareRetryCount)
                    throw new LocalAuditWriteException("busy", "다른 쪽이 감사 파일을 오래 쥐고 있어 감사 줄을 쓰지 못했습니다.", inner: ex);
                if (attempt == 1) _logger.LogWarning(ex, "감사 파일을 다른 쪽이 쓰는 중이라 잠깐 기다립니다. 파일={Path}", FilePath);
                // ⬛ 고침 1 전(3-V F9): Thread.Sleep(ShareRetryDelayMs);
                await Task.Delay(ShareRetryDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>파일 전체를 읽는다. 🆕 고침 1(X6) — 파일 없음 = null(빈 배열과 가름).</summary>
    private async Task<byte[]?> ReadAllForReadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var data = new byte[fs.Length];
                fs.ReadExactly(data);
                return data;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < ShareRetryCount)
            {
                if (attempt == 1) _logger.LogWarning(ex, "감사 파일 읽기를 잠깐 기다립니다. 파일={Path}", FilePath);
                // ⬛ 고침 1 전(3-V F9): Thread.Sleep(ShareRetryDelayMs);
                await Task.Delay(ShareRetryDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsSharingViolation(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is 32 or 33; // ERROR_SHARING_VIOLATION · ERROR_LOCK_VIOLATION
    }

    private static LocalAuditEntry Build(long seq, DateTime atUtc, LocalAuditRecord r, string prevHash, out string line)
    {
        var body = string.Join('\t',
            Version,
            seq.ToString(CultureInfo.InvariantCulture),
            atUtc.ToString(TimeFormat, CultureInfo.InvariantCulture),
            r.TenantId,
            r.ParentUserId,
            r.Action.ToString(),
            r.Result.ToString(),
            r.OpId,
            r.Detail ?? string.Empty,
            prevHash);
        var hash = Sha256Hex(body);
        line = body + "\t" + hash;
        return new LocalAuditEntry(seq, atUtc, r, prevHash, hash);
    }

    // ───────────────────────── 검사 규칙 ─────────────────────────

    private static void ValidateRecord(LocalAuditRecord r)
    {
        // ⬛ 고침 1 전: ValidateId 는 IsValidId(「-」 만 있는 값도 통과 · 4 P9)
        ValidateCallerId(r.TenantId, nameof(r.TenantId));
        ValidateCallerId(r.ParentUserId, nameof(r.ParentUserId));
        ValidateCallerId(r.OpId, nameof(r.OpId));
        if (!Enum.IsDefined(r.Action)) throw new ArgumentException("모르는 동작입니다.", nameof(r.Action));
        if (!Enum.IsDefined(r.Result)) throw new ArgumentException("모르는 결과입니다.", nameof(r.Result));
        if (r.Action == LocalAuditAction.TornTailNoted)
            throw new ArgumentException("깨진 줄 표시는 기록기만 남깁니다.", nameof(r.Action));
        // ⬛ 고침 1 전 문구: "덧말은 글자 · 숫자 · . _ - : + = 만, 200자 이하로 적습니다(개인정보 · 문장 금지)." — 「막는다」 를 빼고 사실대로(F6)
        if (r.Detail is not null && !IsValidCallerDetail(r.Detail))
            throw new ArgumentException("덧말이 글자 규칙 밖입니다 — 글자 · 숫자 · . _ - : + = 만 · 200자 이하 · 토큰 · 주민번호 · 전화번호 꼴은 받지 않습니다(비밀을 넣지 않는 것은 호출자 몫).", nameof(r.Detail));
    }

    private static void ValidateCallerId(string? v, string name)
    {
        if (!IsValidCallerId(v)) throw new ArgumentException("id 는 영문 · 숫자 · - _ 만(글자 · 숫자 1개 이상), 64자 이하입니다.", name);
    }

    /// <summary>🆕 고침 1(4 P9) — 호출자 id = 줄 꼴 id + 글자 · 숫자 1개 이상(<c>-</c> 같은 표시 줄 자리 값 거절).</summary>
    private static bool IsValidCallerId(string? v)
    {
        if (!IsValidId(v)) return false;
        foreach (var c in v!)
            if (char.IsAsciiLetterOrDigit(c)) return true;
        return false;
    }

    private static bool IsValidId(string? v)
    {
        if (string.IsNullOrEmpty(v) || v.Length > MaxIdLength) return false;
        foreach (var c in v)
            if (!IsIdChar(c)) return false;
        return true;
    }

    private static bool IsIdChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_';

    private static bool IsDetailChar(char c) => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '+' or '=';

    private static bool IsValidDetail(string v)
    {
        if (v.Length == 0 || v.Length > MaxDetailLength) return false;
        foreach (var c in v)
            if (!IsDetailChar(c)) return false;
        return true;
    }

    /// <summary>🆕 고침 1(3-V F6) — 호출자 덧말 = 글자 규칙 + 비밀 모양 거절(JWT · 대소문자 섞인 긴 덩어리 · 주민번호 · 휴대전화). 읽을 때는 글자 규칙만(옛 줄 보존).</summary>
    private static bool IsValidCallerDetail(string v)
    {
        if (!IsValidDetail(v)) return false;
        if (v.Contains("eyJ", StringComparison.Ordinal)) return false;
        foreach (Match m in MixedCaseLongRun.Matches(v))
            if (m.Value.Any(char.IsAsciiLetterUpper) && m.Value.Any(char.IsAsciiLetterLower)) return false;
        if (ResidentNumberShape.IsMatch(v)) return false;
        if (MobilePhoneShape.IsMatch(v)) return false;
        return true;
    }

    private static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Utf8Strict.GetBytes(s))).ToLowerInvariant();

    private static string TornId(ReadOnlySpan<byte> fragment) => Convert.ToHexString(SHA256.HashData(fragment)).ToLowerInvariant()[..16];

    private static bool IsHex64(string s) => s.Length == 64 && IsHexChars(s);

    private static bool IsHexChars(string s)
    {
        foreach (var c in s)
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }

    // ───────────────────────── 훑기(쓰기 · 읽기 · 검사 공용) ─────────────────────────

    private sealed class ScanResult
    {
        /// <summary>읽은 줄 전부 + 믿는가(X3 — 지문 맞고 · 사슬 이어지고 · 순번 맞음).</summary>
        public List<(LocalAuditEntry Entry, bool Trusted)> Entries { get; } = new();
        public int ValidLines { get; set; }
        public int? FirstBadLine { get; set; }
        public string? FirstBadReason { get; set; }
        public bool TailTorn { get; set; }
        /// <summary>🆕 고침 1(X5) — 끝의 끊긴 조각 지문들(파일 차례). TailTorn 일 때만 채움.</summary>
        public List<string> TrailingTornIds { get; } = new();
        public int ExplainedTorn { get; set; }
        public int UntrustedCount { get; set; }
        public string? FirstUntrustedReason { get; set; }
        public string LastHash { get; set; } = ZeroHash;
        public long LastSeq { get; set; }

        public void Bad(int lineNo, string reason)
        {
            if (FirstBadLine is not null) return;
            FirstBadLine = lineNo;
            FirstBadReason = reason;
        }

        public void Untrusted(string reason)
        {
            UntrustedCount++;
            FirstUntrustedReason ??= reason;
        }
    }

    /// <summary>표시 줄을 기다리는 조각(파일 줄 번호 · 조각 지문 · 기록기가 쓰다 끊긴 꼴인가).</summary>
    private readonly record struct Fragment(int LineNo, string TornId, bool TornShaped);

    // ⬛ 고침 1 전 Scan 요지(#1 기록): 깨진 줄 하나를 PendingTornId 로 들고, 다음 줄이 그 지문의 표시 줄이 아니면 torn_unexplained ·
    //   맨 끝 표시 없는 조각은 줄바꿈 여부 · 꼴과 무관하게 TailTorn(초록) · Entries.Add 무조건(지문 · 사슬 틀린 줄도 미완 읽기에 씀 · 3-V F3).
    private static ScanResult Scan(byte[] data)
    {
        var res = new ScanResult();
        var expectedPrev = ZeroHash;
        var expectedSeq = 1L;
        var pending = new List<Fragment>();
        var lineNo = 0;
        var start = 0;

        while (start < data.Length)
        {
            var nl = Array.IndexOf(data, (byte)'\n', start);
            var terminated = nl >= 0;
            var end = terminated ? nl : data.Length;
            var seg = new ReadOnlySpan<byte>(data, start, end - start);
            start = terminated ? nl + 1 : data.Length;
            lineNo++;

            var entry = TryParse(seg, out var selfHashOk);
            if (entry is null)
            {
                pending.Add(new Fragment(lineNo, TornId(seg), IsTornShaped(seg, expectedSeq, expectedPrev)));
                continue;
            }

            // 🆕 고침 1(X3) — 이 줄을 믿는가: 지문 맞음 · 사슬 이어짐 · 순번 맞음 · (표시 줄이면) 그 조각을 설명함. 못 믿으면 까닭 하나를 센다.
            string? untrusted = null;
            if (!selfHashOk) { res.Bad(lineNo, "hash_mismatch"); untrusted ??= "hash_mismatch"; }
            if (!string.Equals(entry.PrevHash, expectedPrev, StringComparison.Ordinal)) { res.Bad(lineNo, "chain_break"); untrusted ??= "chain_break"; }
            if (entry.Seq != expectedSeq) { res.Bad(lineNo, "seq_gap"); untrusted ??= "seq_gap"; }

            if (entry.Record.Action == LocalAuditAction.TornTailNoted)
            {
                if (pending.Count == 0)
                {
                    res.Bad(lineNo, "torn_marker_without_torn");
                    untrusted ??= "torn_marker_without_torn";
                }
                else
                {
                    var head = pending[0];
                    var isMarker = head.TornShaped
                        && string.Equals(entry.Record.Detail, TornPrefix + head.TornId, StringComparison.Ordinal);
                    if (isMarker)
                    {
                        pending.RemoveAt(0);
                        res.ExplainedTorn++;
                    }
                    else
                    {
                        res.Bad(head.LineNo, "torn_unexplained");
                        foreach (var _ in pending) res.Untrusted("torn_unexplained");
                        pending.Clear();
                        untrusted ??= "torn_unexplained";
                    }
                }
            }
            else if (pending.Count > 0)
            {
                // 설명 없는 깨진 줄(가운데 쓰레기 · 끼워 넣은 줄) — 줄마다 못 믿는 줄로 센다.
                res.Bad(pending[0].LineNo, "torn_unexplained");
                foreach (var _ in pending) res.Untrusted("torn_unexplained");
                pending.Clear();
            }

            var trusted = untrusted is null;
            if (!trusted) res.Untrusted(untrusted!);
            if (selfHashOk && res.FirstBadLine is null) res.ValidLines++;
            res.Entries.Add((entry, trusted));
            expectedPrev = entry.Hash;
            expectedSeq = entry.Seq + 1;
            res.LastHash = entry.Hash;
            res.LastSeq = entry.Seq;
        }

        // ⬛ 고침 1 전: if (res.PendingTornId is not null) res.TailTorn = true;  — 맨 끝 조각은 꼴 · 줄바꿈 무관 초록(F7 · F10)
        // 🆕 고침 1 — 맨 끝 조각이 모두 「기록기가 쓰다 끊긴 꼴」 일 때만 전원 끊김 흔적(초록 · 다음 쓰기가 차례로 표시). 쓰레기가 하나라도 있으면 빨강.
        if (pending.Count > 0)
        {
            var garbage = pending.FirstOrDefault(p => !p.TornShaped);
            if (garbage.TornId is not null)
            {
                res.Bad(garbage.LineNo, "tail_garbage");
                foreach (var p in pending.Where(p => !p.TornShaped)) res.Untrusted("tail_garbage");
            }
            else
            {
                res.TailTorn = true;
                res.TrailingTornIds.AddRange(pending.Select(p => p.TornId));
            }
        }
        return res;
    }

    /// <summary>
    /// 🆕 고침 1(X5 · F7 · F10) — 이 조각이 「기록기가 줄 앞부분을 쓰다 끊긴 꼴」 인가.
    /// <c>v1</c> 의 앞부분으로 시작 · 칸이 11개 이하 · 다 적힌 칸은 칸 규칙 그대로(순번 = 기다리는 순번 · 앞 줄 지문 = 기다리는 지문) ·
    /// 마지막 칸은 그 칸 값의 앞부분 · 끝에 0 바이트(NTFS 전원 끊김 흔적)가 붙어도 됨 · 0 바이트만 있는 조각도 됨.
    /// 빈 줄 · BOM · 아무 글자 = 아님(쓰레기).
    /// </summary>
    private static bool IsTornShaped(ReadOnlySpan<byte> seg, long expectedSeq, string expectedPrev)
    {
        var len = seg.Length;
        while (len > 0 && seg[len - 1] == 0) len--;
        var hadNul = len < seg.Length;
        if (len == 0) return hadNul;
        if (!TryDecodePrefix(seg[..len], out var text) || text.Length == 0) return false;

        var f = text.Split('\t');
        if (f.Length > FieldCount) return false;
        var seqText = expectedSeq.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < f.Length; i++)
        {
            var partial = i == f.Length - 1;
            var v = f[i];
            var ok = i switch
            {
                0 => partial ? Version.StartsWith(v, StringComparison.Ordinal) : v == Version,
                1 => partial ? seqText.StartsWith(v, StringComparison.Ordinal) : v == seqText,
                2 => partial ? IsTimePrefix(v) : DateTime.TryParseExact(v, TimeFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _),
                3 or 4 or 7 => v.Length <= MaxIdLength && v.All(IsIdChar) && (partial || v.Length > 0),
                5 => partial ? Enum.GetNames<LocalAuditAction>().Any(n => n.StartsWith(v, StringComparison.Ordinal))
                             : Enum.GetNames<LocalAuditAction>().Contains(v),
                6 => partial ? Enum.GetNames<LocalAuditResult>().Any(n => n.StartsWith(v, StringComparison.Ordinal))
                             : Enum.GetNames<LocalAuditResult>().Contains(v),
                8 => v.Length <= MaxDetailLength && v.All(IsDetailChar),
                9 => partial ? expectedPrev.StartsWith(v, StringComparison.Ordinal) : v == expectedPrev,
                _ => partial && v.Length < 64 && IsHexChars(v), // 10 = 이 줄 지문(다 적혔으면 줄로 읽혔을 것)
            };
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>엄격 UTF-8 로 읽되, 끝에서 끊긴 여러 바이트 글자 하나(앞 바이트 + 이어 바이트 일부)는 봐준다.</summary>
    private static bool TryDecodePrefix(ReadOnlySpan<byte> bytes, out string text)
    {
        for (var cut = 0; cut <= 3 && cut < bytes.Length; cut++)
        {
            if (cut > 0)
            {
                var lead = bytes[bytes.Length - cut];
                if (lead < 0xC2 || lead > 0xF4) continue; // 잘린 자리 첫 바이트가 여러 바이트 글자의 앞 바이트일 때만
            }
            try
            {
                text = Utf8Strict.GetString(bytes[..(bytes.Length - cut)]);
                return true;
            }
            catch (DecoderFallbackException)
            {
                // 판정 값 — 더 잘라 본다(빈 catch 아님 · 끝까지 못 읽으면 false)
                continue;
            }
        }
        text = string.Empty;
        return false;
    }

    private static bool IsTimePrefix(string v)
    {
        if (v.Length > TimeFormat.Length) return false;
        for (var i = 0; i < v.Length; i++)
        {
            var p = TimeFormat[i];
            var c = v[i];
            var ok = p switch
            {
                'y' or 'M' or 'd' or 'H' or 'm' or 's' or 'f' => c is >= '0' and <= '9',
                _ => c == p,
            };
            if (!ok) return false;
        }
        return true;
    }

    private static LocalAuditEntry? TryParse(ReadOnlySpan<byte> seg, out bool selfHashOk)
    {
        selfHashOk = false;
        string text;
        try
        {
            text = Utf8Strict.GetString(seg);
        }
        catch (DecoderFallbackException)
        {
            return null; // 깨진 바이트 = 줄로 못 읽음 → 호출자가 깨진 조각으로 다룬다(빈 catch 아님 · 판정 값)
        }

        var f = text.Split('\t');
        if (f.Length != FieldCount || f[0] != Version) return null;
        if (!long.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seq) || seq < 1) return null;
        if (!DateTime.TryParseExact(f[2], TimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)) return null;
        var isMarkerShape = f[5] == nameof(LocalAuditAction.TornTailNoted);
        if (!(isMarkerShape ? f[3] == "-" : IsValidId(f[3]))) return null;
        if (!(isMarkerShape ? f[4] == "-" : IsValidId(f[4]))) return null;
        if (!Enum.TryParse<LocalAuditAction>(f[5], ignoreCase: false, out var action) || !Enum.IsDefined(action) || f[5] != action.ToString()) return null;
        if (!Enum.TryParse<LocalAuditResult>(f[6], ignoreCase: false, out var result) || !Enum.IsDefined(result) || f[6] != result.ToString()) return null;
        if (!IsValidId(f[7])) return null;
        string? detail = f[8].Length == 0 ? null : f[8];
        if (detail is not null && !IsValidDetail(detail)) return null;
        if (!IsHex64(f[9]) || !IsHex64(f[10])) return null;

        var body = text[..text.LastIndexOf('\t')];
        selfHashOk = string.Equals(Sha256Hex(body), f[10], StringComparison.Ordinal);
        return new LocalAuditEntry(seq, at, new LocalAuditRecord(f[3], f[4], action, result, f[7], detail), f[9], f[10]);
    }
}
