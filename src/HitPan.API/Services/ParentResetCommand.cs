using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using HitPan.Application.Services;
using HitPan.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace HitPan.API.Services;

// parent-reset 오프라인 서브커맨드 — W-G1-52 갈래 C(대표 비번 되찾기 · 윈도우 관리자 승인 C안)
//   근거: 덧 지시서 20261128작_W-G1-52_갈래AC_착수조건덧 §2 · 설계 W-G1-52 §8-4 2 · §15-6 ㄹ + R16 ① ~ ④ · 대표 §287 Q3 (가)
//
// 목적:
//   웹 호스트를 띄우지 않고(seed-parent 와 같은 진입 · Program.cs) 이 DB 의 대표 행이 정확히 1개인지 확인한 뒤
//   1회용 표(무작위 · DB 엔 지문만 · 10분 · 1회) 행 하나를 만들고, 기본 브라우저로 재설정 화면을 연다(표는 주소 # 뒤 · 서버 기록에 안 남음).
//
// 🔴 지키는 것
//   ① 스스로 관리자 확인 — 아니면 자기를 「관리자로 다시 실행」(runas · 윈도우 [예]). 다시 실행 표지 인자 1개로 되풀이 0(R16 ③).
//      넘기는 인자 = 고정값만(사용자 입력 0). 서비스 · 예약 작업 · 레지스트리 · 방화벽 손 0(#29).
//   ② 모르는 인자 = 웹 호스트 띄우기 전 종료(R16 ④ · 종료 코드 2).
//   ③ 🔴 H3 — 감사 폴더 · 파일을 만들거나 쓰지 않는다(ILocalAuditFile 을 부르지 않음). 명령 실행 사실은 API 가 재설정 때 감사 2벌로 남긴다.
//      (관리자 프로세스가 감사 폴더를 먼저 만들면 실행 계정 기준 보호 권한이 어긋나 SYSTEM API 가 영구 거절될 수 있다 · 짧은 재확인 발견 6 · #30)
//   ④ 🔴 #40 · seed-parent 머리 주석 선례 — 비번은 이 명령이 아예 받지 않는다. 표 값은 DB 에 넣지 않고(지문만) 로그(Serilog)에 싣지 않는다.
//      브라우저 열기에 실패했을 때만 이 창에 주소를 보여 준다(R16 ②).
//   ⑤ 브라우저는 승격 풀고 연다 — explorer.exe 에 주소를 넘겨 사용자 셸이 연다(R16 ②). ⚠️ 이 PC 실측 0(가설) · 시험 PC 몫.
//
// 종료 코드:
//   0 = 표 만들고 브라우저 열기 시도함(또는 관리자 창으로 넘김)
//   2 = 인자 오류 · 윈도우 아님
//   3 = 다시 실행했는데도 관리자 아님(되풀이 멈춤)
//   4 = 윈도우 「허용?」 에서 [아니요]
//   5 = DB 설정 · 연결 오류
//   6 = 대표 계정 0개
//   7 = 대표 계정 2개 이상
public static class ParentResetCommand
{
    public const string CommandName = "parent-reset";

    /// <summary>관리자로 다시 실행한 자식 프로세스 표지(고정값 · 되풀이 막기).</summary>
    public const string ElevatedMarker = "--elevated";

    /// <summary>웹 화면 포트 기본값(web-server.ps1 :4 `$port = 5234`).</summary>
    public const int DefaultWebPort = 5234;

    /// <summary>재설정 화면 경로(ParentReset.razor @page 와 같은 글자).</summary>
    public const string ResetPagePath = "/parent-reset";

