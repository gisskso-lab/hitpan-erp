using System.Security.Cryptography;
using System.Text;
using Dapper;
using HitPan.Backoffice.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace HitPan.Backoffice.API.Controllers;

// 설치마법사 EXE 부트스트랩 API (브라운킴 PM 2026-06-09, 사장님 결재 Plan 정합)
//
// 흐름:
//   1) 고객이 EXE 다운로드 → 더블클릭 실행
//   2) 마법사 화면에서 시리얼 키 입력
//   3) EXE가 POST /api/installer/bootstrap 호출
//   4) 백오피스 검증 → 회사정보 + 도메인 + 터널 토큰 응답
//   5) EXE가 응답대로 cloudflared 등록 + DB 셋업 + 자동 시작
//
// 헌법 정합:
//   #18·#22 — 시리얼 = 백오피스↔ERP 포링키. 평문 IO 1회만 (응답 직후 EXE 파기 의무)
//   #28·#30 — 고객 손 0번. 시리얼 1개만 입력하면 끝
//   #29 — 인프라 토큰은 백오피스가 발급 (EXE에 사전 저장하지 않음)
//   #33 — 모든 응답에 source 명시 ("installer-bootstrap")
//   #35 — 시리얼 = 포링키. 백오피스가 평문 관리, EXE는 평문 받아 사용 후 폐기
[ApiController]
[Route("api/installer")]
[AllowAnonymous]
public class InstallerBootstrapController : ControllerBase
{
    // ══════════════════════════════════════════════════════════════════════════
    // 🔴 20261007작12 1차수 B3-1 — ⓑ 시도 제한 · ⓒ 응답 최소화 (추가만 · 헌법 #1)
    //   설계 정본: docs/설계/백오피스/20261007_설계_작12_1차수_설치부트스트랩_선검증.md §4·§5
    //   사장님 결재 2026-10-07 §10: ② 잠금 키 단위 = 지문만 · ③ 60분 자동 해제 신설
    //   🔴 발명 0 — 두 숫자는 SerialVerifyController.cs:26-27 의 기존 값 그대로다.
    //   🔴 DDL 0 — serial_verify_locks·serial_verify_attempts 를 기존 모양 그대로 쓴다(설계 §8 R-1).
    //      ⇒ 되돌림은 이 커밋 revert 하나뿐. 마이그 되돌림이 없다.
    // ══════════════════════════════════════════════════════════════════════════
    private const int MaxFailedAttempts = 5;
    private const int FailWindowMinutes = 60;

    /// <summary>
    /// ⓒ 단계 2(워치독 터널 자가복구) 표시값 — <b>요청</b> 선택 필드의 유일한 유의미 값(설계 §5-2).
    /// <para>🔴 생략이 기본이고 생략 = 지금과 똑같은 전체 응답이다. 거꾸로 잡으면 고객 PC 의
    /// 구 설치본·구 워치독이 전부 깨진다(G-8 이 이걸 문다).</para>
    /// </summary>
    private const string PurposeTunnelRecovery = "tunnel-recovery";

    // 🔴 serial_verify_attempts.result = varchar(20) ⇒ 값은 20자 이내 (출하 DDL 00_backoffice_core.sql:470).
    //   같은 표를 브라우저 시리얼 검증(SerialVerifyController: "locked"·"mismatch"·"success")이 공유한다.
    //   구분 없이 남기면 추적이 섞이므로 installer- 접두어로 가른다.
    private const string ResultLocked = "installer-locked";      // 16자
    private const string ResultMismatch = "installer-mismatch";  // 18자

    private readonly IConfiguration _config;
    private readonly ICloudflareDomainService _cfDomain;
    private readonly ILogger<InstallerBootstrapController> _logger;

