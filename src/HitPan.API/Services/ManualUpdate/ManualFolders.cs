using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace HitPan.API.Services.ManualUpdate;

/// <summary>
/// 2026-09-30 작1 갈래 U — 수동 업데이트가 쓰는 폴더(받는 곳 · 사용 기록)의 위치와 쓰기 전 검사.
///
/// ■ 위치 = <c>{app}\manual\</c> (병렬이슈 [3-V] 01 P0 반영 · PM 지시 9/30)
///   <c>%ProgramData%\HitPan</c> 은 일반 사용자가 하위 폴더를 '먼저' 만들 수 있다(Users 쓰기·추가 상속).
///   그러면 받은 zip·기록을 남이 바꿔치기할 자리가 생긴다. 설치 폴더 <c>{app}</c>(Program Files 아래)는
///   관리자·SYSTEM 만 쓴다 ⇒ 받는 곳·사용 기록을 그 아래에 둔다. {app} = API 실행 폴더(<c>{app}\api\</c>)의 위.
///   (워치독 <c>UpdateLockFile</c>·<c>DbConfReader</c> 가 쓰는 설치 구조 전제와 같다.)
///
/// ■ 쓰기 직전 검사 = 백업 폴더 C-12 방식(BackupService — 소유자 · 재분석 지점 · 넓은 그룹 쓰기 권한)
///   ⓐ 폴더와 그 위(드라이브 루트까지)에 재분석 지점(교차점·심볼릭 링크) 0
///   ⓑ 없으면 보호 ACL(상속 끊음 · SYSTEM·Administrators·실행 계정만)로 만든다 → 만든 뒤 다시 ⓐ
///   ⓒ 소유자 ∈ {SYSTEM · Administrators · 실행 계정}
///   ⓓ 위 셋 밖의 주체에게 쓰기 계열 허용 줄이 있으면 거부
///   ⚠️ BackupService 의 같은 판정은 private 이라 부를 수 없어 이 모듈 안에 같은 규칙으로 둔다(복사가 아니라 규칙 준수 ·
///      공용 함수로 뽑는 것은 BackupService 수정이라 이 갈래 범위 밖 — 개발명세서 「못 한 것」).
/// </summary>
public sealed class ManualFolders
{
    /// <summary>설치 폴더 {app}.</summary>
    public string AppRoot { get; }

    /// <summary>{app}\manual — 수동 모듈 전용.</summary>
    public string ManualDir => Path.Combine(AppRoot, "manual");

    /// <summary>{app}\manual\staging — 수동 업데이트가 받는 곳(워치독 staging 과 따로).</summary>
    public string StagingDir => Path.Combine(ManualDir, "staging");

    /// <summary>{app}\manual\usage.jsonl — 수동 사용 기록(덧붙이기만).</summary>
    public string UsageFile => Path.Combine(ManualDir, "usage.jsonl");

    /// <summary>운영: API 실행 폴더의 위를 {app} 로 본다.</summary>
    public ManualFolders()
        : this(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")))
    {
    }

    /// <summary>시험: {app} 를 직접 준다.</summary>
    public ManualFolders(string appRoot)
    {
        AppRoot = Path.GetFullPath(appRoot);
    }

    /// <summary>
    /// 폴더를 쓰기 전에 검사하고(없으면 제한 권한으로 만든다) 실패하면 <see cref="InvalidOperationException"/>.
    /// 윈도가 아니면 만들기만 한다(권한 모델이 다르다 — 운영은 윈도뿐).
    /// </summary>
    public static void EnsureRestricted(string dirPath)
    {
        if (OperatingSystem.IsWindows())
        {
            EnsureRestrictedWindows(dirPath);
            return;
        }
        Directory.CreateDirectory(dirPath);
    }

    /// <summary>파일 자체가 재분석 지점(연결)이면 true — 받은 파일을 믿기 전에 본다.</summary>
    public static bool IsReparsePoint(string filePath)
    {
        var info = new FileInfo(filePath);
        return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    [SupportedOSPlatform("windows")]
    private static void EnsureRestrictedWindows(string dirPath)
    {
        var allowed = AllowedPrincipals();
        var dir = new DirectoryInfo(Path.GetFullPath(dirPath));

        ThrowIfReparsePointOnPath(dir.FullName);

        if (!dir.Exists)
        {
            if (dir.Parent is { Exists: false } parent) EnsureRestrictedWindows(parent.FullName);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in allowed)
            {
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            }
            dir.Create(security);
            dir.Refresh();
        }

        // 만든 직후에도 다시 본다 — 확인과 생성 사이에 남이 먼저 만들었거나 연결로 바꿔치기한 경우까지 같은 판정.
        ThrowIfReparsePointOnPath(dir.FullName);

        var acl = dir.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !allowed.Contains(owner))
            throw new InvalidOperationException("수동 업데이트 폴더의 소유자가 허용된 계정이 아니라 쓰지 않았습니다.");

        const FileSystemRights WriteLike =
            FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.IdentityReference is SecurityIdentifier sid && allowed.Contains(sid)) continue;
            if ((rule.FileSystemRights & WriteLike) != 0)
                throw new InvalidOperationException("수동 업데이트 폴더에 허용되지 않은 계정의 쓰기 권한이 있어 쓰지 않았습니다.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ThrowIfReparsePointOnPath(string fullPath)
    {
        for (var current = new DirectoryInfo(fullPath); current is not null; current = current.Parent)
        {
            if (!current.Exists) continue;
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("수동 업데이트 폴더나 그 위 폴더가 다른 위치로 이어진 연결 폴더라서 쓰지 않았습니다.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static HashSet<SecurityIdentifier> AllowedPrincipals()
    {
        var set = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
        };
        using var me = WindowsIdentity.GetCurrent();
        if (me.User is { } user) set.Add(user);
        return set;
    }
}