    /// <summary>
    /// 인자 판정 — 「parent-reset」 단독 또는 「parent-reset --elevated」 만 받는다. 그 밖은 null(=종료 코드 2).
    /// </summary>
    /// <returns>true = 다시 실행 표지 있음 · false = 없음 · null = 모르는 인자.</returns>
    public static bool? ParseArgs(string[] args)
    {
        if (args is null || args.Length == 0 || !string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase)) return null;
        if (args.Length == 1) return false;
        if (args.Length == 2 && string.Equals(args[1], ElevatedMarker, StringComparison.Ordinal)) return true;
        return null;
    }

    /// <summary>재설정 화면 주소 — 표는 <c>#</c> 뒤(브라우저가 서버로 보내지 않음 · 서버 기록 0).</summary>
    public static string BuildResetUrl(int webPort, string token) =>
        $"http://localhost:{webPort}{ResetPagePath}#t={token}";

    public static async Task<int> RunAsync(WebApplicationBuilder builder, string[] args)
    {
        var elevated = ParseArgs(args);
        if (elevated is null)
        {
            Console.Error.WriteLine($"{CommandName}: 알 수 없는 인자입니다. 사용법: HitPan.API.exe {CommandName}");
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine($"{CommandName}: 윈도우에서만 쓸 수 있습니다.");
            return 2;
        }

        if (!IsAdministrator())
        {
            if (elevated.Value)
            {
                Console.Error.WriteLine("관리자 권한을 얻지 못했습니다. 메인PC 의 윈도우 관리자 계정으로 다시 해 주세요.");
                WaitKey();
                return 3;
            }
            return RelaunchElevated();
        }

        Console.WriteLine("히트판 — 대표 비밀번호 다시 정하기");

        string? token;
        try
        {
            // seed-parent 와 같은 연결 해석(db.conf 단일 진실원 — 관리자만 읽을 수 있는 파일 · 설계 §8-2 C 덧 벽).
            var cs = new CompanyBootstrapProvisioner(builder.Configuration, NullLogger<CompanyBootstrapProvisioner>.Instance).ResolveConnectionString();
            await using var db = new MySqlConnection(cs);
            var (outcome, issued) = await ParentResetTokenIssuer.IssueAsync(db, CancellationToken.None);
            switch (outcome)
            {
                case ParentResetTokenIssuer.Outcome.NoParent:
                    Console.Error.WriteLine("대표 계정을 찾지 못했습니다. 히트판 설치를 먼저 마쳐 주세요.");
                    WaitKey();
                    return 6;
                case ParentResetTokenIssuer.Outcome.MultipleParents:
                    Console.Error.WriteLine("대표 계정이 둘 이상이라 어느 쪽인지 정할 수 없습니다. 고객센터로 문의해 주세요.");
                    WaitKey();
                    return 7;
            }
            token = issued;
        }
        catch (Exception ex) when (ex is MySqlException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // 예외 문구에 연결 문자열이 실릴 수 있어 종류만 보인다(#40 — 자격증명 모양 출력 0).
            Console.Error.WriteLine($"자료 저장소에 연결하지 못했습니다({ex.GetType().Name}). 히트판이 켜져 있는지 확인한 뒤 다시 해 주세요.");
            WaitKey();
            return 5;
        }

        var url = BuildResetUrl(ResolveWebPort(), token!);
        if (!TryOpenBrowserUnelevated(url))
        {
            // R16 ② — 브라우저를 못 열면 주소를 이 창에 보여 준다(이 창 밖 기록 0).
            Console.WriteLine("브라우저를 열지 못했습니다. 아래 주소를 브라우저 주소 칸에 붙여 넣어 주세요.");
            Console.WriteLine(url);
        }
        Console.WriteLine($"브라우저에서 새 비밀번호를 정해 주세요. 이 안내는 {ParentResetTokenIssuer.LifetimeMinutes}분 동안 한 번만 쓸 수 있습니다.");
        WaitKey();
        return 0;
    }

    private static int ResolveWebPort()
    {
        var raw = TenantConfigReader.Get("WEB_PORT");
        return int.TryParse(raw, out var p) && p > 0 && p <= 65535 ? p : DefaultWebPort;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [SupportedOSPlatform("windows")]
    private static int RelaunchElevated()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            Console.Error.WriteLine("프로그램 위치를 찾지 못했습니다.");
            return 2;
        }

        try
        {
            // 고정 인자만(사용자 입력 0) · runas = 윈도우 「허용?」.
            using var p = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = CommandName + " " + ElevatedMarker,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
            });
            return 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // 1223 = 사용자가 [아니요] — 바뀐 것 0.
            Console.Error.WriteLine("윈도우 허용을 받지 못해 멈췄습니다. 바뀐 것은 없습니다.");
            WaitKey();
            return 4;
        }
    }

    private static bool TryOpenBrowserUnelevated(string url)
    {
        try
        {
            // explorer.exe 는 이미 떠 있는 사용자 셸에 넘겨 승격 없이 연다(R16 ② · 가설 — 시험 PC 실측 몫).
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            psi.ArgumentList.Add(url);
            using var p = Process.Start(psi);
            return p is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // 주소 값은 남기지 않는다 — 종류만.
            Console.Error.WriteLine($"브라우저 열기 실패({ex.GetType().Name}).");
            return false;
        }
    }

    private static void WaitKey()
    {
        if (Console.IsInputRedirected) return;
        Console.WriteLine("창을 닫으려면 아무 키나 누르세요.");
        Console.ReadKey(intercept: true);
    }
}