    public InstallerBootstrapController(
        IConfiguration config,
        ICloudflareDomainService cfDomain,
        ILogger<InstallerBootstrapController> logger)
    {
        _config = config;
        _cfDomain = cfDomain;
        _logger = logger;
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.LicenseKey))
            return BadRequest(new { success = false, message = "시리얼 키를 입력해주세요." });
        if (string.IsNullOrWhiteSpace(req.MachineFingerprint))
            return BadRequest(new { success = false, message = "PC 정보가 누락되었습니다." });

        try
        {
            await using var db = await OpenAsync(ct);

            var pepper = _config["License:Pepper"] ?? throw new InvalidOperationException("License:Pepper 미설정");
            var normalizedKey = req.LicenseKey.Trim().ToUpperInvariant().Replace(" ", "");
            var licHash = ComputeHmacSha256(normalizedKey, pepper);

            // 🆕 작12 B3-1 ⓑ 재료 — 지문 해시·접속 IP. 잠금 **판독은 키 조회 뒤**에 한다(아래 ⑤).
            //   🔴 잠금 키 단위는 client_fingerprint 해시 **하나뿐**이다 — serial_verify_locks 의
            //      UNIQUE KEY uk_fingerprint 가 그것이고, submitted_hash·client_ip 에는 인덱스가 없다
            //      (출하 DDL :471-473) ⇒ 키·IP 단위 가산은 DDL 추가를 부르므로 2차수(사장님 결재 ②).
            var fpHash = ComputeHmacSha256(req.MachineFingerprint, pepper);
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            // 시리얼 = 포링키. tenants에서 평문 비교 가능하지만 HMAC 비교가 보안 정합.
            // 데이터 흐름도 정정 (사장님 결재 2026-06-18 "길 B"): 백오피스는 사업자번호·대표자명 평문을
            //   보유하지 않으므로 부트스트랩 응답에서도 biz_no·ceo_name 제거. 회사명·도메인·연락처만 공급.
            //   사업자번호·대표자명은 ERP 설치화면(/setup/license)에서 사용자 입력 → ERP 로컬에만 저장.
            var tenant = await db.QueryFirstOrDefaultAsync<TenantRow>(@"
                SELECT CAST(t.tenant_id AS CHAR) AS TenantId,
                       t.tenant_code AS TenantCode,
                       t.domain_alias AS DomainAlias,
                       t.company_name AS CompanyName,
                       t.tel AS Tel,
                       t.status AS Status,
                       ls.email AS Email,
                       ls.plan_type AS PlanType
                FROM tenants t
                -- Z2 (20261006작8 갈래 가): 회사명 글자 조인 → tenant_id 키 조인.
                --   키로 묶인 가입서를 먼저 집고, 키가 없는 옛 행(tenant_id NULL)만
                --   기존 글자 조인으로 읽기 폴백한다(#20 — 끊김 금지). 같은 회사명 2건이어도
                --   키 행이 있으면 글자 행은 쳐다보지 않는다(동명 회사 엇갈림 차단 · C-1).
                LEFT JOIN landing_signups ls ON ls.signup_id = (
                    SELECT ls2.signup_id FROM landing_signups ls2
                    WHERE ls2.tenant_id = t.tenant_id
                       OR (ls2.tenant_id IS NULL
                           AND ls2.company_name = t.company_name
                           AND NOT EXISTS (SELECT 1 FROM landing_signups k WHERE k.tenant_id = t.tenant_id))
                    ORDER BY (ls2.tenant_id = t.tenant_id) DESC, ls2.submitted_at DESC
                    LIMIT 1
                )
                WHERE t.license_key_hash = @Hash AND t.status = 'active'
                LIMIT 1",
                new { Hash = licHash });

            if (tenant is null)
            {
                // ══════════════════════════════════════════════════════════════
                // ⑤ 🆕 작12 B3-1 ⓑ — 잠금은 **키를 못 찾은 뒤에만** 본다
                //
                // 🔴🔴 P0-1 처방 (사장님 결재 2026-10-07 · [4] 검증팀장 반증):
                //   「유효한 키는 잠금과 무관하게 통과시킨다.」
                //   1차 구현은 잠금 판독을 키 조회 **앞**에 뒀다. 그러면 지문이 한 번 잠긴 PC 는
                //   **유효 키로도 423** 이 되어 터널 자가복구·재설치가 60분 멈춘다(#27·#28·#30).
                //   반증 사실: 워치독은 60분에 **최대 10회** 부트스트랩을 부를 수 있다 —
                //     WS28F_CoolDown.AllowRecovery 가 **키별 독립 큐**(한도 5)이고
                //     Worker.cs:448(ServiceReinstall) · Worker.cs:1051(PostReboot:ServiceReinstall)
                //     두 키가 각각 깨운다. 큐는 in-memory 라 서비스 재시작마다 리셋 ⇒ 10 은 **하한**.
                //   게다가 조회 조건이 t.status='active'(:88) 라 **구독 정지·미승인 고객은 유효 키로도 401**
                //     ⇒ 설계 §4-3⑤「워치독은 실패 경로에 안 들어간다」는 **반증됐다.**
                //   보너스: 「남의 지문을 보내 그 PC 설치를 60분 막는」 공격면(검증 S-2)도 함께 사라진다 —
                //     그 공격으로는 **틀린 키 시도만** 막힌다.
                //   무차별 대입은 항상 **틀린 키**를 내므로 막는 힘은 그대로다.
                // ══════════════════════════════════════════════════════════════
                var lockState = await db.QueryFirstOrDefaultAsync<BootstrapLockRow>(@"
                    SELECT failed_count AS FailedCount,
                           is_locked AS IsLocked,
                           CASE WHEN last_failed_at >= DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Window MINUTE)
                                THEN 1 ELSE 0 END AS WithinWindow
                    FROM serial_verify_locks
                    WHERE client_fingerprint = @Fp",
                    new { Fp = fpHash, Window = FailWindowMinutes });

                // 🔴 60분 창 밖이면 is_locked=1 이어도 통과시킨다(사장님 결재 ③ — 자동 해제).
                //   기존 기계(SerialVerifyController :66-76)는 영구 잠금 + 본사 수동 해제다.
                //   그걸 그대로 연결하면 오타 5번으로 재설치가 영구 차단된다. G-4b 가 이 자리를 문다.
                if (lockState is not null && lockState.IsLocked == 1 && lockState.WithinWindow == 1)
                {
                    await LogBootstrapAttempt(db, null, licHash, fpHash, clientIp, ResultLocked);
                    _logger.LogWarning(
                        "[InstallerBootstrap] 틀린 키 + 지문 단위 잠금으로 거절 count={Cnt} (60분 뒤 자동 해제)",
                        lockState.FailedCount);
                    return StatusCode(423, new
                    {
                        success = false,
                        locked = true,
                        message = "시리얼 입력 5회 실패로 잠시 중지되었습니다. 1시간 뒤 다시 시도해주세요."
                    });
                }

                //   🔴 가산은 **키 불일치에서만** 돈다. 본사 장애·DB 오류·CF 실패는 바깥 catch → 500 이라
                //      이 자리에 닿지 않는다 ⇒ 본사가 아파도 고객이 잠기지 않는다(설계 §4-3 1번 · G-6).
                await IncrementBootstrapFailedCount(db, fpHash);
                await LogBootstrapAttempt(db, null, licHash, fpHash, clientIp, ResultMismatch);

                var afterFail = await db.QueryFirstOrDefaultAsync<BootstrapLockRow>(@"
                    SELECT failed_count AS FailedCount, is_locked AS IsLocked, 1 AS WithinWindow
                    FROM serial_verify_locks WHERE client_fingerprint = @Fp",
                    new { Fp = fpHash });

                if (afterFail is not null && afterFail.IsLocked == 1)
                {
                    _logger.LogWarning(
                        "[InstallerBootstrap] 5회 실패로 지문 단위 잠금 count={Cnt} (60분 뒤 자동 해제)",
                        afterFail.FailedCount);
                    return StatusCode(423, new
                    {
                        success = false,
                        locked = true,
                        message = "시리얼 입력 5회 실패로 잠시 중지되었습니다. 1시간 뒤 다시 시도해주세요."
                    });
                }

                // 🔴 1~4회째 응답은 **문구·상태코드 그대로 둔다** — 워치독이 401 을
                //   「시리얼 무효/미승인」으로 해석해 진단 로그를 가른다(TunnelTokenRecovery.cs:102-108).
                //   문구 oracle 봉합은 B4(3차수)와 같은 판정이다(설계 §7).
                _logger.LogWarning("[InstallerBootstrap] invalid serial machine={Fp}", req.MachineFingerprint);
                return Unauthorized(new
                {
                    success = false,
                    message = "올바르지 않은 시리얼이거나 승인되지 않은 계정입니다."
                });
            }

            // 사장님 결재 2026-06-09 — 도메인 별칭(domain_alias) 저장된 영역 우선 저장하기.
            // 저장하지 못한 영역(레거시 가입자)이면 텐넌트 코드 폴백, 단 외부 메일/UI 표시는 절대 저장하지 않음 (헌법 #22 정합).
            var subdomain = !string.IsNullOrWhiteSpace(tenant.DomainAlias)
                ? tenant.DomainAlias!
                : tenant.TenantCode.ToLowerInvariant().Replace("-", "");
            var primaryDomain = $"{subdomain}.hitpan.kr";
            var apiDomain = $"api-{subdomain}.hitpan.kr";

            // Cloudflare 환경변수 설정되어 있으면 DNS + 터널 자동 발급 (사장님 결재 2026-06-09 Day 5)
            string? tunnelToken = null;
            string? tunnelDomain = null;
            string? tunnelId = null;
            if (_cfDomain.IsConfigured)
            {
                try
                {
                    var domainResult = await _cfDomain.IssueAsync(tenant.TenantId, tenant.TenantCode, tenant.DomainAlias, ct);
                    tunnelDomain = domainResult.Domain;

                    // cloudflared 터널 자동 발급 (실패해도 DNS만으로 동작 가능 — 부트스트랩은 성공으로 처리)
                    try
                    {
                        var tunnelResult = await _cfDomain.IssueTunnelAsync(tenant.TenantId, tenant.TenantCode, ct);
                        tunnelToken = tunnelResult.TunnelToken;
                        tunnelId = tunnelResult.TunnelId;

                        // 봉합 2026-06-16 (사고: test000 1033 재발):
                        //   DNS Idempotent 봉합 후 기존 DNS record가 잘못된 터널 가리키는 사고.
                        //   터널 새로 발급 → DNS CNAME content를 새 tunnelId.cfargotunnel.com 로 PATCH.
                        try
                        {
                            await _cfDomain.UpdateDnsTunnelTargetAsync(domainResult.RecordId, domainResult.Domain, tunnelId, ct);
                        }
                        catch (Exception pex)
                        {
                            _logger.LogWarning(pex, "[InstallerBootstrap] DNS PATCH 실패 tenant={Tid} (1033 사고 가능성)", tenant.TenantId);
                        }

                        // 봉합 2026-06-17 (v1.2.12 P0-B):
                        //   터널 ingress 라우팅 PUT — hostname → http://localhost:5257 (HitPan.API)
                        //   본 PUT이 없으면 cloudflared가 트래픽 어디로 보낼지 모름 → 1033 100% 재발
                        try
                        {
                            await _cfDomain.UpdateTunnelIngressAsync(tunnelId, primaryDomain, "http://localhost:5257", ct);
                        }
                        catch (Exception iex)
                        {
                            _logger.LogWarning(iex, "[InstallerBootstrap] 터널 ingress PUT 실패 tenant={Tid} (1033 사고 재발 가능성)", tenant.TenantId);
                        }
                    }
                    catch (Exception tex)
                    {
                        _logger.LogWarning(tex, "[InstallerBootstrap] 터널 발급 실패 (DNS만 발급, 수동 터널 등록 폴백) tenant={Tid}", tenant.TenantId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[InstallerBootstrap] CF 발급 실패 (수동 발급 폴백) tenant={Tid}", tenant.TenantId);
                }
            }

            // 부트스트랩 토큰 — EXE가 향후 자동 업데이트·재인증에 사용 (24시간 유효)
            var bootstrapToken = GenerateBootstrapToken();
            var bootstrapTokenHash = ComputeHmacSha256(bootstrapToken, pepper);

            // 봉합 (재설치 P0 2차 벽, 사장님 결재 2026-07-06, 작지서 20260706작2, 4인회의+보안상무 결재):
            //   부모계정 생성용 서명 토큰은 백오피스(LandingPublicController.ClaimLicense)가
            //   HITPAN_BOOTSTRAP_TOKEN_KEY 로 HMAC 서명하고, ERP(CompanyBootstrapController.VerifyBootstrapToken)가
            //   같은 키로 검증한다. 설치 EXE 가 이 키를 db.conf 에 기록하지 않아 ERP 가 DEV 폴백값으로 검증 →
            //   서명 불일치 401 → 모든 신규/재설치 부모계정 생성 차단(헌법 #20). 여기서 키를 내려 EXE 가 db.conf 에
            //   기록하면 로컬 검증 정합(헌법 #30 본사 의존 0 유지). LandingPublicController.ClaimLicense 와 동일 취득.
            //   [베타=전 고객 공용키(보안상무 결재: create-parent 앞단 게이트가 타 고객사 실피해 차단, 라이선스 우회 등급).
            //    정식 출시 전 = HMAC(마스터키, tenant_id) 테넌트별 파생키로 전환 필수 + jti 재사용방지·만료단축 동시.]
            var bootstrapTokenKey = Environment.GetEnvironmentVariable("HITPAN_BOOTSTRAP_TOKEN_KEY")
                                    ?? _config["Bootstrap:TokenKey"]
                                    ?? throw new InvalidOperationException(
                                        "HITPAN_BOOTSTRAP_TOKEN_KEY 미설정 — 부트스트랩 서명키 없이 설치 응답 불가(DEV 폴백 금지, 보안상무 결재 조건1)");

            // ⑧-b 🆕 작12 B3-1 ⓑ — 성공이 실패 카운터를 0 으로 리셋한다 (설계 §1 ⑧-b · §4-3 3번)
            //   🔴 P0-1 처방의 2단이기도 하다 — 잠긴 지문도 **유효 키면 여기까지 와서** 잠금이 풀린다.
            //      is_locked 도 함께 0 으로 내린다(아래 UPDATE). G-10 이 그 자리를 동작으로 문다.
            //   기존 serial/verify 성공 경로(SerialVerifyController.cs:133-137)와 같은 식이다.
            //   ⇒ 이전 오타가 다음 설치로 넘어가지 않는다. G-5 가 이 자리를 문다.
            await db.ExecuteAsync(@"
                UPDATE serial_verify_locks
                SET failed_count = 0, is_locked = 0
                WHERE client_fingerprint = @Fp",
                new { Fp = fpHash });

            // 기기 부트스트랩 로그 (감사 추적)
            // 봉합 v1.2.5 (2026-06-11): 컬럼 영역 정정 license_key_hash -> submitted_hash
            await db.ExecuteAsync(@"
                INSERT INTO serial_verify_attempts
                    (tenant_id, submitted_hash, client_fingerprint, client_ip, result, attempted_at)
                VALUES
                    (@TenantId, @LicHash, @Fp, @Ip, 'installer-bootstrap', UTC_TIMESTAMP(6))",
                new
                {
                    TenantId = tenant.TenantId,
                    LicHash = licHash,
                    Fp = ComputeHmacSha256(req.MachineFingerprint, pepper),
                    Ip = HttpContext.Connection.RemoteIpAddress?.ToString()
                });

            _logger.LogInformation("[InstallerBootstrap] success tenant={Tid} code={Code} domain={Dom} cf={Cf}",
                tenant.TenantId, tenant.TenantCode, primaryDomain, _cfDomain.IsConfigured);

            // ──────────────────────────────────────────────────────────────────
            // ⑩ 🆕 작12 B3-1 ⓒ — 단계 2(워치독 터널 자가복구) 전용 축소 응답 (설계 §5)
            //
            //   🔴 **빼기만 한다. 항목 이름을 더하지 않는다.** 구 설치본의
            //      ExtractJsonValue(RawResponse,'tokenKey') 는 평면 이름 검색이라
            //      이름이 겹치는 새 항목을 더하면 엉뚱한 값을 집는다(설계 §5-3 · HitPan-Universal.iss:612-621).
            //
            //   무엇을 빼나 (코드 실측 전수 grep · 설계 §5-1):
            //     · tenant 묶음 6개(tenant·tenantCode·companyName·tel·email·planType) — 워치독 참조 0건
            //     · bootstrap.token · bootstrap.tokenKey — 워치독 참조 0건 · **비밀값성 4항목 중 2개**
            //   무엇을 못 빼나 (정직하게 적는다):
            //     · domain.tunnelToken · domain.tunnelId 는 **두 단계 모두** 필요하다
            //       (설치 iss:615·618 / 워치독 :131·:136) ⇒ 비밀값성 4항목 중 2개만 줄어든다.
            //   🔴 교정 2026-10-07 ([4] 검증 지적): 워치독이 **실제로 읽는 것은 이 둘뿐**이다.
            //      설계 §5-1 은 domain.tunnelTokenIssued 도 「필요」로 적었는데 그건 **주석 한 줄**이고
            //      TryGetProperty 호출이 0건이다(TunnelTokenRecovery.cs:128 주석). 주석은 코드가 아니다.
            //      그래서 그 항목은 「빼도 안 깨지지만 변경 최소로 그대로 둔다」가 정확한 서술이다.
            //
            //   ⚠️ 단계적 발효 — 고객 PC 의 **구 워치독**은 이 필드를 안 보내므로 전체 응답을 받는다(안 깨진다).
            //      자동 업데이트로 새 워치독이 깔린 PC 부터 축소가 발효된다. 「게시 직후 전 고객 축소」가 아니다.
            if (string.Equals(req.Purpose, PurposeTunnelRecovery, StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new
                {
                    success = true,
                    source = "installer-bootstrap",
                    domain = new
                    {
                        primary = tunnelDomain ?? primaryDomain,
                        api = apiDomain,
                        tunnelTokenIssued = !string.IsNullOrEmpty(tunnelToken),
                        tunnelToken = tunnelToken,
                        tunnelId = tunnelId
                    },
                    bootstrap = new
                    {
                        // token·tokenKey 는 싣지 않는다(위 주석). 남은 둘은 비밀값성이 아니고 변경 최소 원칙.
                        expiresInSec = 86400,
                        backofficeUrl = "https://back.hitpan.kr"
                    }
                });
            }

            // 🔴 기본값 = 지금과 똑같은 전체 응답. 선택 필드 **생략이 기본**이다(G-8 하위호환).
            return Ok(new
            {
                success = true,
                source = "installer-bootstrap",
                tenant = new
                {
                    // 길 B (사장님 결재 2026-06-18): bizNo·ceoName 응답 제거 — 백오피스 평문 미보유.
                    //   ERP는 설치화면 입력으로 사업자번호·대표자명을 로컬에만 저장.
                    tenantCode = tenant.TenantCode,
                    companyName = tenant.CompanyName,
                    tel = tenant.Tel,
                    email = tenant.Email,
                    planType = tenant.PlanType
                },
                domain = new
                {
                    primary = tunnelDomain ?? primaryDomain,
                    api = apiDomain,
                    tunnelTokenIssued = !string.IsNullOrEmpty(tunnelToken),
                    tunnelToken = tunnelToken,
                    tunnelId = tunnelId
                },
                bootstrap = new
                {
                    token = bootstrapToken,
                    expiresInSec = 86400,
                    backofficeUrl = "https://back.hitpan.kr",
                    // 재설치 P0 2차 벽 봉합 (2026-07-06): EXE 가 db.conf 에 HITPAN_BOOTSTRAP_TOKEN_KEY 로 기록.
                    //   ERP VerifyBootstrapToken 이 로컬에서 이 키로 서명 검증(본사 통신 0). 응답은 HTTPS(back.hitpan.kr) 전제.
                    tokenKey = bootstrapTokenKey
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[InstallerBootstrap] 처리 실패");
            return StatusCode(500, new { success = false, message = "부트스트랩 처리 중 오류가 발생했습니다." });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 🆕 작12 B3-1 ⓑ 보조 — 기존 표 2개를 **기존 모양 그대로** 쓴다(DDL 0 · 설계 §8 R-1)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 실패 카운터 UPSERT — 창(60분) 밖이면 1 로 되돌리고 잠금도 푼다 ⇒ 오래된 실패가 쌓여서 잠그지 않는다.
    ///
    /// <para>🔴🔴 <b>대입 순서가 판정이다 — 손대지 마라.</b> (2026-10-07 작12 B3-1 실측으로 발견)
    /// MariaDB 의 <c>ON DUPLICATE KEY UPDATE</c> 는 대입을 <b>위에서 아래로</b> 평가하고,
    /// 뒤 줄이 앞 줄에서 <b>이미 바뀐 값</b>을 본다. 그래서 <c>failed_count</c> 를 먼저 올려 두고
    /// 아래에서 <c>failed_count + 1 &gt;= @Max</c> 를 보면 <b>한 번 일찍</b> 잠긴다.</para>
    ///
    /// <para><b>실측(hitpan_trgtest · 2026-10-07)</b>: 설계가 「기존 식 그대로」라 한
    /// <c>SerialVerifyController.cs:178-202</c> 의 순서를 그대로 복사했더니
    /// <b>4회째에 <c>failed_count=4, is_locked=1</c></b> 이 됐다 — 결재된 수치는 <b>5회</b>다
    /// (사장님 헌법 「5회 실패 시 잠금」 · <c>MaxFailedAttempts=5</c>).
    /// 같은 이유로 <c>locked_at</c> 이 <c>is_locked</c> 뒤에 있으면 <c>is_locked = 0</c> 조건이
    /// 영원히 거짓이 되어 <b>잠긴 시각이 NULL 로 남는다</b>.</para>
    ///
    /// <para>⇒ 옛 값을 보는 줄을 <b>전부 위로</b> 올렸다: locked_at → is_locked → failed_count →
    /// first_failed_at → last_failed_at. 숫자(5·60)는 하나도 바꾸지 않았다.</para>
    ///
    /// <para>⚠️ <b>같은 결함이 <c>SerialVerifyController</c> 에 그대로 있다</b>(브라우저 시리얼 검증).
    /// 이번 차수 범위가 아니라 <b>안 고쳤다</b> — 개발명세서 §6 에 올려 뒀다.
    /// 그 문은 레포 전수 호출자 0건이라 오늘 아무도 안 쓴다 ⇒ 「기존에 검증된 기계」가 아니다.</para>
    /// </summary>
    private static Task IncrementBootstrapFailedCount(MySqlConnection db, string fingerprint) =>
        db.ExecuteAsync(@"
            INSERT INTO serial_verify_locks (client_fingerprint, failed_count, first_failed_at, last_failed_at)
            VALUES (@Fp, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                locked_at = CASE
                    WHEN last_failed_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Window MINUTE) THEN NULL
                    WHEN failed_count + 1 >= @Max AND is_locked = 0 THEN UTC_TIMESTAMP()
                    ELSE locked_at
                END,
                is_locked = CASE
                    WHEN last_failed_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Window MINUTE) THEN 0
                    WHEN failed_count + 1 >= @Max THEN 1
                    ELSE is_locked
                END,
                failed_count = CASE
                    WHEN last_failed_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Window MINUTE) THEN 1
                    ELSE failed_count + 1
                END,
                first_failed_at = CASE
                    WHEN last_failed_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @Window MINUTE) THEN UTC_TIMESTAMP(6)
                    ELSE first_failed_at
                END,
                last_failed_at = UTC_TIMESTAMP(6)",
            new { Fp = fingerprint, Window = FailWindowMinutes, Max = MaxFailedAttempts });

    /// <summary>
    /// 시도 기록 — INSERT ONLY(#3). <c>result</c> 는 <b>varchar(20)</b> 이므로 20자 이내 값만 넣는다.
    /// 기존 성공 기록(result='installer-bootstrap')은 한 줄도 건드리지 않았다.
    /// </summary>
    private static Task LogBootstrapAttempt(MySqlConnection db, string? tenantId, string submittedHash,
        string fingerprint, string? clientIp, string result) =>
        db.ExecuteAsync(@"
            INSERT INTO serial_verify_attempts
                (tenant_id, submitted_hash, client_fingerprint, client_ip, result, attempted_at)
            VALUES
                (@TenantId, @Hash, @Fp, @Ip, @Result, UTC_TIMESTAMP(6))",
            new { TenantId = tenantId, Hash = submittedHash, Fp = fingerprint, Ip = clientIp, Result = result });

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cs = _config.GetConnectionString("BackofficeDb")
                 ?? _config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정");
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }

    private static string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GenerateBootstrapToken()
    {
        Span<byte> buf = stackalloc byte[32];
        RandomNumberGenerator.Fill(buf);
        return "bst_" + Convert.ToBase64String(buf).Replace("/", "_").Replace("+", "-").TrimEnd('=');
    }

    public class BootstrapRequest
    {
        public string LicenseKey { get; set; } = "";
        public string MachineFingerprint { get; set; } = "";
        public string? Hostname { get; set; }
        public string? OsVersion { get; set; }
        public string? InstallerVersion { get; set; }

        /// <summary>
        /// 🆕 작12 B3-1 ⓒ — <b>선택</b> 필드. 생략 가능하고 <b>생략이 기본</b>이다(설계 §5-2).
        /// <para>유일한 유의미 값 = <c>"tunnel-recovery"</c>(워치독 터널 자가복구 · 무인).
        /// 그 값일 때만 응답에서 tenant 묶음과 bootstrap.token·tokenKey 를 <b>뺀다</b>.</para>
        /// <para>🔴 installerVersion 으로는 단계를 가를 수 없다 — 설치 EXE 는 <c>{#AppVersion}</c>,
        /// 워치독은 <c>VersionInfo.Current</c> 로 **둘 다 제품 버전**이라 모양이 같을 수 있다.
        /// 추측으로 가르면 조용히 틀린다.</para>
        /// </summary>
        public string? Purpose { get; set; }
    }

    /// <summary>🆕 작12 B3-1 ⓑ — 잠금 판독 행. 모양은 기존 <c>serial_verify_locks</c> 그대로(DDL 0).</summary>
    private class BootstrapLockRow
    {
        public int FailedCount { get; set; }
        public int IsLocked { get; set; }
        /// <summary>마지막 실패가 60분 창 **안**인가 — 1 이면 잠금 유효, 0 이면 자동 해제(사장님 결재 ③).</summary>
        public int WithinWindow { get; set; }
    }

    private class TenantRow
    {
        // 길 B (사장님 결재 2026-06-18): BizNo·CeoName 속성 제거 — 백오피스는 사업자번호·대표자명 미보유.
        public string TenantId { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string? DomainAlias { get; set; }
        public string CompanyName { get; set; } = "";
        public string Tel { get; set; } = "";
        public string Status { get; set; } = "";
        public string? Email { get; set; }
        public string? PlanType { get; set; }
    }
}
