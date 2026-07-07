using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace HitPan.API.Services;

// seed-parent 오프라인 서브커맨드 (작업지시서 20260707작1 ②단계 P0-3, A안 — 사장님 결재 2026-07-07)
//
// 목적 (헌법 #40·#30·#20 정합):
//   웹서버(터널·CORS·브라우저) 없이 부모계정을 로컬 DB 에 생성한다. 설치마법사(.iss)가 이 서브커맨드를
//   호출해 부모계정을 심으므로, 오늘 진범(브라우저→백오피스 CORS 차단)을 원천 우회한다.
//
// 호출 규약 (.iss 가 준수):
//   HitPan.API.exe seed-parent <inputJsonPath>
//     - <inputJsonPath> = 입력 JSON 파일 경로. 비밀번호 평문을 PowerShell 인자/SetupLog 에 노출하지 않기 위해
//       모든 입력을 임시파일(ACL 제한)로 전달한다. 서브커맨드는 읽는 즉시 파일을 소각(삭제)한다.
//   입력 JSON 필드:
//     signedProof : 백오피스 발급 증표(3-part 공개키 권장, 2-part HMAC 도 이행기 수용)
//     loginId     : 부모계정 아이디(형식무관, users.email 재사용 — #40)
//     password    : 부모계정 비밀번호(로컬 BCrypt 만, 본사 0 — #22·#40)
//     name        : 부모계정 이름
//     bizNo       : 사업자번호(10자리) — local_company 저장용
//     ceoName·tel·address·email·bizType·bizItem·zipCode·corpNo : 회사정보(선택)
//
// 종료 코드 (.iss 가 판정):
//   0  = 성공(부모계정 생성 완료. 이미 존재 시에도 0 = 멱등 성공으로 처리)
//   2  = 입력 오류(파일 없음·JSON 파싱 실패·필수 누락)
//   3  = 증표 검증 실패(위·변조·만료·알 수 없는 kid)
//   4  = bootstrap/부모계정 생성 실패(DB·비즈니스 규칙)
//   5  = 예기치 못한 서버 오류
//
// tenant_id 는 증표 payload 의 sub 단일 사용(파생 금지 — 원칙 B). local_company·users 동일값.
public static class SeedParentCommand
{
    public const string CommandName = "seed-parent";

    public static async Task<int> RunAsync(WebApplicationBuilder builder, string[] args)
    {
        // 서브커맨드는 웹 호스트를 띄우지 않고 DI 컨테이너만 빌드해 공유 서비스를 재사용한다.
        //   (create-parent 와 동일한 SerialProofVerifier·CompanyBootstrapProvisioner — 복붙 금지.)
        builder.Services.AddSingleton<SerialProofVerifier>();
        builder.Services.AddScoped<CompanyBootstrapProvisioner>();

        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("SeedParent");

        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("seed-parent: 입력 JSON 경로 인자가 필요합니다. 사용법: HitPan.API.exe seed-parent <inputJsonPath>");
            return 2;
        }

