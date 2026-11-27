using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
///
/// 🔴 <b>줄 꼴</b>(탭 구분 · UTF-8 · BOM 없음 · 끝 <c>\n</c>):
///   <c>v1 \t 순번 \t 시각(UTC · 7자리 소수 · Z) \t 회사 \t 대표 user_id \t 동작 \t 결과 \t 묶음 id \t 덧말 \t 앞 줄 지문 \t 이 줄 지문</c>
///   이 줄 지문 = SHA-256(마지막 탭 앞 글자 전부의 UTF-8) 소문자 16진. 첫 줄의 앞 줄 지문 = 0 64개.
///
/// 🔴 <b>동시 쓰기</b> — 2겹: ① 같은 경로는 프로세스 안 한 줄 서기(경로별 SemaphoreSlim · 인스턴스가 여럿이어도 같음)
///   ② 파일은 <c>FileShare.Read</c> 로 열어 다른 프로세스의 쓰기를 막는다(읽기는 허용 · 막히면 잠깐 기다렸다 다시).
///
/// 🔴 <b>전원 끊김</b> — 줄 끝마다 <c>Flush(true)</c>(디스크까지) + <c>WriteThrough</c>. 마지막 줄이 줄바꿈 없이 깨져 있으면
///   다음 쓰기가 그 조각을 줄바꿈으로 닫고 <see cref="LocalAuditAction.TornTailNoted"/> 줄(조각 지문)을 남긴 뒤 이어 쓴다.
///   사슬 검사는 「깨진 조각 바로 뒤에 그 조각 지문을 적은 표시 줄」 일 때만 통과시킨다.
///
/// 🔴 <b>네트워크 의존성 0</b>(#18 · #22 · 설계 §12 G11) — 이 형식은 파일 · 해시 · 로그 형식만 쓴다. 시험이 IL 까지 훑어 잰다.
///
/// ⚠️ 쓸 때마다 파일 전체를 읽는다 — 복원 · 비번 되찾기는 드문 일이라 파일이 작다(한 해 수십 줄 안팎 · 추정). 단순함을 고른 값.
/// </remarks>
public sealed class LocalAuditFileWriter : ILocalAuditFile
{
    private const string Version = "v1";
    private const int FieldCount = 11;
    private const int MaxIdLength = 64;
    private const int MaxDetailLength = 200;
    private const int ShareRetryCount = 100;
    private const int ShareRetryDelayMs = 50;
    private const string TornPrefix = "torn-sha256:";
    private static readonly string ZeroHash = new('0', 64);
    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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

