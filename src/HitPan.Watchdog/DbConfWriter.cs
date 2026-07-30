namespace HitPan.Watchdog;

/// <summary>
/// db.conf 값 갱신 (작업지시서 20260730작8 P0-4, 사장님 결재 2026-07-30).
///
/// ■ 왜 필요한가
///   TunnelTokenRecovery 가 본사에서 새 터널 토큰을 받아도, db.conf 에 저장하지 못하면
///   WS-28-D(ServiceReinstall)는 여전히 옛(죽은) 토큰을 읽는다 → 영구 미복구.
///   같은 사각지대를 오늘 인스톨러(HitPan-Universal.iss UpdateDbConfValue)에서도 봉합했다.
///   워치독 쪽에도 대응하는 쓰기 수단이 필요하다.
///
/// ■ 설계 원칙
///   · **해당 줄만 교체.** 다른 줄은 한 글자도 건드리지 않는다(헌법 #1 — 덮어쓰기 금지).
///   · 파일을 새로 만들지 않고 **내용만 덮는다** → 인스톨러가 걸어둔 ACL
///     (icacls Administrators·SYSTEM 만)이 유지된다. db.conf 는 토큰·키를 담은 시크릿이다.
///   · db.conf 가 없으면 **아무것도 하지 않는다**(false 반환). 없는 곳에 새로 만들면
///     ACL 없는 평문 시크릿 파일이 생겨 헌법 #22 위반이 된다.
///   · 경로 규칙은 DbConfReader.ResolveDbConfPath 와 동일하게 유지한다(단일 진실원 정합).
///     그쪽이 private 이라 물리적으로 공유는 못 하므로, 규칙이 갈라지지 않게 주석으로 못박는다.
///     ⚠️ DbConfReader 의 후보 경로가 바뀌면 여기도 같이 바꿀 것.
/// </summary>
public static class DbConfWriter
{
    /// <summary>
    /// db.conf 의 key 값을 교체한다(없으면 줄 추가). 성공 시 true.
    /// 파일 부재·IO 오류 시 false — 예외를 던지지 않는다(워치독 사이클 보호).
    /// </summary>
    public static bool SetValue(string key, string value)
    {
        var path = ResolveDbConfPath();
        if (path is null) return false;

        try
        {
            var lines = File.ReadAllLines(path).ToList();
            var found = false;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith(key + "=", StringComparison.Ordinal))
                {
                    lines[i] = key + "=" + value;
                    found = true;
                }
            }
            if (!found) lines.Add(key + "=" + value);

            File.WriteAllLines(path, lines);
            return true;
        }
        catch (Exception)
        {
            // 호출부가 로그를 남긴다(헌법 #15 — 여기서 삼키지만 침묵은 아니다).
            return false;
        }
    }

    /// <summary>
    /// DbConfReader.ResolveDbConfPath 와 **동일 규칙**. 둘이 갈라지면 워치독이
    /// 읽는 파일과 쓰는 파일이 달라져 복구가 조용히 실패한다.
    /// </summary>
    private static string? ResolveDbConfPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "..", "db.conf"),  // {app}\watchdog\ → {app}\db.conf (정식 설치 구조)
            Path.Combine(baseDir, "db.conf")          // 동일 폴더(하위호환·테스트)
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
