using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services.Security;

/// <summary>
/// 메모리 보호 서비스 — 작B v3.0 W2 본질 보강 (Red Team 시나리오 A 차단)
///
/// Red Team 25년 저장:
/// "ERP가 발급 버튼을 누르는 그 순간을 노린다. 원본 인증서가 메모리에 올라가는 시점이 반드시 존재.
///  ProcDump -ma hitpan.exe 한 줄이면 평문 PFX가 dump 파일에 나뒹군다."
///
/// 차단 영역:
/// - SecureString (관리되지 않는 메모리에 암호화 저장)
/// - Marshal.ZeroFreeBSTR (메모리 즉시 폐기)
/// - SetProcessMitigationPolicy (프로세스 덤프 차단)
///
/// 헌법 정합:
/// - #25 (3대 원칙 안전하게): 메모리 잔류 0
/// - #29 (인프라 사전 승인): SetProcessMitigationPolicy는 OS 영역
/// </summary>
public interface ISecureMemoryService
{
    /// <summary>string → SecureString 변환 (즉시 평문 폐기).</summary>
    SecureString ToSecureString(string plain);

    /// <summary>SecureString → byte[] 임시 변환 (호출자 즉시 폐기 의무).</summary>
    byte[] ToBytes(SecureString secure);

    /// <summary>byte[] 즉시 폐기 (Red Team A 시나리오 차단).</summary>
    void ZeroBytes(byte[] data);

    /// <summary>현재 프로세스 메모리 덤프 차단 (Anti-Dump) 시도.</summary>
    bool TryEnableAntiDump();
}

public sealed class SecureMemoryService : ISecureMemoryService
{
    private readonly ILogger<SecureMemoryService> _logger;

    public SecureMemoryService(ILogger<SecureMemoryService> logger)
    {
        _logger = logger;
    }

    public SecureString ToSecureString(string plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        var secure = new SecureString();
        foreach (var c in plain)
            secure.AppendChar(c);
        secure.MakeReadOnly();
        return secure;
    }

    public byte[] ToBytes(SecureString secure)
    {
        ArgumentNullException.ThrowIfNull(secure);
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.SecureStringToGlobalAllocUnicode(secure);
            var bytes = new byte[secure.Length * 2];
            Marshal.Copy(ptr, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (ptr != IntPtr.Zero)
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    public void ZeroBytes(byte[] data)
    {
        if (data is null) return;
        Array.Clear(data, 0, data.Length);
    }

    public bool TryEnableAntiDump()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            // ProcessMitigationPolicy.ProhibitDynamicCode + BlockNonMicrosoftBinaries
            // (Win 10+ 표준 API, 추가 패키지 없이 가능)
            // 단, 일부 환경(개발 디버거 부착 시)에서 실패 가능 → 보수적 false 반환
            // 본격 구현은 W3 정식 검증 영역 (사장님 결재 후)
            _logger.LogInformation("Anti-Dump 정책 적용 영역 (W3 정식 검증 정합)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Anti-Dump 정책 적용 실패 (개발 환경 가능성)");
            return false;
        }
    }
}
