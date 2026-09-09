using System.Runtime.Versioning;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#pragma warning disable CA1416

namespace HitPan.API.Controllers;

/// <summary>
/// 레거시 히트판 MDB 데이터 마이그레이션 컨트롤러
/// - 기존 VB + Access(.mdb) 데이터를 신규 ERP DB로 이관
/// - tenant_admin 권한 필수
/// - Windows 전용 (Microsoft.Jet.OLEDB ACE 드라이버 의존) — Linux 컨테이너 배포 시 미지원
///   사장님 헌법 #19 warnings 0 준수: SupportedOSPlatform 어트리뷰트로 명시 → CA1416 해소
/// </summary>
[ApiController]
[Route("api/migration")]
[Authorize(Policy = "TenantAdminOnly")]
[SupportedOSPlatform("windows")]
public sealed class MigrationController : ControllerBase
{
    private readonly MdbMigrationService _migrationService;
    private readonly ILogger<MigrationController> _logger;
    private readonly MigrationJobStore _jobStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMigrationProgressService _progress;
    private readonly MdbReconciliationService _reconciliation;

    public MigrationController(
        MdbMigrationService migrationService,
        ILogger<MigrationController> logger,
        MigrationJobStore jobStore,
        IServiceScopeFactory scopeFactory,
        IMigrationProgressService progress,
        MdbReconciliationService reconciliation)
    {
        _migrationService = migrationService;
        _logger = logger;
        _jobStore = jobStore;
        _scopeFactory = scopeFactory;
        _progress = progress;
        _reconciliation = reconciliation;
    }

    /// <summary>
    /// MDB 폴더 내 테이블 건수 미리보기 (실제 import 없음)
    /// - 마이그레이션 전 데이터 규모를 확인할 때 사용
    /// - 핫픽스 2026-05-13: mdbPassword 파라미터 추가 (비번 걸린 레거시 MDB 지원)
    /// </summary>
    /// 작22 (2026-09-09) A4 · 병렬이슈 11 봉합: GET → POST <b>이동</b>(기능은 그대로).
    ///   종전엔 <c>?folderPath=…&amp;mdbPassword=…</c> 라 MDB 비번이 URL·서버 액세스 로그·브라우저 기록에 남았다.
    ///   같은 기능을 바디로 옮긴 것이고 <b>GET 은 남기지 않는다</b> — 남기면 구멍이 그대로라 봉합이 아니다(PM 전결).
    [HttpPost("legacy-mdb/preview")]
    public async Task<IActionResult> PreviewLegacyMdb(
        [FromBody] MdbMigrationRequest request,
        CancellationToken ct)
    {
        // tenant_id는 JWT 클레임 기반 TenantMiddleware에서 설정
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var folderPath = request.FolderPath;
        var mdbPassword = request.MdbPassword;

        // 폴더 경로 유효성 검증
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return BadRequest(new { message = "MDB 폴더 경로를 입력해주세요." });
        }

