using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Security;

/// <summary>
/// 원격제어 감지 — 작B v3.0 W2 본질 보강 (Red Team 1순위 박제)
///
/// Red Team 25년 박제:
/// "영세 사업장 사장님이 'ERP 좀 봐달라'며 자식·세무사·외주 개발자에게 원격 허용
/// → PIN 입력 화면 그대로 노출 → 동시 발급. 이게 한국 중소기업 진짜 공격 벡터다."
///
/// 감지 대상:
/// - TeamViewer (한국 1위)
/// - AnyDesk (한국 2위)
/// - RustDesk (오픈소스 신흥)
/// - Chrome Remote Desktop (브라우저 기반)
/// - Microsoft RDP (rdpclip.exe)
/// - ScreenConnect, LogMeIn, Splashtop, GoToMyPC
///
/// 헌법 정합:
/// - #25 (3대 원칙 안전하게): 원격제어 시 발급 차단
/// - #30 (고객 PC 자가 회복): 워치독 통합 영역
/// </summary>
public interface IRemoteControlDetector
{
    /// <summary>원격제어 SW 활성 여부 (발급 직전 호출).</summary>
    bool IsRemoteControlActive(out string? detectedTool);

    /// <summary>활성 원격제어 SW 전체 목록 (로그용).</summary>
    IReadOnlyList<string> GetActiveRemoteTools();
}

public sealed class RemoteControlDetector : IRemoteControlDetector
{
    // 감지 대상 프로세스 (실측 박제 — 한국 시장 기준)
    private static readonly Dictionary<string, string> BLOCKED_TOOLS = new(StringComparer.OrdinalIgnoreCase)
    {
        // 한국 시장 점유율 순
        { "TeamViewer", "TeamViewer (한국 1위)" },
        { "TeamViewer_Service", "TeamViewer 서비스" },
        { "AnyDesk", "AnyDesk (한국 2위)" },
        { "rustdesk", "RustDesk (오픈소스)" },
        { "chrome_remote_desktop_host", "Chrome Remote Desktop" },
        { "remoting_host", "Chrome Remote Desktop (Legacy)" },
        { "rdpclip", "Microsoft RDP (Windows 원격 데스크톱)" },
        { "ScreenConnect", "ScreenConnect" },
        { "LogMeIn", "LogMeIn" },
        { "g2m", "GoToMyPC" },
        { "Splashtop", "Splashtop" },
        { "alpinec", "AlpineCircle" },
        { "ConnectWiseChat", "ConnectWise Control" },
        // 한국 토종
        { "rsupport", "알서포트 (RemoteCall)" },
        { "anysupport", "AnySupport (안랩)" },
        { "rxc", "RXClient (한국 원격지원)" }
    };

    private readonly ILogger<RemoteControlDetector> _logger;

    public RemoteControlDetector(ILogger<RemoteControlDetector> logger)
    {
        _logger = logger;
    }

    public bool IsRemoteControlActive(out string? detectedTool)
    {
        detectedTool = null;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    if (BLOCKED_TOOLS.TryGetValue(proc.ProcessName, out var displayName))
                    {
                        detectedTool = displayName;
                        _logger.LogWarning("🚨 원격제어 감지 (Red Team 위험): {Tool} (PID: {Pid})",
                            displayName, proc.Id);
                        return true;
                    }
                }
                catch (InvalidOperationException procEx)
                {
                    // 프로세스 접근 권한 없음 (시스템 프로세스) — 무시 (헌법 #15 정합 로그)
                    _logger.LogDebug(procEx, "원격제어 감지: 프로세스 접근 권한 없음 (정상 — 시스템 프로세스)");
                }
                finally
                {
                    proc.Dispose();
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "원격제어 감지 중 예외 — 보수적으로 false 반환 (가도 차단 회피)");
            return false;
        }
    }

    public IReadOnlyList<string> GetActiveRemoteTools()
    {
        var found = new List<string>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return found;

        try
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    if (BLOCKED_TOOLS.TryGetValue(proc.ProcessName, out var displayName))
                    {
                        if (!found.Contains(displayName))
                            found.Add(displayName);
                    }
                }
                catch (InvalidOperationException procEx) { _logger.LogDebug(procEx, "활성 원격제어 도구 조회: 프로세스 접근 권한 없음 (정상)"); }
                finally { proc.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "원격제어 목록 조회 예외");
        }

        return found;
    }
}
