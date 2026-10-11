using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

/// <summary>
/// 백오피스(hitpan_backoffice) 자립형 스키마 자동 적용기.
///
/// 사장님 결재 2026-06-18 — A안: "백오피스 스키마 파일 신설 + 마이그 자동실행".
///
/// 동작:
///   - 앱 기동 시 installer/backoffice/ 폴더의 *.sql을 파일명 번호순으로 순차 적용.
///   - 적용 이력(_schema_migrations)으로 멱등성 — 같은 (파일명+내용해시)는 1회만.
///   - CREATE TABLE IF NOT EXISTS 기반이라 재실행 안전.
///
/// 헌법 정합:
///   - #29 — DB 인스턴스 생성(CREATE DATABASE)·운영은 사장님 영역. 본 클래스는 "앱이 자기
///           스키마를 보장"하는 코드일 뿐. 환경변수 HITPAN_BO_AUTO_MIGRATE=1 일 때만 동작(기본 off).
///           즉 사장님이 명시적으로 켜야 적용 — 통제권은 사장님.
///   - #15 — 빈 catch 금지. 실패 시 로그 + 기동 중단(스키마 불일치로 런타임 500 잠복 방지).
///   - #19 — 스키마 보장으로 "어 어젠 됐는데" 류 환경차 사고 차단.
/// </summary>
public sealed class SchemaMigrator
{
    private readonly string _connectionString;
    private readonly string _scriptsDir;
    private readonly ILogger<SchemaMigrator> _logger;

    public SchemaMigrator(string connectionString, string scriptsDir, ILogger<SchemaMigrator> logger)
    {
        _connectionString = connectionString;
        _scriptsDir = scriptsDir;
        _logger = logger;
    }