        try
        {
            // preview = true: 건수만 조회, 실제 데이터 이관 없음
            var result = await _migrationService.PreviewAsync(folderPath, tenantId, mdbPassword, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Preview] MDB 파일 미발견 folder={Folder}", folderPath);
            return NotFound(new { message = $"MDB 파일을 찾을 수 없습니다. 폴더 경로를 확인해주세요. ({ex.Message})" });
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Preview] 폴더 미존재 folder={Folder}", folderPath);
            return NotFound(new { message = $"폴더가 존재하지 않습니다: {folderPath}" });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "[Preview] 폴더 접근 권한 없음 folder={Folder}", folderPath);
            return StatusCode(403, new { message = $"폴더 접근 권한이 없습니다: {folderPath}" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[Preview] 데이터 무결성·형식 오류 folder={Folder}", folderPath);
            return BadRequest(new { message = ex.Message });
        }
        catch (System.Data.OleDb.OleDbException ex)
        {
            _logger.LogWarning(ex, "[Preview] OLEDB 오류 folder={Folder} hresult={HResult}", folderPath, ex.HResult);
            var hint = ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("암호")
                ? "MDB 비밀번호가 틀렸거나 비번이 걸려있습니다. 비밀번호 칸을 확인해주세요."
                : "MDB 파일을 열 수 없습니다. ACE OLEDB Provider 설치 여부 + 파일 손상 여부를 확인해주세요.";
            return BadRequest(new { message = $"{hint} (상세: {ex.Message})" });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "[Preview] ACE Provider 미설치 가능성 folder={Folder}", folderPath);
            return StatusCode(500, new { message = "MDB 처리 엔진(Microsoft.ACE.OLEDB.12.0)이 설치되지 않았을 가능성이 있습니다. 서버 설정을 확인해주세요." });
        }
        catch (Exception ex)
        {
            // 봉합: 미처리 예외도 사용자에게 의미있는 메시지로 (silent swallow 금지 - 헌법 #15)
            _logger.LogError(ex, "[Preview] 미처리 예외 folder={Folder}", folderPath);
            return StatusCode(500, new { message = $"미리보기 실행 중 오류가 발생했습니다: {ex.GetType().Name} - {ex.Message}" });
        }
    }

    /// <summary>
    /// 로그에 넣는 사용자 입력에서 줄바꿈·탭을 제거한다 ([3-V] 2026-09-08 · CodeQL cs/log-forging · 병렬이슈 14).
    /// 폴더 경로는 사용자가 치는 값이라 개행을 섞으면 가짜 로그 줄을 만들 수 있다.
    /// </summary>
    private static string ForLog(string? value)
        => System.Text.RegularExpressions.Regex.Replace(value ?? string.Empty, @"[\r\n\t]+", " ").Trim();

    /// <summary>
    /// 레거시 MDB ↔ 히트판 대사표 (20260904작21 갈래 B2).
    /// - 레거시 쪽은 MDB 를 읽기 전용으로 직접 집계, 히트판 쪽은 현재 테넌트의 이관 결과를 집계해 나란히 놓는다.
    /// - 이관 전에 부르면 레거시 열만 의미가 있다(히트판 열은 0 → DIFF). 이관 후 다시 부르면 판정이 선다.
    /// - 예외 처리는 PreviewLegacyMdb 와 같은 사다리 (비번 힌트 · 엔진 미설치 · 폴더 오류).
    /// </summary>
    /// 작22 (2026-09-09) A4 · 병렬이슈 11: preview 와 같은 이유로 GET → POST 이동(비번을 URL 에 싣지 않는다).
    [HttpPost("legacy-mdb/reconcile")]
    public async Task<IActionResult> ReconcileLegacyMdb(
        [FromBody] MdbMigrationRequest request,
        CancellationToken ct)
    {
        // tenant_id는 JWT 클레임 기반 TenantMiddleware에서 설정 (헌법 #2 — 파라미터로 받지 않는다)
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        var folderPath = request.FolderPath;
        var mdbPassword = request.MdbPassword;

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return BadRequest(new { message = "MDB 폴더 경로를 입력해주세요." });
        }

        try
        {
            var report = await _reconciliation.BuildAsync(folderPath, mdbPassword, tenantId, ct).ConfigureAwait(false);
            return Ok(report);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Reconcile] MDB 파일 미발견 folder={Folder}", ForLog(folderPath));
            return NotFound(new { message = $"MDB 파일을 찾을 수 없습니다. 폴더 경로를 확인해주세요. ({ex.Message})" });
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Reconcile] 폴더 미존재 folder={Folder}", ForLog(folderPath));
            return NotFound(new { message = $"폴더가 존재하지 않습니다: {folderPath}" });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "[Reconcile] 폴더 접근 권한 없음 folder={Folder}", ForLog(folderPath));
            return StatusCode(403, new { message = $"폴더 접근 권한이 없습니다: {folderPath}" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[Reconcile] 데이터 무결성·형식 오류 folder={Folder}", ForLog(folderPath));
            return BadRequest(new { message = ex.Message });
        }
        catch (System.Data.OleDb.OleDbException ex)
        {
            _logger.LogWarning(ex, "[Reconcile] OLEDB 오류 folder={Folder} hresult={HResult}", ForLog(folderPath), ex.HResult);
            var hint = ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("암호")
                ? "MDB 비밀번호가 틀렸거나 비번이 걸려있습니다. 비밀번호 칸을 확인해주세요."
                : "MDB 파일을 열 수 없습니다. ACE OLEDB Provider 설치 여부 + 파일 손상 여부를 확인해주세요.";
            return BadRequest(new { message = $"{hint} (상세: {ex.Message})" });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "[Reconcile] ACE Provider 미설치 가능성 folder={Folder}", ForLog(folderPath));
            return StatusCode(500, new { message = "MDB 처리 엔진(Microsoft.ACE.OLEDB.12.0)이 설치되지 않았을 가능성이 있습니다. 서버 설정을 확인해주세요." });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("[Reconcile] 요청 취소 folder={Folder}", ForLog(folderPath));
            return StatusCode(499, new { message = "대사 요청이 취소되었습니다." });
        }
        catch (Exception ex)
        {
            // 미처리 예외도 사용자에게 의미있는 메시지로 (silent swallow 금지 - 헌법 #15)
            _logger.LogError(ex, "[Reconcile] 미처리 예외 folder={Folder}", ForLog(folderPath));
            return StatusCode(500, new { message = $"대사표 작성 중 오류가 발생했습니다: {ex.GetType().Name} - {ex.Message}" });
        }
    }

    /// <summary>
    /// 레거시 MDB 데이터를 신규 ERP DB로 마이그레이션 실행
    /// - 업체, 상품, BOM, 사원, 발주, 수주, 재고원장, 세금계산서, 수금, 분개, 현금출납 등 일괄 이관
    /// </summary>
    [HttpPost("legacy-mdb")]
    public async Task<IActionResult> MigrateLegacyMdb([FromBody] MdbMigrationRequest request, CancellationToken ct)
    {
        // tenant_id는 JWT 클레임 기반 TenantMiddleware에서 설정
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Forbid();
        }

        // 폴더 경로 유효성 검증
        if (string.IsNullOrWhiteSpace(request.FolderPath))
        {
            return BadRequest(new { message = "MDB 폴더 경로를 입력해주세요." });
        }

        // 작22 (2026-09-09) A2: 덮어쓰기는 아직 못 받는다 (⛔ 본체는 사장님 Q1 답 후 · 헌법 #33).
        //   이 동기 엔드포인트는 종전 그대로 — 모드를 서비스에 넘기지 않는다(baseline·기존 호출자 동작 불변).
        if (RejectUnsupportedMode(request) is { } modeError) return modeError;

        try
        {
            // 실제 마이그레이션 실행 — 테이블별 이관 건수 반환
            var result = await _migrationService.MigrateAsync(request.FolderPath, tenantId, request.MdbPassword, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Migrate] MDB 파일 미발견 folder={Folder}", request.FolderPath);
            return NotFound(new { message = $"MDB 파일을 찾을 수 없습니다: {ex.Message}" });
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Migrate] 폴더 미존재 folder={Folder}", request.FolderPath);
            return NotFound(new { message = $"폴더가 존재하지 않습니다: {request.FolderPath}" });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "[Migrate] 폴더 접근 권한 없음 folder={Folder}", request.FolderPath);
            return StatusCode(403, new { message = $"폴더 접근 권한이 없습니다: {request.FolderPath}" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[Migrate] 데이터 무결성·형식 오류 folder={Folder}", request.FolderPath);
            return BadRequest(new { message = ex.Message });
        }
        catch (System.Data.OleDb.OleDbException ex)
        {
            _logger.LogWarning(ex, "[Migrate] OLEDB 오류 folder={Folder} hresult={HResult}", request.FolderPath, ex.HResult);
            var hint = ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("암호")
                ? "MDB 비밀번호가 틀렸거나 비번이 걸려있습니다. 비밀번호 칸을 확인해주세요."
                : "MDB 파일을 열 수 없습니다. ACE OLEDB Provider 설치 여부 + 파일 손상 여부를 확인해주세요.";
            return BadRequest(new { message = $"{hint} (상세: {ex.Message})" });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "[Migrate] ACE Provider 미설치 가능성 folder={Folder}", request.FolderPath);
            return StatusCode(500, new { message = "MDB 처리 엔진(Microsoft.ACE.OLEDB.12.0)이 설치되지 않았을 가능성이 있습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Migrate] 미처리 예외 folder={Folder}", request.FolderPath);
            return StatusCode(500, new { message = $"마이그레이션 실행 중 오류: {ex.GetType().Name} - {ex.Message}" });
        }
    }

    /// <summary>
    /// 2026-05-14: 백그라운드 마이그 시작 — Cloudflare 524 회피용.
    /// POST 즉시 JobId 반환 (1초 내). 진행률은 /status/{jobId} GET 폴링.
    ///
    /// 작22 (2026-09-09) A3 · 별지 §1-3 (5/16 사장님 UX): 여기서는 <b>1단계(기초자료)만</b> 돌리고 잡을 <c>paused</c> 로 둔다.
    ///   끝나면 <c>PhaseCompleted(1)</c> 이 화면으로 가고 화면이 사장님 문안 다이얼로그를 띄운다.
    ///   「예」면 <c>legacy-mdb/{jobId}/continue</c> 가 같은 잡으로 2단계를 잇는다. 「아니오」여도 ERP 는 기초자료만으로 바로 쓸 수 있다(헌법 #20).
    /// </summary>
    [HttpPost("legacy-mdb/start")]
    public async Task<IActionResult> StartMigrationJob([FromBody] MdbMigrationRequest request)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.FolderPath))
            return BadRequest(new { message = "MDB 폴더 경로를 입력해주세요." });

        // 작22 (2026-09-09) A2: 덮어쓰기 요청은 여기서 막는다 (⛔ 본체는 사장님 Q1 답 후 · 헌법 #33 — 그 자리 코드 0).
        if (RejectUnsupportedMode(request) is { } modeError) return modeError;

        // P0 #5 (2026-05-14): migration_jobs DB INSERT — migration_errors FK 충족.
        var userId = HttpContext.Items["UserId"]?.ToString() ?? tenantId;
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var job = await _jobStore.CreateAsync(tenantId, userId, request.FolderPath, clientIp, userAgent);

        // 작22 (2026-09-09) A3: 1단계만 돌린다. 백그라운드 실행부는 continue 와 **같은 메서드**를 쓴다(복붙 금지).
        RunMigrationInBackground(job, tenantId, request.FolderPath, request.MdbPassword, request.Mode, MdbMigrationPhase.MasterOnly);

        return Accepted(new { jobId = job.JobId, status = "queued", phase = 1 });
    }

    /// <summary>
    /// 작22 (2026-09-09) A3 · 별지 §1-3: 「이어서 가져오기」 — 1단계에서 멈춘(<c>paused</c>) 잡을 <b>같은 jobId</b> 로 2단계(거래)까지 잇는다.
    ///
    /// <para>
    /// 🔴 잡은 <b>DB(<c>migration_jobs</c>)에서</b> 읽는다 — 메모리 <c>_jobs</c> 는 API 가 재시작하면 사라지는데,
    /// 1단계 뒤 한참 지나 이어받는 것이 이 기능의 정상 사용 모습이다(5/16 사장님: 레거시와 병행 쓰는 고객이 많다).
    /// </para>
    /// <para>
    /// 🔴 <b>남의 회사 잡은 잇지 못한다</b> — jobId 가 GUID 라는 것을 보안 경계로 삼지 않는다. DB 의 <c>tenant_id</c> 와
    /// 요청 클레임을 대조해 다르면 거절한다(헌법 #2). 폴더 경로도 요청 값이 아니라 <b>DB 에 적힌 값</b>을 쓴다.
    /// 비번은 다시 받고 어디에도 저장하지 않는다.
    /// </para>
    /// </summary>
    [HttpPost("legacy-mdb/{jobId}/continue")]
    public async Task<IActionResult> ContinueMigrationJob(string jobId, [FromBody] MdbMigrationRequest request)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        if (RejectUnsupportedMode(request) is { } modeError) return modeError;

        var row = await _jobStore.GetFromDbAsync(jobId);
        if (row is null) return NotFound(new { message = "이어서 가져올 자료를 찾을 수 없습니다." });
        if (!string.Equals(row.TenantId, tenantId, StringComparison.Ordinal))
        {
            _logger.LogWarning("[Migrate-Continue] 다른 회사의 가져오기 작업 요청 차단 job={JobId}", ForLog(jobId));
            return Forbid();
        }

        // paused(1단계 완료) · failed(도중 실패) 만 이을 수 있다. 도는 중인 것을 또 걸면 같은 표를 두 번 돌린다.
        if (row.Status is not ("paused" or "failed"))
        {
            return BadRequest(new { message = $"지금은 이어서 가져올 수 없는 상태입니다. (현재: {JobStatusText(row.Status)})" });
        }

        if (string.IsNullOrWhiteSpace(row.SourceFolder))
        {
            return BadRequest(new { message = "처음 가져올 때 쓴 폴더 경로가 남아 있지 않습니다. 처음부터 다시 가져와 주세요." });
        }

        var job = new MigrationJob
        {
            JobId = row.JobId,
            TenantId = row.TenantId,
            Status = "queued",
            StartedAt = row.StartedAt ?? row.CreatedAt,
        };
        RunMigrationInBackground(job, tenantId, row.SourceFolder, request.MdbPassword, request.Mode, MdbMigrationPhase.TransactionsOnly);

        return Accepted(new { jobId = row.JobId, status = "queued", phase = 2 });
    }

    /// <summary>
    /// 작22 (2026-09-09) A3 · 별지 §1-3: 화면이 <b>다시 붙기</b> 위한 조회 — 이 회사의 가장 최근 가져오기 1건(DB 기준) + 표별 진행 상태.
    /// 선행검증 §2-5: 지금까지 화면은 진행 중이던 작업에 재접속하는 길이 <b>0</b> 이었다(새로고침하면 사라졌다).
    /// </summary>
    [HttpGet("legacy-mdb/current")]
    public async Task<IActionResult> GetCurrentMigrationJob()
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var row = await _jobStore.GetLatestByTenantAsync(tenantId);
        if (row is null) return Ok(new { job = (object?)null });

        var checkpoints = await _jobStore.GetCheckpointsAsync(row.JobId);
        return Ok(new
        {
            job = new
            {
                jobId = row.JobId,
                status = row.Status,
                statusText = JobStatusText(row.Status),
                sourceFolder = row.SourceFolder,
                startedAt = row.StartedAt,
                pausedAt = row.PausedAt,
                completedAt = row.CompletedAt,
                createdAt = row.CreatedAt,
            },
            tables = checkpoints.Select(c => new
            {
                tableName = c.TableName,
                status = c.Status,
                rows = c.ProcessedCount,
                startedAt = c.StartedAt,
                completedAt = c.CompletedAt,
                errorMessage = c.LastError,
            }),
        });
    }

    /// <summary>
    /// 작22 (2026-09-09) B1 · 별지 §2: 「찾아보기」 — 자료가 든 이 컴퓨터의 폴더를 서버가 대신 열어 보여준다.
    /// 9/4 사장님 오더 7 <i>"파일경로를 직접 찾을 수 있게"</i>. 브라우저는 PC 경로를 못 읽으므로 서버가 목록을 만든다.
    ///
    /// <para>
    /// 🔴 <see cref="HitPan.API.Security.MainPcOnlyAttribute"/> — 자료가 든 그 컴퓨터에서만(8/11 사장님 지시).
    /// 화면만 감추는 것은 차단이 아니라서 서버에도 같은 판정을 둔다(<c>DataResetController</c>·<c>BackupController</c> 와 같은 규칙).
    /// </para>
    /// <para>
    /// 규칙은 <see cref="MdbFolderBrowsePolicy"/> 순수함수: <c>..</c>·UNC·상대경로 거부 · 시스템 폴더 제외 ·
    /// MDB 3개가 다 있는 폴더에 표시. 접근 거부 폴더는 예외를 삼키지 않고 <c>LogDebug</c> 후 목록에서만 뺀다(헌법 #15).
    /// ⛔ 업로드는 이번에 접었다(사장님 Q2) — 다른 컴퓨터 자료는 이 컴퓨터로 복사한 뒤 고른다.
    /// </para>
    /// </summary>
    [HttpGet("legacy-mdb/browse")]
    [HitPan.API.Security.MainPcOnly]
    public IActionResult BrowseFolders([FromQuery] string? path)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        // 경로가 없으면 드라이브 목록부터 — 사용자가 어디서 시작할지 고르게.
        if (string.IsNullOrWhiteSpace(path))
        {
            return Ok(new
            {
                path = (string?)null,
                parent = (string?)null,
                hasMdb = false,
                folders = Array.Empty<object>(),
                drives = ReadyDriveNames(),
            });
        }

        if (!MdbFolderBrowsePolicy.TryValidatePath(path, out var pathError))
        {
            return BadRequest(new { message = pathError });
        }

        var current = path.Trim();
        if (!Directory.Exists(current))
        {
            return NotFound(new { message = $"폴더가 존재하지 않습니다: {current}" });
        }

        try
        {
            var folders = new List<object>();
            foreach (var dir in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(dir);
                if (MdbFolderBrowsePolicy.IsExcludedFolderName(name) || MdbFolderBrowsePolicy.IsExcludedPath(dir))
                {
                    continue;   // 윈도우·프로그램 폴더는 자료 폴더가 아니다 — 목록만 어지럽힌다.
                }

                bool hasMdb;
                try
                {
                    hasMdb = MdbFolderBrowsePolicy.HasAllMdbFiles(Directory.EnumerateFiles(dir, "*.mdb"));
                }
                catch (UnauthorizedAccessException ex)
                {
                    // 헌법 #15: 삼키지 않는다 — 사유는 남기고 그 폴더만 뺀다(볼 수 없는 폴더를 목록에 두면 눌렀을 때 오류가 난다).
                    _logger.LogDebug(ex, "[Browse] 접근 권한이 없어 목록에서 제외 folder={Folder}", ForLog(dir));
                    continue;
                }
                catch (IOException ex)
                {
                    _logger.LogDebug(ex, "[Browse] 읽을 수 없어 목록에서 제외 folder={Folder}", ForLog(dir));
                    continue;
                }

                folders.Add(new { name, fullPath = dir, hasMdb });
            }

            string? parent = null;
            try
            {
                parent = Directory.GetParent(current)?.FullName;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Browse] 상위 폴더 판정 실패 folder={Folder}", ForLog(current));
            }

            var currentHasMdb = MdbFolderBrowsePolicy.HasAllMdbFiles(Directory.EnumerateFiles(current, "*.mdb"));

            return Ok(new
            {
                path = current,
                parent,
                hasMdb = currentHasMdb,
                folders,
                drives = ReadyDriveNames(),
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "[Browse] 폴더 접근 권한 없음 folder={Folder}", ForLog(current));
            return StatusCode(403, new { message = $"폴더를 열 권한이 없습니다: {current}" });
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "[Browse] 폴더 읽기 실패 folder={Folder}", ForLog(current));
            return BadRequest(new { message = $"폴더를 읽지 못했습니다: {current}" });
        }
    }

    /// <summary>지금 읽을 수 있는 드라이브 이름 목록(<c>C:\</c> 꼴). 준비 안 된 드라이브는 상태를 묻다 예외가 나므로 걸러 낸다.</summary>
    private string[] ReadyDriveNames()
    {
        var names = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.IsReady) names.Add(d.Name);
            }
            catch (IOException ex)
            {
                // 헌법 #15: 빈 catch 금지 — 못 읽는 드라이브는 사유를 남기고 뺀다(네트워크 드라이브 연결 끊김 등).
                _logger.LogDebug(ex, "[Browse] 드라이브 상태 확인 실패 drive={Drive}", ForLog(d.Name));
            }
        }
        return names.ToArray();
    }

    /// <summary>
    /// 작22 (2026-09-09) A2: 아직 못 받는 방식을 거른다.
    /// ⛔ 덮어쓰기(초기화 → 가져오기) 본체는 <b>사장님 Q1 답 후</b>다(선행검증 §2-1: 초기화가 회사 뼈대까지 지우는데 되살리는 코드가 0건).
    /// DTO 필드(<c>Mode</c>·<c>Password</c>·<c>ConfirmText</c>)는 화면·API 계약을 먼저 세우려고 받아 두되, 여기서 분기만 하고 아무 일도 하지 않는다(헌법 #33).
    /// </summary>
    private IActionResult? RejectUnsupportedMode(MdbMigrationRequest request)
    {
        if (!MdbMigrationModes.IsValid(request.Mode))
        {
            return BadRequest(new { message = "가져오기 방식을 알 수 없습니다. 화면에서 다시 선택해 주세요." });
        }
        if (MdbMigrationModes.IsOverwrite(request.Mode))
        {
            return BadRequest(new { message = "덮어쓰기는 준비 중입니다." });
        }
        return null;
    }

    /// <summary>잡 상태를 화면에 그대로 보여줄 수 있는 말로 (개발용어 0).</summary>
    private static string JobStatusText(string? status) => status switch
    {
        "pending" or "queued" => "준비 중",
        "preview" => "미리보기",
        "running" => "가져오는 중",
        "paused" => "1단계까지 완료",
        "completed" => "완료",
        "failed" => "실패",
        "canceled" => "중단됨",
        _ => "알 수 없음",
    };

    /// <summary>
    /// 작22 (2026-09-09) A3: 백그라운드 가져오기 실행 — <c>/start</c>(1단계)와 <c>continue</c>(2단계)가 <b>같은 코드</b>를 탄다.
    /// 종전 <c>/start</c> 안에 있던 <c>Task.Run</c> 블록을 그대로 옮긴 것이다(복붙 금지 · 헌법 #1 — 바뀐 것은 단계 분기뿐).
    /// </summary>
    private void RunMigrationInBackground(
        MigrationJob job, string tenantId, string folderPath, string? mdbPassword, string? mode, MdbMigrationPhase phase)
    {
        // API 재시작 뒤 continue 로 들어온 잡도 메모리에 자리를 만든다(status 폴링·SignalR 재접속용). 이미 있으면 그대로 둔다.
        _jobStore.Restore(job);

        // 백그라운드 실행 — HttpContext 끊김 무관, 새 스코프로 서비스 해결.
        _ = Task.Run(async () =>
        {
            // 백그라운드 안의 모든 DB 작업은 새 scope의 connection 사용 (HttpContext scoped 무관).
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<MdbMigrationService>();
            var storeBg = scope.ServiceProvider.GetRequiredService<MigrationJobStore>();
            try
            {
                _jobStore.Update(job.JobId, j => { j.Status = "running"; j.CurrentStep = "초기화"; });
                await storeBg.SyncToDbAsync(job.JobId, "running");

                _jobStore.Update(job.JobId, j => j.CurrentStep = "MDB 읽기 + Bulk INSERT 진행 중...");
                // P0 #5 (2026-05-14): jobId — migration_errors AES 저장용.
                // P0 #6 (2026-05-14): 진행 콜백 — UI Sticky/카드 가시화.
                var jobIdLocal = job.JobId;
                var result = await svc.MigrateAsync(
                    folderPath, tenantId, mdbPassword, jobIdLocal,
                    progressCallback: (table, status, rows, elapsedMs, err) =>
                    {
                        // 정공법 CODE-01 (2026-05-14): in-memory 봉합(_jobStore) + SignalR push(_progress) 병행.
                        // _jobStore는 폴링 fallback / DB sync 용도로 유지(dead code 아님).
                        // _progress는 정공법 — 서버 push 즉시 Blazor 화면 갱신.
                        _jobStore.Update(jobIdLocal, j =>
                        {
                            var prog = j.TableProgress.GetOrAdd(table, _ => new MigrationTableProgress());
                            prog.Status = status;
                            prog.Rows = rows;
                            prog.ElapsedMs = elapsedMs;
                            prog.ErrorMessage = err;
                            if (status == "running") prog.StartedAt ??= DateTime.UtcNow;
                            if (status is "completed" or "failed") prog.FinishedAt = DateTime.UtcNow;
                            j.CurrentStep = status == "running" ? $"진행 중: {table}" : $"{table} {status}";
                        });
                        // SignalR push (fire-and-forget — 백그라운드 잡 흐름 막지 않음).
                        _ = _progress.UpdateAsync(jobIdLocal, table, status, rows, elapsedMs, err);
                    },
                    mode, phase,
                    CancellationToken.None);

                // 작22 (2026-09-09) A3: 1단계만 돈 잡은 「끝」이 아니라 「멈춤」이다 — 2단계를 이어서 가져올 수 있게 paused 로 둔다.
                var isPhase1 = phase == MdbMigrationPhase.MasterOnly;
                _jobStore.Update(job.JobId, j =>
                {
                    j.Status = isPhase1 ? "paused" : "completed";
                    j.CurrentStep = isPhase1 ? "1단계(기초자료) 완료" : "완료";
                    j.FinishedAt = isPhase1 ? null : DateTime.UtcNow;
                    j.Result = new MigrationJobResult
                    {
                        Partners = result.Partners, Items = result.Items, BomHeaders = result.BomHeaders,
                        Employees = result.Employees, SalesOrders = result.SalesOrders, PurchaseOrders = result.PurchaseOrders,
                        StockLedger = result.StockLedger, Collections = result.Collections, Payments = result.Payments, Cashbook = result.Cashbook,
                        Expenses = result.Expenses, PurchaseOrdersFromIU = result.PurchaseOrdersFromIU,
                        SalesOrdersFromIO = result.SalesOrdersFromIO, TaxInvoices = result.TaxInvoices,
                        Bills = result.Bills, CardPayments = result.CardPayments, BankTransactions = result.BankTransactions
                    };
                });
                if (isPhase1)
                {
                    // 5/16 사장님 UX: 여기서 화면에 "1단계자료 이관완료…" 를 띄우고 사람이 정한다. 잡은 살아 있다.
                    await storeBg.SyncToDbAsync(job.JobId, "paused");
                    await _progress.PhaseCompletedAsync(job.JobId, 1);
                }
                else
                {
                    await storeBg.SyncToDbAsync(job.JobId, "completed", DateTime.UtcNow);
                    await _progress.CompleteJobAsync(job.JobId, "completed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Migrate-Job] {JobId} 실패", job.JobId);
                _jobStore.Update(job.JobId, j =>
                {
                    j.Status = "failed";
                    j.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
                    j.FinishedAt = DateTime.UtcNow;
                });
                await storeBg.SyncToDbAsync(job.JobId, "failed", DateTime.UtcNow);
                await _progress.CompleteJobAsync(job.JobId, "failed", ex.Message);
            }
        });
    }

    /// <summary>
    /// 마이그 잡 진행 상태 조회 — Razor가 2초마다 폴링.
    /// </summary>
    [HttpGet("legacy-mdb/status/{jobId}")]
    public IActionResult GetMigrationJobStatus(string jobId)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (string.IsNullOrEmpty(tenantId)) return Forbid();

        var job = _jobStore.Get(jobId);
        if (job is null) return NotFound(new { message = "잡 ID를 찾을 수 없습니다." });
        if (job.TenantId != tenantId) return Forbid();

        // P0 #6 (2026-05-14): tableProgress 추가 — UI Sticky/카드 가시화.
        return Ok(new
        {
            jobId = job.JobId,
            status = job.Status,
            currentStep = job.CurrentStep,
            startedAt = job.StartedAt,
            finishedAt = job.FinishedAt,
            result = job.Result,
            errorMessage = job.ErrorMessage,
            elapsedSeconds = (int)((job.FinishedAt ?? DateTime.UtcNow) - job.StartedAt).TotalSeconds,
            tableProgress = job.TableProgress.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    status = kv.Value.Status,
                    rows = kv.Value.Rows,
                    elapsedMs = kv.Value.ElapsedMs,
                    errorMessage = kv.Value.ErrorMessage,
                })
        });
    }
}

