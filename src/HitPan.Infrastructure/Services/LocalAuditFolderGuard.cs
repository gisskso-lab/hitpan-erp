using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using HitPan.Application.Interfaces;

namespace HitPan.Infrastructure.Services;

/// <summary>
/// 🔴 W-G1-52 갈래 0 고침 1 X1(3-V F1 · 4 발견 1) — 감사 폴더 · 파일 문지기.
/// 꼴은 <c>BackupService.EnsureRestrictedSystemFolder</c>(:815-870 · C-8 · C-12)를 <b>본떴다</b> — 그 파일은 고치지 않는다.
/// </summary>
/// <remarks>
/// 왜 그 공용 입구를 그대로 부르지 않나: ① 실패 형이 <see cref="InvalidOperationException"/> · 문구가 「백업」 기준이라 X7(이름 있는 예외 하나)과 안 맞음
///   ② 그 형식은 DB · Dapper 를 쓰는 큰 서비스라 G11 훑개(전이 훑기 · F10)가 넘어가면 네트워크 · DB 형식이 잡힘 ③ 파일 자신의 검사(재분석 · 소유자)가 없음.
///
/// 🔴 판정(Windows):
///   <list type="number">
///   <item>폴더와 그 위 폴더(드라이브 뿌리까지)에 재분석 지점(교차점 · 심볼릭 링크 · 마운트) 하나라도 있으면 거절 — 만들기 <b>전</b>과 <b>뒤</b> 두 번.</item>
///   <item>없으면 상속을 끊고 SYSTEM · Administrators · 실행 계정만 모든 권한으로 만든다(일반 사용자 권한 0). 실서비스 실행 계정 = SYSTEM(예약 작업 /RU SYSTEM)이라 실제로는 SYSTEM · Administrators 둘.</item>
///   <item>소유자 ∉ {SYSTEM · Administrators · 실행 계정} 이면 거절(남이 먼저 만든 폴더).</item>
///   <item>권한 줄 주체 = 위 셋 + 개별 사용자 계정만(관리자만 붙일 수 있음 · C-12 ⓑ 그대로). 그룹 · 별칭 · 잘 알려진 그룹(Users · Everyone · CREATOR OWNER …)은 거절 · 판정 못 하면 거절.</item>
///   <item>파일이 이미 있으면 열기 전에 파일 자신도: 재분석 지점이면 거절 · 소유자 · 권한 줄 같은 판정.</item>
///   </list>
/// Windows 밖(리눅스 CI)은 권한 모델이 달라 <b>권한 걸음만 건너뛰고</b> 재분석(심볼릭 링크) 검사는 그대로 한다.
/// 실패 = <see cref="LocalAuditWriteException"/>(문구에 경로 값을 넣지 않음).
/// </remarks>
internal static class LocalAuditFolderGuard
{
    /// <summary>감사 폴더를 안전하게 만들거나(없으면) 안전한지 확인한다(있으면). 실패 = <see cref="LocalAuditWriteException"/>.</summary>
    public static void EnsureFolder(string folder)
    {
        var full = Path.GetFullPath(folder);
        ThrowIfReparsePointOnPath(full, "folder_reparse_point");

        if (OperatingSystem.IsWindows())
        {
            EnsureFolderWindows(full);
        }
        else
        {
            Directory.CreateDirectory(full);
        }

        // 만든 직후에도 검사한다 — 확인과 만들기 사이에 남이 먼저 만든(또는 연결로 바꿔치기한) 경우까지 같은 판정.
        ThrowIfReparsePointOnPath(full, "folder_reparse_point");
        if (OperatingSystem.IsWindows()) CheckOwnerAndRulesWindows(new DirectoryInfo(full), isFolder: true);
    }

    /// <summary>파일이 이미 있으면 열기 전에 파일 자신을 검사한다(없으면 아무것도 안 함 · 만들지 않음).</summary>
    public static void EnsureExistingFileSafe(string file)
    {
        var info = new FileInfo(Path.GetFullPath(file));
        if (!info.Exists && info.LinkTarget is null) return;
        if (IsReparse(info))
            throw new LocalAuditWriteException("file_reparse_point",
                "감사 파일이 다른 위치로 이어진 연결 파일이라 쓰지 않았습니다. 관리자에게 확인을 요청하세요.");
        if (OperatingSystem.IsWindows()) CheckOwnerAndRulesWindows(info, isFolder: false);
    }