    /// <summary>
    /// 스크립트 폴더의 *.sql을 번호순으로 적용. 환경변수 HITPAN_BO_AUTO_MIGRATE=1 일 때만 호출되도록
    /// Program.cs에서 게이트한다.
    /// </summary>
    public async Task ApplyAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_scriptsDir))
        {
            // 스크립트 폴더가 없으면 적용할 게 없음 — 명확히 로그하고 통과(사고 아님).
            _logger.LogWarning("[SchemaMigrator] 스크립트 폴더 없음: {Dir} — 스키마 적용 건너뜀", _scriptsDir);
            return;
        }

        await using var db = new MySqlConnection(_connectionString);
        await db.OpenAsync(ct);

        // 🔴 20261011작1 — 서버 판을 1회 판정한다 (설계 §2-1).
        //   왜: 운영 백오피스(NCP)는 MySQL 8.0.45 인데 우리 SQL 은 MariaDB 전용
        //       `ALTER … ADD COLUMN IF NOT EXISTS` 를 쓴다. MySQL 8 은 그 문법을 거부하고(1064),
        //       적용기는 첫 실패에서 기동을 중단하므로 11번 이후 전부가 운영에 못 들어갔다
        //       (선행검증서 20261011_1V §3 — 서버 로그 실측).
        //   🔴 20261011작1 2차수 — [4] 검증 V-3 교정. 종전 주석은 「판정 못 하면 보정 경로로 간다,
        //      보정은 MariaDB 에서도 정답이니까」였다. **그 전제가 측정으로 틀렸다**:
        //      MariaDB 11.4.10 에서 같은 이름 FK 재추가는 **1005(errno 121)** 를 낸다 — 1826 이 아니다.
        //      즉 관용 목록 밖이라, MariaDB 를 MySQL 로 오인하면 `31` 번 ADD CONSTRAINT 에서
        //      **기동이 영구 거부**될 수 있다(우리가 MySQL 쪽에서 막은 그 덫이 반대 판에 열린다).
        //   ⇒ 판독 실패(빈 문자열)는 **종전 경로**로 떨어뜨린다. 그게 「아무것도 안 바뀐 상태」이고,
        //      새 덫을 만들지 않는 쪽이다. (MySQL 인데 판독이 실패하면 종전처럼 1064 로 멈춘다 —
        //      오늘과 같은 상태일 뿐, 더 나빠지지 않는다.)
        var serverVersion = await QueryServerVersionAsync(db, ct);
        var isMariaDb = serverVersion.Length == 0
                     || serverVersion.Contains("mariadb", StringComparison.OrdinalIgnoreCase);
        _logger.LogInformation(
            "[SchemaMigrator] 서버 판 = {Version} → {Path}",
            string.IsNullOrEmpty(serverVersion) ? "(판독 실패)" : serverVersion,
            isMariaDb
                ? (serverVersion.Length == 0 ? "종전 경로(판독 실패 — 안전쪽)" : "종전 경로(무변경)")
                : "MySQL 호환 보정 경로");

        // 적용 이력 테이블 보장
        await ExecuteAsync(db, @"
            CREATE TABLE IF NOT EXISTS _schema_migrations (
                file_name     varchar(200) NOT NULL,
                content_hash  varchar(64)  NOT NULL,
                applied_at    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
                PRIMARY KEY (file_name)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci", ct);

        var files = Directory.GetFiles(_scriptsDir, "*.sql")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogInformation("[SchemaMigrator] 적용할 .sql 0개 ({Dir})", _scriptsDir);
            return;
        }

        foreach (var path in files)
        {
            var fileName = Path.GetFileName(path);
            var rawSql = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            // 멱등 해시는 "원본"(치환 전) 기준 — env값이 바뀌어도 같은 파일은 재실행 안 함.
            // (env로 주입하는 값은 비밀번호 해시 등 민감값이라 _schema_migrations에 흔적을 남기지 않는 목적도 겸함)
            var hash = ComputeSha256(rawSql);

            // 이미 같은 내용으로 적용됐으면 건너뜀(멱등)
            var appliedHash = await QueryHashAsync(db, fileName, ct);
            if (appliedHash == hash)
            {
                _logger.LogInformation("[SchemaMigrator] skip (적용됨) {File}", fileName);
                continue;
            }

            // 환경변수 치환: SQL 안 ${VAR} 를 실행 직전에만 환경변수 값으로 치환.
            //   - 의도: 비밀번호 해시 등 민감값을 Git/SQL 파일에 평문으로 두지 않고 NCP env로 주입(#22·#23).
            //   - 미설정(${VAR}가 그대로 남음) → 이 파일은 건너뜀. 빈 값으로 깨진 INSERT 실행 방지(#15·#19).
            if (!TrySubstituteEnv(rawSql, fileName, out var sql))
            {
                continue;
            }

            try
            {
                if (isMariaDb)
                {
                    // 종전 경로 — 한 파일을 통째로 실행(여러 CREATE TABLE 포함). MySqlConnector는
                    // AllowUserVariables + 다중 statement를 ExecuteNonQuery로 처리.
                    // 🔴 20261011작1 — 이 경로는 한 글자도 안 바꿨다(#1). MariaDB 판 회귀 0 의 근거.
                    await using (var cmd = new MySqlCommand(sql, db))
                    {
                        cmd.CommandTimeout = 120;
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
                else
                {
                    // 🔴 20261011작1 — MySQL 호환 보정 경로(설계 §2-2~§2-4).
                    await ApplyForMySqlAsync(db, sql, fileName, ct);
                }

                // 이력 기록(파일명 PK — 내용 바뀌면 UPDATE)
                await using (var rec = new MySqlCommand(@"
                    INSERT INTO _schema_migrations (file_name, content_hash, applied_at)
                    VALUES (@F, @H, current_timestamp(6))
                    ON DUPLICATE KEY UPDATE content_hash = @H, applied_at = current_timestamp(6)", db))
                {
                    rec.Parameters.AddWithValue("@F", fileName);
                    rec.Parameters.AddWithValue("@H", hash);
                    await rec.ExecuteNonQueryAsync(ct);
                }

                _logger.LogInformation("[SchemaMigrator] applied {File}", fileName);
            }
            catch (Exception ex)
            {
                // 빈 catch 금지(#15). 스키마 불일치는 런타임 500 잠복이므로 기동 중단으로 즉시 노출(#19).
                _logger.LogError(ex, "[SchemaMigrator] 적용 실패 {File} — 기동 중단", fileName);
                throw;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 🔴 20261011작1 — MySQL 8 호환 보정 (사장님 결재 2026-10-11 「안1대로 진행해」)
    //
    //   근거: docs/검증/선행/20261011_1V_백오피스_MySQL8_문법거부_선행검증서.md
    //         docs/설계/백오피스/20261011_스키마적용기_MySQL8호환_설계문서.md
    //
    //   🔴 installer/backoffice/*.sql 은 한 글자도 안 고친다 — 백오피스 DDL 진실원(#36·#37·#41)이고
    //      그 파일을 파싱해 표→칸 색인을 만드는 게이트(BackofficeColumnContractGateTests)가 산다.
    //      그래서 보정은 여기(적용기) 한 곳에서만 한다.
    //   감시: BackofficeSchemaMysql8GateTests (G-M8-1~5) — 이 보정을 되돌리면 FAIL 한다.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>주석(<c>-- …</c> · <c>#…</c> · <c>/* … */</c>)을 지우는 패턴. 실행문이 있는지 판정할 때만 쓴다.</summary>
    private static readonly Regex CommentRegex = new(
        @"/\*.*?\*/|--(?=[ \t\r\n])[^\n]*|--$|#[^\n]*",
        RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// <c>IF NOT EXISTS</c> / <c>IF EXISTS</c> 토큰 하나. <c>\G</c> 로 <b>검사 위치에서만</b> 맞춘다.
    /// <para>🔴 <c>ALTER</c> 문에서만 쓴다 — <c>CREATE TABLE IF NOT EXISTS</c> 는 MySQL 도 아는 문법이고
    /// 벗기면 멱등성이 깨진다(설계 §2-3).</para>
    /// <para>🔴 왜 「<c>ADD COLUMN</c> 뒤」 같은 모양으로 좁히지 않았나: 실물 31번이
    /// <c>ADD CONSTRAINT fk_… FOREIGN KEY IF NOT EXISTS (…)</c> 처럼 <b>이름이 중간에 끼는</b> 모양을 쓴다.
    /// 모양을 열거하면 그런 변형을 놓친다(G-M8-5c 가 설계 중에 실제로 잡아냈다).
    /// 그래서 <c>ALTER</c> 문 안에서는 — 인용·주석 밖이라면 — 전부 벗긴다.</para>
    /// <para>🔴 20261011작1 2차수 — [4] 검증 교정: 종전 주석은 「MySQL 8 의 <c>ALTER</c> 에는 이 토큰이
    /// 설 자리가 <b>아예 없으므로</b>」라고 적었으나 근거가 어긋난다(MySQL 8 은 <c>CREATE INDEX IF NOT EXISTS</c>
    /// 등도 모른다 — <c>ALTER</c> 만의 성질이 아니다). <c>ALTER</c> 로 한정하는 진짜 근거는
    /// <b>오늘 `installer/backoffice/*.sql` 14개의 사정</b>이다 — 이 토큰이 쓰인 곳이 <c>ALTER</c> 와
    /// <c>CREATE/DROP TABLE</c> 뿐이고, 후자는 MySQL 도 아는 문법이라 건드리면 안 된다.
    /// <c>CREATE INDEX IF NOT EXISTS</c> 는 오늘 0건이다 — 생기면 G-M8-5c 가 아니라 **적용 실패**로 드러난다.</para>
    /// </summary>
    private static readonly Regex ExistsTokenRegex = new(
        @"\GIF\s+(?:NOT\s+)?EXISTS\b\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>서버 판 문자열(<c>SELECT VERSION()</c>). 판독 실패 시 빈 문자열.</summary>
    private static async Task<string> QueryServerVersionAsync(MySqlConnection db, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand("SELECT VERSION()", db) { CommandTimeout = 30 };
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw as string ?? string.Empty;
    }

    /// <summary>
    /// MySQL 경로 — 파일을 문장으로 쪼개 하나씩 실행한다. <c>IF NOT EXISTS</c> 토큰은 벗기고,
    /// 그 대가로 생기는 「이미 있다」(1060·1061·1091)만 적용됨으로 보고 넘어간다.
    /// <para>그 밖의 오류는 <b>종전과 같이</b> 던져서 기동을 중단시킨다(#15·#19) —
    /// 스키마 불일치를 안고 돌면 런타임 500 이 숨는다.</para>
    /// </summary>
    private async Task ApplyForMySqlAsync(MySqlConnection db, string sql, string fileName, CancellationToken ct)
    {
        var statements = SplitStatements(sql);
        if (statements.Count == 0)
        {
            // 주석만 있는 파일 — 사고가 아니다. 다만 조용히 넘기지 않는다.
            _logger.LogWarning("[SchemaMigrator] {File} — 실행할 문장 0개(주석뿐)", fileName);
            return;
        }

        var tolerated = 0;
        for (var n = 0; n < statements.Count; n++)
        {
            var statement = StripAlterExistsTokens(statements[n]);
            try
            {
                await using var cmd = new MySqlCommand(statement, db) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (MySqlException ex) when (IsAlreadyThere(ex))
            {
                // 🔴 20261011작1 2차수 — [4] 검증이 찾은 구멍(V-1) 봉합.
                //   사고 모양: `32` 번은 **한 문장에 ADD COLUMN 4절**(`42` 번 첫 문장은 6절)이다.
                //   그 중 **한 칸만 이미 있어도** MySQL 은 문장 **전체를 롤백**하고 1060 을 낸다.
                //   그걸 그냥 「이미 적용됨」으로 넘기면 나머지 3칸(42면 5칸)이 **안 생긴 채**
                //   `_schema_migrations` 에 기록되고, 해시 멱등이 그 파일을 **영영 skip** 한다.
                //   ⇒ 적용기는 성공을 보고하는데 운영에는 칸이 없다 = 10/11 사고와 같은 500 이 조용히 돌아온다.
                //   (검증 실측: MariaDB 11.4.10 임시표에서 4절 중 가운데 1절만 중복이어도 나머지 3칸 미생성)
                //   봉합: 절 단위로 쪼개 다시 시도한다. 진짜 「이미 있는」 절만 넘어가고 나머지는 적용된다.
                var byClause = await TryApplyAlterClauseByClauseAsync(db, statements[n], fileName, n + 1, ct);
                tolerated++;
                _logger.LogInformation(
                    "[SchemaMigrator] {File} 문장 {No}/{Total} — 이미 적용됨으로 넘어감 (오류 {Code}{Retry})",
                    fileName, n + 1, statements.Count, (int)ex.ErrorCode,
                    byClause ? " · 절 단위 재시도 완료" : string.Empty);
            }
        }

        _logger.LogInformation(
            "[SchemaMigrator] {File} — MySQL 보정 경로: 문장 {Total}개 실행 · 「이미 있다」 {Tolerated}건",
            fileName, statements.Count, tolerated);
    }

    /// <summary>
    /// 「이미 있다」로 볼 오류만 넷 — 1060 중복 칸 · 1061 중복 인덱스 · 1091 없는 것 지우기 · 1826 중복 FK 이름.
    /// <para>🔴 1826 을 넣은 이유(게이트 G-M8-3 이 설계 중에 잡아낸 자리): <c>31</c> 번은
    /// <c>ADD CONSTRAINT fk_landing_signups_tenant</c> 를 <b>IF NOT EXISTS 없이</b> 쓴다.
    /// 파일이 중간에 실패하면 이력에 안 남아 다음 기동이 그 파일을 처음부터 다시 돌리는데,
    /// 그때 FK 이름 중복(1826)에서 또 멈춰 <b>영영 안 열린다</b>. 그 덫을 막는다.</para>
    /// <para>그 밖의 오류는 전부 던진다 — 스키마 불일치를 안고 도는 것이 더 위험하다(#19).</para>
    /// </summary>
    private static bool IsAlreadyThere(MySqlException ex)
        => (int)ex.ErrorCode is 1060 or 1061 or 1091 or 1826;

    /// <summary><c>ALTER TABLE &lt;표&gt;</c> 머리. 앞에 붙은 주석·공백을 함께 먹는다.</summary>
    private static readonly Regex AlterPrefixRegex = new(
        @"^(?:\s|--[^\n]*|\#[^\n]*|/\*.*?\*/)*ALTER\s+(?:ONLINE\s+)?(?:IGNORE\s+)?TABLE\s+(?:IF\s+EXISTS\s+)?(?:`[^`]+`|[A-Za-z0-9_$]+)(?:\s*\.\s*(?:`[^`]+`|[A-Za-z0-9_$]+))?",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// 🔴 20261011작1 2차수 (V-1 봉합) — 여러 절 <c>ALTER</c> 를 <b>절 단위로 쪼개 다시 시도</b>한다.
    /// <para>한 절의 「이미 있다」로 문장 전체가 롤백되면 나머지 절이 적용되지 않는데, 그걸 적용됨으로
    /// 기록하면 해시 멱등이 영영 건너뛴다. 절로 쪼개면 <b>진짜 있는 절만</b> 넘어가고 나머지는 적용된다.</para>
    /// <para>그 밖의 오류는 <b>던진다</b> — 종전과 같다(#19).</para>
    /// </summary>
    /// <returns>쪼개 돌렸으면 <c>true</c> · 쪼갤 게 없으면(절 1개 또는 <c>ALTER</c> 가 아니면) <c>false</c>.</returns>
    private async Task<bool> TryApplyAlterClauseByClauseAsync(
        MySqlConnection db, string statement, string fileName, int no, CancellationToken ct)
    {
        var clauses = SplitAlterClauses(statement, out var prefix);
        if (clauses.Count < 2)
        {
            return false;
        }

        var applied = 0;
        var already = 0;
        foreach (var clause in clauses)
        {
            try
            {
                await using var cmd = new MySqlCommand(
                    StripAlterExistsTokens($"{prefix} {clause}"), db) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync(ct);
                applied++;
            }
            catch (MySqlException ex) when (IsAlreadyThere(ex))
            {
                already++;
            }
        }

        _logger.LogInformation(
            "[SchemaMigrator] {File} 문장 {No} — 절 단위 재시도: 절 {Total}개 중 적용 {Applied} · 이미 있음 {Already}",
            fileName, no, clauses.Count, applied, already);
        return true;
    }

    /// <summary>
    /// <c>ALTER TABLE t ADD …, ADD …</c> 를 머리(<paramref name="prefix"/>)와 절 목록으로 가른다.
    /// 괄호 안·인용 안·주석 안의 쉼표는 경계가 아니다. <c>ALTER</c> 가 아니면 빈 목록.
    /// </summary>
    internal static List<string> SplitAlterClauses(string statement, out string prefix)
    {
        prefix = string.Empty;
        var clauses = new List<string>();

        var stripped = StripAlterExistsTokens(statement);
        var head = AlterPrefixRegex.Match(stripped);
        if (!head.Success)
        {
            return clauses;
        }

        prefix = head.Value.TrimEnd();
        var rest = stripped[head.Length..];

        var sb = new StringBuilder();
        var depth = 0;
        for (var i = 0; i < rest.Length; i++)
        {
            if (TryCopyVerbatim(rest, ref i, sb)) { continue; }

            var c = rest[i];
            if (c == '(') { depth++; }
            else if (c == ')') { depth--; }
            else if (c == ',' && depth == 0)
            {
                AddIfExecutable(clauses, sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        AddIfExecutable(clauses, sb.ToString());
        return clauses;
    }

    /// <summary>
    /// SQL 본문을 문장 단위로 쪼갠다. 인용 구간(<c>'</c> <c>"</c> <c>`</c>)과 주석 안의 <c>;</c> 는 경계가 아니다.
    /// <para>실측(2026-10-11 · installer/backoffice 14파일 전수): 문자열 안 <c>;</c> 0건 · 주석 안 <c>;</c> 0건.
    /// 그래도 6가지(<c>'</c> <c>"</c> <c>`</c> <c>--</c> <c>#</c> <c>/* */</c>)를 인식한다 — 다음에 누가 넣어도 안 깨지게.</para>
    /// </summary>
    internal static List<string> SplitStatements(string sql)
    {
        var list = new List<string>();
        var sb = new StringBuilder();

        for (var i = 0; i < sql.Length; i++)
        {
            // 주석·인용 구간은 통째로 옮긴다 — 그 안의 ; 는 문장 경계가 아니다.
            if (TryCopyVerbatim(sql, ref i, sb)) { continue; }

            if (sql[i] == ';')
            {
                AddIfExecutable(list, sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(sql[i]);
        }

        AddIfExecutable(list, sb.ToString());
        return list;
    }

    /// <summary>
    /// 지금 위치가 주석(<c>-- </c> · <c>#</c> · <c>/* */</c>)이나 인용 구간(<c>'</c> <c>"</c> <c>`</c>)의
    /// 시작이면 그 구간을 <paramref name="sb"/> 에 그대로 옮기고 <paramref name="i"/> 를 마지막 글자로 옮긴다.
    /// <para>쪼개기와 토큰 벗기기가 <b>같은 눈</b>으로 본문을 읽게 하려고 한 곳에 뒀다 —
    /// 두 쪽이 다르게 읽으면 한쪽이 가린 것을 다른 쪽이 고쳐버린다.</para>
    /// </summary>
    private static bool TryCopyVerbatim(string s, ref int i, StringBuilder sb)
    {
        var c = s[i];

        // 줄주석 `-- `(MySQL 은 -- 뒤에 공백/줄끝이어야 주석) · `#`
        var isDashComment = c == '-' && i + 1 < s.Length && s[i + 1] == '-'
                            && (i + 2 >= s.Length || s[i + 2] is ' ' or '\t' or '\r' or '\n');
        if (isDashComment || c == '#')
        {
            var nl = s.IndexOf('\n', i);
            var end = nl < 0 ? s.Length - 1 : nl;
            sb.Append(s, i, end - i + 1);
            i = end;
            return true;
        }

        // 블록주석 /* … */
        if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
        {
            var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
            var end = close < 0 ? s.Length - 1 : close + 1;
            sb.Append(s, i, end - i + 1);
            i = end;
            return true;
        }

        // 인용 구간
        if (c is '\'' or '"' or '`')
        {
            var j = i + 1;
            while (j < s.Length)
            {
                if (s[j] == '\\' && c != '`' && j + 1 < s.Length) { j += 2; continue; }
                if (s[j] == c)
                {
                    // 같은 인용부호 2개 = 이스케이프된 리터럴 — 구간이 안 끝난다
                    if (j + 1 < s.Length && s[j + 1] == c) { j += 2; continue; }
                    break;
                }
                j++;
            }
            var end = j < s.Length ? j : s.Length - 1;
            sb.Append(s, i, end - i + 1);
            i = end;
            return true;
        }

        return false;
    }

    private static void AddIfExecutable(List<string> list, string statement)
    {
        // 주석을 지우고도 글자가 남으면 실행문이다. (빈 문장을 보내면 MySQL 이 1065 로 거부한다)
        if (!string.IsNullOrWhiteSpace(CommentRegex.Replace(statement, " ")))
        {
            list.Add(statement.Trim());
        }
    }

    /// <summary>
    /// <c>ALTER</c> 문에서만 <c>IF NOT EXISTS</c> / <c>IF EXISTS</c> 토큰을 벗긴다.
    /// <c>CREATE</c>·<c>DROP TABLE</c> 등 다른 문장은 원문 그대로 돌려준다.
    /// </summary>
    internal static string StripAlterExistsTokens(string statement)
    {
        var bare = CommentRegex.Replace(statement, " ").TrimStart();
        if (!bare.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase))
        {
            return statement;
        }

        var sb = new StringBuilder(statement.Length);
        for (var i = 0; i < statement.Length; i++)
        {
            // 주석·인용 안의 글자는 고치지 않는다 — COMMENT '…' 안의 설명을 바꾸면 그게 DB 에 들어간다.
            if (TryCopyVerbatim(statement, ref i, sb)) { continue; }

            var c = statement[i];
            var wordStart = i == 0 || !(char.IsLetterOrDigit(statement[i - 1]) || statement[i - 1] == '_');
            if ((c is 'I' or 'i') && wordStart)
            {
                var m = ExistsTokenRegex.Match(statement, i);
                if (m.Success)
                {
                    i += m.Length - 1; // 토큰(+뒤 공백)을 버린다
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static async Task ExecuteAsync(MySqlConnection db, string sql, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(sql, db) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string?> QueryHashAsync(MySqlConnection db, string fileName, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(
            "SELECT content_hash FROM _schema_migrations WHERE file_name = @F", db);
        cmd.Parameters.AddWithValue("@F", fileName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    // ${VAR} 패턴을 환경변수로 치환. 미설정 변수가 하나라도 있으면 false(파일 건너뜀).
    //   - 예: 91_seed_admin.sql 의 '${HITPAN_BO_SEED_ADMIN_HASH}' → NCP env 주입값.
    //   - env 미설정 시 master 계정 시드를 건너뜀(빈 해시로 깨진 계정 INSERT 방지). 사고 아님 — 로그로 명시.
    private bool TrySubstituteEnv(string sql, string fileName, out string substituted)
    {
        substituted = sql;
        var pattern = new System.Text.RegularExpressions.Regex(@"\$\{([A-Z0-9_]+)\}");
        var matches = pattern.Matches(sql);
        if (matches.Count == 0)
        {
            return true; // 치환할 게 없으면 원본 그대로 실행
        }

        var missing = new List<string>();
        substituted = pattern.Replace(sql, m =>
        {
            var name = m.Groups[1].Value;
            var val = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(val))
            {
                missing.Add(name);
                return m.Value; // 그대로 둠(아래에서 건너뜀 처리)
            }
            return val;
        });

        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "[SchemaMigrator] {File} 건너뜀 — 환경변수 미설정: {Vars}. (값 주입 후 다음 기동 때 자동 적용)",
                fileName, string.Join(", ", missing.Distinct()));
            return false;
        }
        return true;
    }

    private static string ComputeSha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
