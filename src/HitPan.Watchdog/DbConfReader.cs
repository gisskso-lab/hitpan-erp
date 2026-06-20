namespace HitPan.Watchdog;

/// <summary>
/// 워치독 단일출처 설정 리더 — 고객 PC의 {app}\db.conf 를 읽어 워치독 옵션을 동적 구성한다.
///
/// 봉합 (2026-06-23, 6차 전수조사 D-P0-01·D-P1-02, 사장님 결재 수정 B안):
///   종전엔 appsettings.json 의 HealthCheckUrl 이 "https://demo.hitpan.kr/health" 로 고정 출하되어,
///   고객 PC 워치독이 자기 터널이 아니라 본사 데모서버를 헬스체크했다.
///   → ① 고객 터널이 죽어도 데모가 살아있으면 "정상" 오판(자가복구 미작동, 헌법 #27·#28 무력)
///   → ② 데모 다운 시 전 고객 워치독이 동시에 FullRecovery 발동(자격증명 재생성+서비스 재설치) 집단 자해.
///   또 로컬 API 헬스 포트가 5234(Blazor dev)로 고정돼 슬롯 포트(5257+100*(슬롯-1))와 불일치했다.
///
///   환경변수(HITPAN_SUBDOMAIN 등)는 2026-06-12 사장님 결재로 폐기(멀티슬롯 덮어쓰기 사고 #41·#42·#39)되어
///   신뢰 단일출처가 아니다. ERP 본체(TenantConfigReader)와 동일하게 {app}\db.conf 를 단일출처로 읽는다.
///   db.conf 가 없거나 PRIMARY_DOMAIN 이 비면(또는 localhost), 외부 헬스체크를 비활성한다 — demo 로는 절대 가지 않는다.
/// </summary>
public static class DbConfReader
{
    /// <summary>
    /// {app}\db.conf 를 찾아 읽고, WatchdogOptions 의 HealthCheckUrl·로컬 API 헬스 포트를 고객 환경에 맞게 덮어쓴다.
    /// db.conf 미발견·PRIMARY_DOMAIN 부재·localhost 면 HealthCheckUrl 을 빈 문자열로 만들어 외부 헬스체크를 비활성한다.
    /// </summary>
    public static void ApplyToOptions(WatchdogOptions options, Action<string>? log = null)
    {
        var confPath = ResolveDbConfPath();
        if (confPath is null)
        {
            // db.conf 미발견 — demo 고정값을 쓰지 않도록 외부 헬스체크 비활성(집단 자해·오판 둘 다 차단).
            options.HealthCheckUrl = string.Empty;
            log?.Invoke("[DbConf] db.conf 미발견 — 외부 헬스체크 비활성(demo 미타격)");
            return;
        }

        var conf = Parse(confPath);

        // PRIMARY_DOMAIN → 외부 헬스체크 URL. localhost(LOCAL 모드)·빈 값이면 외부 체크 비활성.
        conf.TryGetValue("PRIMARY_DOMAIN", out var primaryDomain);
        if (string.IsNullOrWhiteSpace(primaryDomain) ||
            primaryDomain.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            options.HealthCheckUrl = string.Empty;
            log?.Invoke($"[DbConf] PRIMARY_DOMAIN='{primaryDomain}' — LOCAL/미설정, 외부 헬스체크 비활성");
        }
        else
        {
            options.HealthCheckUrl = $"https://{primaryDomain.Trim()}/health";
            log?.Invoke($"[DbConf] 외부 헬스체크 URL = {options.HealthCheckUrl}");
        }

        // API_PORT → 로컬 API 헬스 엔드포인트(슬롯 포트). 5234(Blazor dev) 고정 오류 정정.
        if (conf.TryGetValue("API_PORT", out var apiPortStr) &&
            int.TryParse(apiPortStr.Trim(), out var apiPort) && apiPort > 0)
        {
            var ep = options.Processes.HttpEndpoints.FirstOrDefault(e =>
                string.Equals(e.Name, "HitPan.API", StringComparison.OrdinalIgnoreCase));
            if (ep is not null)
            {
                ep.Url = $"http://127.0.0.1:{apiPort}/health";
                log?.Invoke($"[DbConf] 로컬 API 헬스 = {ep.Url}");
            }
        }
    }

    /// <summary>
    /// 봉합 (2026-06-21, 7차 전수조사 D6-P0-01, 사장님 결재 "db.conf 단일출처로 통합"):
    ///   HealthProbe·MetaPingClient·WS28C 가 종전엔 Machine 환경변수(HITPAN_TUNNEL_ID·HITPAN_LICENSE_KEY·
    ///   HITPAN_TENANT_ID)를 읽었으나, 인스톨러는 이 env var 를 더 이상 만들지 않는다(2026-06-12 폐기,
    ///   멀티슬롯 덮어쓰기 사고 #39·#41·#42). 신규설치 PC 에서 식별자가 전부 null → 본사 메타 ping 인증
    ///   약화 + 터널 자가복구 무력(헌법 #28·#30). 모든 소비자가 이 단일출처 헬퍼로 db.conf 값을 읽도록 통일한다.
    ///   db.conf 미발견·키 부재면 null 반환(소비자가 보수적으로 폴백).
    ///
    ///   db.conf 키 매핑: TUNNEL_ID(=백오피스 응답 domain.tunnelId, CF UUID) / LICENSE_KEY(=설치 시리얼) /
    ///   TENANT_CODE(=T-XXX, 본사 식별 해시용 — HITPAN_TENANT_ID 의미로 재사용) / PRIMARY_DOMAIN.
    /// </summary>
    public static string? GetValue(string key)
    {
        var confPath = ResolveDbConfPath();
        if (confPath is null) return null;
        var conf = Parse(confPath);
        return conf.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
    }

    /// <summary>
    /// 워치독 EXE는 {app}\watchdog\ 에 설치되고 db.conf 는 {app}\db.conf 에 있다(InstallWatchdog.ps1·.iss 정합).
    /// 실행 폴더 기준 상위(..\db.conf)를 1순위, 실행 폴더 자체를 2순위로 탐색한다.
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

    private static Dictionary<string, string> Parse(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                dict[key] = val;
            }
        }
        catch (Exception)
        {
            // 읽기 실패는 호출부가 빈 dict 로 안전 폴백(외부 헬스체크 비활성)하도록 그대로 둔다.
        }
        return dict;
    }
}