    /// <inheritdoc />
    public async Task<LocalAuditEntry> AppendAsync(LocalAuditRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return AppendLocked(record);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalAuditEntry>> ReadUnfinishedSwapsAsync(string? tenantId = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var scan = Scan(ReadAllForRead());
            var open = new Dictionary<string, LocalAuditEntry>(StringComparer.Ordinal);
            foreach (var e in scan.Entries)
            {
                if (tenantId is not null && !string.Equals(e.Record.TenantId, tenantId, StringComparison.Ordinal)) continue;
                var key = e.Record.TenantId + "\t" + e.Record.OpId;
                if (e.Record.Action == LocalAuditAction.SwapStarted) open[key] = e;
                else if (IsSwapTerminal(e.Record.Action)) open.Remove(key);
            }
            return open.Values.OrderBy(e => e.Seq).ToList();
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
            var scan = Scan(ReadAllForRead());
            return new LocalAuditChainReport(
                scan.FirstBadLine is null,
                scan.ValidLines,
                scan.FirstBadLine,
                scan.FirstBadReason,
                scan.TailTorn);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsSwapTerminal(LocalAuditAction a) =>
        a is LocalAuditAction.SwapCompleted or LocalAuditAction.SwapFailed or LocalAuditAction.RecoveryResolved;

    // ───────────────────────── 쓰기 ─────────────────────────

    private LocalAuditEntry AppendLocked(LocalAuditRecord record)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var fs = OpenForWrite();
        var data = new byte[fs.Length];
        fs.Position = 0;
        fs.ReadExactly(data);

        var scan = Scan(data);
        var sb = new StringBuilder();
        if (data.Length > 0 && data[^1] != (byte)'\n') sb.Append('\n'); // 깨진 조각(또는 줄바꿈 못 찍은 줄)을 닫는다

        var prev = scan.LastHash;
        var seq = scan.LastSeq + 1;
        var now = DateTime.UtcNow;

        if (scan.PendingTornId is not null)
        {
            var marker = new LocalAuditRecord("-", "-", LocalAuditAction.TornTailNoted, LocalAuditResult.Noted, "-", TornPrefix + scan.PendingTornId);
            var markerEntry = Build(seq, now, marker, prev, out var markerLine);
            sb.Append(markerLine).Append('\n');
            prev = markerEntry.Hash;
            seq++;
            _logger.LogWarning("감사 파일 끝 줄이 깨져 있어 닫고 이어 씁니다(전원 끊김 흔적). 파일={Path} 조각지문={Torn}", FilePath, scan.PendingTornId);
        }

        var entry = Build(seq, now, record, prev, out var line);
        sb.Append(line).Append('\n');

        var bytes = Utf8Strict.GetBytes(sb.ToString());
        fs.Position = fs.Length;
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(flushToDisk: true);
        return entry;
    }

    private FileStream OpenForWrite()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough);
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < ShareRetryCount)
            {
                if (attempt == 1) _logger.LogWarning(ex, "감사 파일을 다른 쪽이 쓰는 중이라 잠깐 기다립니다. 파일={Path}", FilePath);
                Thread.Sleep(ShareRetryDelayMs);
            }
        }
    }

    private byte[] ReadAllForRead()
    {
        if (!File.Exists(FilePath)) return Array.Empty<byte>();
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
                Thread.Sleep(ShareRetryDelayMs);
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
            atUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
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
        ValidateId(r.TenantId, nameof(r.TenantId));
        ValidateId(r.ParentUserId, nameof(r.ParentUserId));
        ValidateId(r.OpId, nameof(r.OpId));
        if (!Enum.IsDefined(r.Action)) throw new ArgumentException("모르는 동작입니다.", nameof(r.Action));
        if (!Enum.IsDefined(r.Result)) throw new ArgumentException("모르는 결과입니다.", nameof(r.Result));
        if (r.Action == LocalAuditAction.TornTailNoted)
            throw new ArgumentException("깨진 줄 표시는 기록기만 남깁니다.", nameof(r.Action));
        if (r.Detail is not null && !IsValidDetail(r.Detail))
            throw new ArgumentException("덧말은 글자 · 숫자 · . _ - : + = 만, 200자 이하로 적습니다(개인정보 · 문장 금지).", nameof(r.Detail));
    }

    private static void ValidateId(string? v, string name)
    {
        if (!IsValidId(v)) throw new ArgumentException("id 는 영문 · 숫자 · - _ 만, 64자 이하입니다.", name);
    }

    private static bool IsValidId(string? v)
    {
        if (string.IsNullOrEmpty(v) || v.Length > MaxIdLength) return false;
        foreach (var c in v)
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')) return false;
        return true;
    }

    private static bool IsValidDetail(string v)
    {
        if (v.Length == 0 || v.Length > MaxDetailLength) return false;
        foreach (var c in v)
            if (!(char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '+' or '=')) return false;
        return true;
    }

    private static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Utf8Strict.GetBytes(s))).ToLowerInvariant();

    private static string TornId(ReadOnlySpan<byte> fragment) => Convert.ToHexString(SHA256.HashData(fragment)).ToLowerInvariant()[..16];

    private static bool IsHex64(string s)
    {
        if (s.Length != 64) return false;
        foreach (var c in s)
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }

    // ───────────────────────── 훑기(쓰기 · 읽기 · 검사 공용) ─────────────────────────

    private sealed class ScanResult
    {
        public List<LocalAuditEntry> Entries { get; } = new();
        public int ValidLines { get; set; }
        public int? FirstBadLine { get; set; }
        public string? FirstBadReason { get; set; }
        public bool TailTorn { get; set; }
        public string? PendingTornId { get; set; }
        public string LastHash { get; set; } = ZeroHash;
        public long LastSeq { get; set; }

        public void Bad(int lineNo, string reason)
        {
            if (FirstBadLine is not null) return;
            FirstBadLine = lineNo;
            FirstBadReason = reason;
        }
    }

    private static ScanResult Scan(byte[] data)
    {
        var res = new ScanResult();
        var expectedPrev = ZeroHash;
        var expectedSeq = 1L;
        int? pendingLine = null;
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
                if (res.PendingTornId is not null) res.Bad(pendingLine ?? lineNo, "torn_unexplained");
                res.PendingTornId = TornId(seg);
                pendingLine = lineNo;
                continue;
            }

            if (!selfHashOk) res.Bad(lineNo, "hash_mismatch");
            if (!string.Equals(entry.PrevHash, expectedPrev, StringComparison.Ordinal)) res.Bad(lineNo, "chain_break");
            if (entry.Seq != expectedSeq) res.Bad(lineNo, "seq_gap");

            if (res.PendingTornId is not null)
            {
                var isMarker = entry.Record.Action == LocalAuditAction.TornTailNoted
                    && string.Equals(entry.Record.Detail, TornPrefix + res.PendingTornId, StringComparison.Ordinal);
                if (!isMarker) res.Bad(pendingLine ?? lineNo, "torn_unexplained");
                res.PendingTornId = null;
                pendingLine = null;
            }
            else if (entry.Record.Action == LocalAuditAction.TornTailNoted)
            {
                res.Bad(lineNo, "torn_marker_without_torn");
            }

            if (selfHashOk && res.FirstBadLine is null) res.ValidLines++;
            res.Entries.Add(entry);
            expectedPrev = entry.Hash;
            expectedSeq = entry.Seq + 1;
            res.LastHash = entry.Hash;
            res.LastSeq = entry.Seq;
        }

        // 맨 끝의 표시 없는 깨진 조각 = 전원 끊김 흔적(다음 쓰기가 닫는다) — 이것만으로는 빨강 아님.
        if (res.PendingTornId is not null) res.TailTorn = true;
        return res;
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
        if (!DateTime.TryParseExact(f[2], "yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture,
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