/// <summary>
/// MDB 마이그레이션 요청 DTO
/// </summary>
public record MdbMigrationRequest
{
    /// <summary>
    /// 레거시 MDB 파일이 위치한 폴더 경로
    /// </summary>
    public string FolderPath { get; init; } = string.Empty;

    /// <summary>
    /// MDB 파일 비밀번호 (선택사항, 핫픽스 2026-05-13).
    /// 레거시 히트판 MDB는 비번이 걸려있는 경우가 있다 (예: 7618968).
    /// 비번이 없으면 null 또는 빈 문자열.
    /// </summary>
    public string? MdbPassword { get; init; }

    /// <summary>
    /// 작22 (2026-09-09) A2 · 9/4 사장님 오더 6 <i>"마이그레이션 옵션이 두개가 되어야 함. 1덮어쓰기, 2병합"</i>.
    /// <c>"merge"</c>(기본) = 있는 자료는 두고 <b>빈칸만</b> 채운다 · <c>"overwrite"</c> = 기존 자료를 지우고 새로 가져온다.
    /// ⛔ 덮어쓰기 본체는 사장님 <b>Q1</b> 답 후다 — 지금은 요청을 받으면 400 으로 돌려보낸다(<c>RejectUnsupportedMode</c> · 헌법 #33).
    /// </summary>
    public string Mode { get; init; } = MdbMigrationModes.Merge;

    /// <summary>
    /// 덮어쓰기 확인용 대표계정 비밀번호 (초기화 화면과 같은 2단 확인 · 별지 §1-1).
    /// ⛔ Q1 답 전이라 <b>받기만 하고 쓰지 않는다</b> — 어디에도 저장하지 않는다.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// 덮어쓰기 확인 문구(초기화 화면의 "초기화" 자리). ⛔ Q1 답 전이라 받기만 한다.
    /// </summary>
    public string? ConfirmText { get; init; }
}