    /// <summary>폴더와 그 위 폴더 가운데 재분석 지점이 하나라도 있으면 실패. 아직 없는 칸은 이 코드가 만들 칸이라 건너뛴다.</summary>
    internal static void ThrowIfReparsePointOnPath(string fullPath, string reason)
    {
        for (var current = new DirectoryInfo(fullPath); current is not null; current = current.Parent)
        {
            if (!current.Exists && current.LinkTarget is null) continue;
            if (IsReparse(current))
                throw new LocalAuditWriteException(reason,
                    "감사 폴더나 그 위 폴더가 다른 위치로 이어진 연결 폴더라서 감사 줄을 쓰지 않았습니다. 관리자에게 확인을 요청하세요.");
        }
    }

    private static bool IsReparse(FileSystemInfo info) =>
        (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;

    [SupportedOSPlatform("windows")]
    private static void EnsureFolderWindows(string full)
    {
        var dir = new DirectoryInfo(full);
        if (dir.Exists) return;

        // 상위(%ProgramData%\HitPan)는 다른 기능(워치독 · 백업)도 쓰므로 종전 상속대로 두고, 감사 한 칸만 제한한다(C-8 과 같음).
        if (dir.Parent is { Exists: false } parent) Directory.CreateDirectory(parent.FullName);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in AllowedPrincipals())
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }
        dir.Create(security);
    }

    [SupportedOSPlatform("windows")]
    internal static void CheckOwnerAndRulesWindows(FileSystemInfo target, bool isFolder)
    {
        var allowed = AllowedPrincipals();
        target.Refresh();
        FileSystemSecurity acl = target is DirectoryInfo d
            ? d.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
            : ((FileInfo)target).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);

        // C-12 ⓐ 소유자 — 먼저 만들어 가진 폴더 · 파일은 여기서 걸린다.
        if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !allowed.Contains(owner))
            throw new LocalAuditWriteException(isFolder ? "folder_owner" : "file_owner",
                (isFolder ? "감사 폴더" : "감사 파일") + "의 소유자가 허용된 계정(SYSTEM·Administrators)이 아니라 감사 줄을 쓰지 않았습니다. 관리자에게 확인을 요청하세요.");

        // C-12 ⓑ 권한 줄 주체 허용 목록 — 위 셋 + 개별 사용자 계정만. 그룹 · 별칭 · 잘 알려진 그룹은 권한 종류와 무관하게 거절.
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.IdentityReference is not SecurityIdentifier sid || allowed.Contains(sid)) continue;
            if (!IsIndividualUserAccountWindows(sid))
                throw new LocalAuditWriteException(isFolder ? "folder_acl" : "file_acl",
                    (isFolder ? "감사 폴더" : "감사 파일") + "에 개별 사용자가 아닌 그룹(모든 사용자·Users 등)의 권한이 있어 감사 줄을 쓰지 않았습니다. 관리자에게 확인을 요청하세요.");
        }
    }

    /// <summary>허용 주체 = SYSTEM · Administrators · 실행 계정(실서비스 = SYSTEM).</summary>
    [SupportedOSPlatform("windows")]
    internal static HashSet<SecurityIdentifier> AllowedPrincipals()
    {
        var set = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
        };
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is { } current) set.Add(current);
        return set;
    }

    /// <summary>C-12 ⓑ — 이 SID 가 개별 사용자 계정인가(LookupAccountSid 의 SidTypeUser). 판정하지 못하면 false(=거절).</summary>
    [SupportedOSPlatform("windows")]
    private static bool IsIndividualUserAccountWindows(SecurityIdentifier sid)
    {
        const int SidTypeUser = 1;
        const int ErrorInsufficientBuffer = 122;
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        uint nameLength = 256, domainLength = 256;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var name = new char[nameLength];
            var domain = new char[domainLength];
            if (LookupAccountSidW(null, bytes, name, ref nameLength, domain, ref domainLength, out var use))
                return use == SidTypeUser;
            if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer) return false;
        }
        return false;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupAccountSidW(string? lpSystemName, byte[] sid, [Out] char[] name, ref uint cchName,
        [Out] char[] referencedDomainName, ref uint cchReferencedDomainName, out int peUse);
}
