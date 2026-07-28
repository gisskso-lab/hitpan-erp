namespace HitPan.Infrastructure.Configuration;

// 사장님 결재 2026-06-12 — 사고 #41·#42 봉합 (싱글 각각 설치 영역 정합)
// 설치 마법사 영역 환경변수 영역 폐기 → 슬롯별 db.conf 영역에서 직접 영역 읽음
//
// 우선순위:
//   1) EXE 옆 영역 db.conf (싱글 각각 설치 영역 = tenant-{slot}\db.conf 영역)
//   2) 환경변수 (개발 영역 호환 + 폴백)
//
// 헌법 정합:
//   #20 (워크플로우 끊김 0건) — db.conf 영역 0건 시 환경변수 폴백
//   #22 (본사 데이터 0건) — 회사별 자격증명 영역 회사 PC 영역 박힘
//   #34 (정식 완성도) — 멀티사업자 영역 환경변수 영역 덮어쓰기 사고 #41·#42 차단
public static class TenantConfigReader
{
    private static readonly Dictionary<string, string> _cache = new();
    private static bool _loaded;
    private static readonly object _lock = new();

    public static string? Get(string key)
    {
        EnsureLoaded();
        if (_cache.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            return v;

        // 폴백: 환경변수 (개발 영역 호환)
        var env = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    public static string GetRequired(string key)
    {
        var v = Get(key);
        if (string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException(
                $"{key} 영역 0건 — db.conf 또는 환경변수 영역 박혀야 함");
        return v;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;

            // 후보 영역 (싱글 각각 설치 영역 정합):
            //   1) {exeDir}\db.conf (회사별 EXE 옆)
            //   2) {exeDir}\..\db.conf (api 폴더 영역 = tenant-N\db.conf)
            //   3) {exeDir}\..\..\db.conf
            //   4) 환경변수 HITPAN_DB_CONF (개발 영역 명시)
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            var candidates = new List<string>
            {
                Path.Combine(exeDir, "db.conf"),
                Path.Combine(exeDir, "..", "db.conf"),
                Path.Combine(exeDir, "..", "..", "db.conf"),
            };
            var envConf = Environment.GetEnvironmentVariable("HITPAN_DB_CONF");
            if (!string.IsNullOrWhiteSpace(envConf))
                candidates.Insert(0, envConf);

            foreach (var path in candidates)
            {
                var full = Path.GetFullPath(path);
                if (!File.Exists(full)) continue;

                foreach (var line in File.ReadAllLines(full))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                    var i = trimmed.IndexOf('=');
                    if (i <= 0) continue;
                    var k = trimmed.Substring(0, i).Trim();
                    var v = trimmed.Substring(i + 1).Trim();
                    _cache[k] = v;
                }
                break;
            }

            // hitpan-keys.conf 영역도 박힌 영역에서 영역 박음 (JWT_SECRET·ERP_ENCRYPTION_KEY)
            foreach (var path in candidates.Select(p => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p))!, "hitpan-keys.conf")))
            {
                if (!File.Exists(path)) continue;
                foreach (var line in File.ReadAllLines(path))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                    var i = trimmed.IndexOf('=');
                    if (i <= 0) continue;
                    var k = trimmed.Substring(0, i).Trim();
                    var v = trimmed.Substring(i + 1).Trim();
                    if (!_cache.ContainsKey(k)) _cache[k] = v;
                }
                break;
            }

            _loaded = true;
        }
    }
}