        var inputPath = args[1];
        SeedParentInput? input;
        try
        {
            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"seed-parent: 입력 파일을 찾을 수 없습니다: {inputPath}");
                return 2;
            }

            var json = await File.ReadAllTextAsync(inputPath);
            input = JsonSerializer.Deserialize<SeedParentInput>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"seed-parent: 입력 파일 읽기/파싱 실패: {ex.Message}");
            return 2;
        }
        finally
        {
            // 비밀번호 평문 잔존 방지 — 읽은 즉시 소각.
            TryDeleteFile(inputPath, logger);
        }

        if (input is null
            || string.IsNullOrWhiteSpace(input.SignedProof)
            || string.IsNullOrWhiteSpace(input.LoginId)
            || string.IsNullOrWhiteSpace(input.Password)
            || string.IsNullOrWhiteSpace(input.Name))
        {
            Console.Error.WriteLine("seed-parent: 필수 입력 누락(signedProof·loginId·password·name).");
            return 2;
        }

        try
        {
            var verifier = sp.GetRequiredService<SerialProofVerifier>();
            var provisioner = sp.GetRequiredService<CompanyBootstrapProvisioner>();
            var ct = CancellationToken.None;

            // 증표 오프라인 검증 — create-parent 와 동일 규칙(allowExpired: true, 서명·aud 강제).
            var (ok, payload, error) = verifier.Verify(input.SignedProof, allowExpired: true);
            if (!ok || payload is null)
            {
                Console.Error.WriteLine($"seed-parent: 증표 검증 실패 — {error}");
                return 3;
            }

            // ① 회사 부트스트랩(local_company + local_subscription, is_locked=1). 이미 잠겨 있으면 멱등 통과.
            if (!string.IsNullOrWhiteSpace(input.BizNo))
            {
                var (bOutcome, bMsg) = await provisioner.BootstrapCompanyAsync(payload, new CompanyBootstrapInput
                {
                    BizNo = input.BizNo,
                    CeoName = input.CeoName,
                    Tel = input.Tel,
                    Address = input.Address,
                    Email = input.Email,
                    BizType = input.BizType,
                    BizItem = input.BizItem,
                    ZipCode = input.ZipCode,
                    CorpNo = input.CorpNo,
                    SyncSource = "seed-parent"
                }, ct);

                // AlreadyLocked = 이미 회사정보 반영됨 → 부모계정 단계로 진행(멱등). 그 외 Error = 실패.
                if (bOutcome == CompanyBootstrapProvisioner.BootstrapOutcome.Error)
                {
                    Console.Error.WriteLine($"seed-parent: 회사 부트스트랩 실패 — {bMsg}");
                    return 4;
                }
            }

            // ② 부모계정 생성.
            var (cOutcome, cMsg, userId) = await provisioner.CreateParentAsync(payload, new CreateParentInput
            {
                LoginId = input.LoginId,
                Password = input.Password,
                Name = input.Name
            }, ct);

            switch (cOutcome)
            {
                case CompanyBootstrapProvisioner.CreateParentOutcome.Ok:
                    Console.WriteLine($"seed-parent: OK tenant={payload.TenantCode} userId={userId}");
                    return 0;
                case CompanyBootstrapProvisioner.CreateParentOutcome.ParentExists:
                    // 멱등: 부모계정이 이미 있으면 재설치·재실행으로 보고 성공 처리(설치 실패로 오판 방지).
                    Console.WriteLine($"seed-parent: 이미 부모계정 존재(멱등 성공) tenant={payload.TenantCode}");
                    return 0;
                case CompanyBootstrapProvisioner.CreateParentOutcome.BootstrapMissing:
                    Console.Error.WriteLine($"seed-parent: 부모계정 생성 실패 — {cMsg}");
                    return 4;
                case CompanyBootstrapProvisioner.CreateParentOutcome.DuplicateLogin:
                    Console.Error.WriteLine($"seed-parent: 부모계정 생성 실패 — {cMsg}");
                    return 4;
                default:
                    Console.Error.WriteLine($"seed-parent: 부모계정 생성 실패 — {cMsg}");
                    return 4;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"seed-parent: 예기치 못한 오류 — {ex.Message}");
            return 5;
        }
    }

    private static void TryDeleteFile(string path, ILogger logger)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            // 소각 실패는 치명 아님(설치 실패로 만들지 않음) — 다만 흔적 남을 수 있으니 경고.
            logger.LogWarning(ex, "[SeedParent] 입력 임시파일 소각 실패: {Path}", path);
        }
    }
}

public sealed class SeedParentInput
{
    [JsonPropertyName("signedProof")] public string SignedProof { get; set; } = "";
    [JsonPropertyName("loginId")] public string LoginId { get; set; } = "";
    [JsonPropertyName("password")] public string Password { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("bizNo")] public string? BizNo { get; set; }
    [JsonPropertyName("ceoName")] public string? CeoName { get; set; }
    [JsonPropertyName("tel")] public string? Tel { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("bizType")] public string? BizType { get; set; }
    [JsonPropertyName("bizItem")] public string? BizItem { get; set; }
    [JsonPropertyName("zipCode")] public string? ZipCode { get; set; }
    [JsonPropertyName("corpNo")] public string? CorpNo { get; set; }
}
