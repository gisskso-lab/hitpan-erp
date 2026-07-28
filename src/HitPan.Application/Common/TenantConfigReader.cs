namespace HitPan.Application.Common;

// 봉합 2026-06-17 1.2.12 — 사장님 결재 안 B (최적화) + 백엔드 매니저 P0-1
// HitPan.Application은 HitPan.Infrastructure 역참조 불가 → 사본 1.
// 원본: HitPan.Infrastructure.Configuration.TenantConfigReader (동일 로직).
//
// 헌법 정합:
//   #1 (덮어쓰기 0) — 원본 그대로 보존, 사본만 1개 추가
//   #20 (워크플로우 끊김 0) — db.conf 0건 시 환경변수 폴백
//   #22 (본사 데이터 0건) — 자격증명 회사 PC만
//   #33 (사장님 의중) — 1.2.6 환경변수 폐기 결재 완전 정합 (4파일 → 8+파일)
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

        var env = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    public static string GetRequired(string key)
    {
        var v = Get(key);
        if (string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException(
                $"{key} 구간 0건 — db.conf 또는 환경변수에 입력 필수");
        return v;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;

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

            // hitpan-keys.conf 구간도 같은 후보 디렉토리에서 추가 적재 (JWT_SECRET·ERP_ENCRYPTION_KEY)
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
