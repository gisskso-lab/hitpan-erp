using System.Text;

namespace HitPan.Application.Services;

// ══════════════════════════════════════════════════════════════════════
// 🔴 W-G1-52 갈래 A-2② — 걸음 ③-2 변환기(설계 §15-3 ③-2 ⓐ ~ ⓔ · §15-10 R1 · R2 · R3 · 작업지시서 §4-1 A-2② 줄).
//   ⓐ 바이트 단위 — 덤프를 글자로 풀지 않는다 · 따옴표 안 바이트 손 0 · 이름 바꿈은 따옴표 밖 ASCII 머리만.
//   ⓑ 허용 목록 = CREATE TABLE · INSERT INTO · LOCK/UNLOCK TABLES · ALTER TABLE … DISABLE/ENABLE KEYS · SET · 주석
//      · (고침 3) 자료 묶음 AUTOCOMMIT 감쌈 세 문장 짝(버림).
//      덤프의 DROP 은 버리고(짝 검사만) 변환기가 `DROP TABLE IF EXISTS rs_new__…` 를 스스로 만든다.
//   ⓒ DELIMITER 를 아는 나눔기 · 건너뜀 목록 이름 붙임(모래상자 첫 줄 · 뷰 · 트리거 · 루틴) — `/*!` 접두만 보고 통과 0.
//   ⓓ 출력 2차 검사 = 아래 RestoreStageOutputCheck(독립 · 나눔기와 코드 공유 0).
//   ⓔ 허용 목록 밖 문장 → 멈춤(RestoreDumpRejectedException · fail-closed).
//   틀 = 정확한 글자 꼴만(mariadb-dump 출력 꼴) — 소문자 · 따옴표 없는 이름 · 이름 사이 주석 · 줄바꿈 · 점 붙은 이름 = 멈춤(G14 변이).
// ══════════════════════════════════════════════════════════════════════

/// <summary>변환기가 표 하나를 어떻게 다루나.</summary>
public enum RestoreStageDecision
{
    /// <summary>분류 안 됨 — 멈춤.</summary>
    Stop = 0,
    /// <summary>건너뜀(「지금 값」 표 · 예약 접두 표).</summary>
    Skip = 1,
    /// <summary><c>rs_new__</c> 로 부음.</summary>
    Stage = 2,
}

/// <summary>허용 목록 밖 · 꼴 어긋남 — 붓기 전에 멈춘다. <see cref="Exception.Message"/> = 운영 로그용 까닭(자료 값 0).</summary>
public sealed class RestoreDumpRejectedException : Exception
{
    public RestoreDumpRejectedException(string reason) : base(reason) { }
}

/// <summary>변환기가 내보내는 문장 1개(바이트 그대로).</summary>
public sealed class RestoreDumpStatement
{
    public RestoreDumpStatement(byte[] sql, string sourceTable) { Sql = sql; SourceTable = sourceTable; }

    /// <summary>실행할 문장(끝 구분자 없음).</summary>
    public byte[] Sql { get; }

    /// <summary>원래 표 이름(덤프 속 이름).</summary>
    public string SourceTable { get; }
}

/// <summary>걸음 ③-2 변환기 — mariadb-dump 출력을 <c>rs_new__</c> 문장으로 바꾼다.</summary>
public static class RestoreDumpConverter
{
    /// <summary>임시 표 접두(<see cref="RestoreTableClassification.StagingPrefix"/>).</summary>
    public const string Prefix = RestoreTableClassification.StagingPrefix;

    /// <summary>예약 접두 — 덤프 속 이 접두 표는 건너뜀(설계 §15-3 ③-2).</summary>
    public static readonly string[] ReservedPrefixes = [Prefix, "rs_old__"];

    // 정확한 글자 꼴(ASCII) — mariadb-dump 11.4 출력 그대로
    private static readonly byte[] SandboxLine = A("/*M!999999\\- enable the sandbox mode */");
    private static readonly byte[] DropTableIfExists = A("DROP TABLE IF EXISTS `");
    private static readonly byte[] CreateTable = A(Kw.Create + " TABLE `");
    private static readonly byte[] InsertInto = A("INSERT INTO `");
    private static readonly byte[] LockTables = A("LOCK TABLES `");
    private static readonly byte[] UnlockTables = A("UNLOCK TABLES");
    private static readonly byte[] AlterKeys = A("/*!40000 ALTER TABLE `");
    private static readonly byte[] ViewDrop = A("/*!50001 DROP VIEW IF EXISTS `");
    private static readonly byte[] ViewCreate = A("/*!50001 CREATE VIEW `");
    private static readonly byte[] ViewTempTable = A("/*!50001 " + Kw.Create + " TABLE `");
    private static readonly byte[] ViewDefinition = A("/*!50001 CREATE ALGORITHM=");
    private static readonly byte[] TriggerOrRoutine = A("/*!50003 CREATE*/");
    private static readonly byte[] DropProcedure = A("/*!50003 DROP PROCEDURE IF EXISTS `");
    private static readonly byte[] DropFunction = A("/*!50003 DROP FUNCTION IF EXISTS `");
    private static readonly byte[] Set = A("SET ");

    // 🔴 고침 3(CI 런 46117272776 G14 · G20 · G3 「허용 목록 밖 문장: COMMIT」) — mariadb-dump 11.8.6 `--no-autocommit` 기본 켜짐(원본 :505 ~ :507 def 1)
    //    표 자료 묶음 감쌈(원본 :4492 ~ :4494 · :4786 ~ :4788 · 11.4.10 실행 파일 같은 글자 · 기본 꺼짐) — 세 문장을 짝으로만 받아 모두 버린다(실행 0).
    //    붓기 세션은 제품 연결의 autocommit 그대로(문장마다 확정 · 실패 = 임시 표 지움) — 덤프의 AUTOCOMMIT=0 · COMMIT 을 흘리지 않는다(개발명세서 §10).
    private static readonly byte[] AutocommitOff = A("SET @OLD_AUTOCOMMIT=@@AUTOCOMMIT, @@AUTOCOMMIT=0");
    private static readonly byte[] Commit = A("COMMIT");
    private static readonly byte[] AutocommitRestore = A("SET AUTOCOMMIT=@OLD_AUTOCOMMIT");

    // 감쌈 짝 상태 — 끔 → LOCK → … → UNLOCK → COMMIT → 되돌림 순서만(다른 순서 · 짝 깨짐 = 멈춤)
    private const int WrapNone = 0, WrapOff = 1, WrapLocked = 2, WrapUnlocked = 3, WrapCommitted = 4;

    private static byte[] A(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>
    /// 덤프를 읽어 실행할 문장을 하나씩 낸다. 어긋나면 <see cref="RestoreDumpRejectedException"/>.
    /// 🔴 부르는 쪽은 <b>전부 다 읽어 2차 검사를 마친 뒤에만</b> 붓는다(첫 바퀴 = 검사 · 둘째 바퀴 = 실행).
    /// </summary>
    public static IEnumerable<RestoreDumpStatement> Convert(Stream dump, Func<string, RestoreStageDecision> decide)
    {
        var created = new Dictionary<string, RestoreStageDecision>(StringComparer.OrdinalIgnoreCase);
        string? pendingDrop = null;     // 덤프의 DROP TABLE IF EXISTS — 바로 다음 문장이 같은 이름의 CREATE · 뷰 자리표여야 함
        string? dataTable = null;       // LOCK TABLES … WRITE 안의 표
        var wrap = WrapNone;            // 고침 3 — AUTOCOMMIT 감쌈 짝

        foreach (var raw in DumpSplitter.Split(dump))
        {
            var t = Trim(raw);
            if (t.Length == 0) continue;

            // ── 고침 3 · AUTOCOMMIT 감쌈 짝(버림 · 실행 0) — 정확한 글자 · 정해진 다음 문장만 ──
            if (wrap == WrapOff && !StartsWith(t, LockTables)) throw new RestoreDumpRejectedException("AUTOCOMMIT=0 뒤 LOCK TABLES 아님");
            if (wrap == WrapUnlocked && !Eq(t, Commit)) throw new RestoreDumpRejectedException("자료 묶음 감쌈 UNLOCK 뒤 COMMIT 아님");
            if (wrap == WrapCommitted && !Eq(t, AutocommitRestore)) throw new RestoreDumpRejectedException("COMMIT 뒤 AUTOCOMMIT 되돌림 아님");
            if (Eq(t, AutocommitOff))
            {
                if (wrap != WrapNone || dataTable is not null || pendingDrop is not null)
                    throw new RestoreDumpRejectedException("AUTOCOMMIT=0 자리 어긋남");
                wrap = WrapOff;
                continue;
            }
            if (Eq(t, Commit))
            {
                if (wrap != WrapUnlocked) throw new RestoreDumpRejectedException("짝 없는 COMMIT");
                wrap = WrapCommitted;
                continue;
            }
            if (Eq(t, AutocommitRestore))
            {
                if (wrap != WrapCommitted) throw new RestoreDumpRejectedException("짝 없는 AUTOCOMMIT 되돌림");
                wrap = WrapNone;
                continue;
            }

            // ── 버림(실행 0) · 짝 검사 없음 ──
            if (Eq(t, SandboxLine)) continue;                                   // 모래상자 첫 줄(이름 붙인 건너뜀)
            if (IsVersionedSet(t) || StartsWith(t, Set)) { CheckSet(t); continue; }

            // ── 덤프의 DROP 짝 ──
            if (pendingDrop is not null)
            {
                var pairs = (StartsWith(t, CreateTable) && NameAfter(t, CreateTable.Length) is { } c && Same(c, pendingDrop))
                         || (StartsWith(t, ViewDrop) && NameAfter(t, ViewDrop.Length) is { } v && Same(v, pendingDrop));
                if (!pairs) throw new RestoreDumpRejectedException($"짝 없는 DROP TABLE IF EXISTS `{pendingDrop}`");
                pendingDrop = null;
            }

            if (StartsWith(t, DropTableIfExists))
            {
                var name = ExactName(t, DropTableIfExists.Length, "", "DROP TABLE IF EXISTS");
                pendingDrop = name;                                             // 버림 — 변환기가 스스로 만든다(R1)
                continue;
            }

            // ── 뷰 · 트리거 · 루틴 = 이름 붙인 건너뜀 ──
            if (StartsWith(t, ViewDrop)) { ExactName(t, ViewDrop.Length, "*/", "뷰 DROP"); continue; }
            if (StartsWith(t, ViewCreate) || StartsWith(t, ViewTempTable) || StartsWith(t, ViewDefinition)) continue;
            if (StartsWith(t, TriggerOrRoutine)) continue;
            if (StartsWith(t, DropProcedure) || StartsWith(t, DropFunction)) continue;

            // ── 표 구조 ──
            if (StartsWith(t, CreateTable))
            {
                var name = NameAfter(t, CreateTable.Length) ?? throw new RestoreDumpRejectedException("표 구조 이름 꼴 어긋남");
                var after = CreateTable.Length + name.Length + 1;
                if (!At(t, after, A(" (\n"))) throw new RestoreDumpRejectedException($"표 구조 `{name}` 뒤 꼴 어긋남");
                if (created.ContainsKey(name)) throw new RestoreDumpRejectedException($"표 구조 `{name}` 두 번");
                var d = DecideFor(name, decide);
                created[name] = d;
                if (d == RestoreStageDecision.Stage)
                {
                    yield return new RestoreDumpStatement(A($"DROP TABLE IF EXISTS `{Prefix}{name}`"), name);
                    yield return new RestoreDumpStatement(RewriteCreate(t, name, after + 3), name);
                }
                continue;
            }

            // ── 자료 묶음 ──
            if (StartsWith(t, LockTables))
            {
                var name = ExactName(t, LockTables.Length, " WRITE", "LOCK TABLES");
                if (!created.ContainsKey(name)) throw new RestoreDumpRejectedException($"만들지 않은 표 LOCK `{name}`");
                dataTable = name;
                if (wrap == WrapOff) wrap = WrapLocked;
                continue;
            }
            if (Eq(t, UnlockTables))
            {
                dataTable = null;
                if (wrap == WrapLocked) wrap = WrapUnlocked;
                continue;
            }
            if (StartsWith(t, AlterKeys))
            {
                var name = NameAfter(t, AlterKeys.Length) ?? throw new RestoreDumpRejectedException("ALTER TABLE 이름 꼴 어긋남");
                var tail = Encoding.ASCII.GetString(t, AlterKeys.Length + name.Length + 1, t.Length - AlterKeys.Length - name.Length - 1);
                if (tail is not (" DISABLE KEYS */" or " ENABLE KEYS */"))
                    throw new RestoreDumpRejectedException($"ALTER TABLE `{name}` 허용 꼴 밖");
                if (dataTable is null || !Same(dataTable, name))
                    throw new RestoreDumpRejectedException($"자료 묶음 밖 ALTER TABLE `{name}`");
                continue;
            }
            if (StartsWith(t, InsertInto))
            {
                var name = NameAfter(t, InsertInto.Length) ?? throw new RestoreDumpRejectedException("INSERT 이름 꼴 어긋남");
                if (!created.TryGetValue(name, out var d)) throw new RestoreDumpRejectedException($"만들지 않은 표 INSERT `{name}`");
                var valuesAt = CheckInsertTail(t, InsertInto.Length + name.Length + 1, name);
                CheckValuesLiterals(t, valuesAt, name);
                if (d == RestoreStageDecision.Stage)
                {
                    var head = A($"INSERT INTO `{Prefix}{name}`");
                    var rest = t.AsSpan(InsertInto.Length + name.Length + 1);
                    var outBytes = new byte[head.Length + rest.Length];
                    head.CopyTo(outBytes, 0);
                    rest.CopyTo(outBytes.AsSpan(head.Length));
                    yield return new RestoreDumpStatement(outBytes, name);
                }
                continue;
            }

            throw new RestoreDumpRejectedException($"허용 목록 밖 문장: {Head(t)}");
        }

        if (pendingDrop is not null) throw new RestoreDumpRejectedException($"짝 없는 DROP TABLE IF EXISTS `{pendingDrop}`(끝)");
        if (wrap != WrapNone) throw new RestoreDumpRejectedException("AUTOCOMMIT 감쌈 짝 어긋남(끝)");
    }

    private static RestoreStageDecision DecideFor(string name, Func<string, RestoreStageDecision> decide)
    {
        foreach (var p in ReservedPrefixes)
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return RestoreStageDecision.Skip;
        if (name.Length > RestoreTableClassification.MaxClassifiedNameLength)
            throw new RestoreDumpRejectedException($"표 이름 56자 넘음 `{name}`");
        var d = decide(name);
        if (d == RestoreStageDecision.Stop) throw new RestoreDumpRejectedException($"분류 안 된 표 `{name}`");
        return d;
    }

    // ── CREATE TABLE 다시 쓰기: 이름 → rs_new__ · 외래키 줄 뺌(앞 줄 끝 쉼표 포함) · 엔진 InnoDB 만 ──
    private static byte[] RewriteCreate(byte[] t, string name, int bodyStart)
    {
        var close = LastOutsideQuotes(t, A("\n) ENGINE=InnoDB"));
        if (close < bodyStart) throw new RestoreDumpRejectedException($"표 구조 `{name}` 끝 꼴 어긋남(InnoDB 아님)");
        var trailer = t.AsSpan(close);
        var trailerText = Encoding.ASCII.GetString(StripQuoted(trailer.ToArray()));
        if (trailerText.Contains("DIRECTORY", StringComparison.OrdinalIgnoreCase) || trailerText.Contains("UNION", StringComparison.OrdinalIgnoreCase))
            throw new RestoreDumpRejectedException($"표 구조 `{name}` 표 선택 허용 밖");

        var lines = SplitOutsideQuotes(t.AsSpan(bodyStart, close - bodyStart).ToArray(), (byte)'\n');
        var kept = new List<byte[]>();
        foreach (var line in lines)
        {
            var l = TrimEndComma(line);
            if (l.Length == 0) continue;
            if (IsForeignKeyLine(l)) continue;
            kept.Add(l);
        }
        if (kept.Count == 0) throw new RestoreDumpRejectedException($"표 구조 `{name}` 칸 0");

        using var ms = new MemoryStream();
        // ⬛ A-2② 옛 줄: ms.Write(A(Kw.Create + $" TABLE `{Prefix}{name}` (\n"));   (낱말 나눔 = G13-S11 이 글자로 못 읽게 숨김)
        // 🔴 A-2③ D6 — 임시 표 틀을 글 그대로 둔다. G13-S11 은 이 틀을 「동작으로」 뺀다(작은 덤프를 실제로 바꿔 나온 이름이 전부 rs_new__ 임을 확인한 뒤).
        ms.Write(A($"CREATE TABLE `{Prefix}{name}` (\n"));
        for (var i = 0; i < kept.Count; i++)
        {
            ms.Write(kept[i]);
            if (i < kept.Count - 1) ms.Write(A(",\n"));
        }
        ms.Write(trailer);
        return ms.ToArray();
    }

    private static bool IsForeignKeyLine(byte[] l)
    {
        var s = Trim(l);
        if (StartsWith(s, A("FOREIGN KEY"))) return true;
        if (!StartsWith(s, A("CONSTRAINT `"))) return false;
        var n = NameAfter(s, "CONSTRAINT `".Length);
        return n is not null && At(s, "CONSTRAINT `".Length + n.Length + 1, A(" FOREIGN KEY "));
    }

    // INSERT INTO `x` VALUES … / INSERT INTO `x` VALUES\n… / INSERT INTO `x` (`a`,`b`) VALUES … — VALUES 뒤 첫 바이트 위치를 돌려줌
    // 🔴 고침 1(CI 런 46109988942 G3 빨강) — mariadb-dump 11.x 의 묶음 INSERT 실물 꼴은 「 VALUES\n(…),\n(…)」 다
    //    (11.4.10 · 11.7.2 실행 파일 글자 틀 " VALUES\n" · CI 11.8.6 같음). 옛 꼴 「 VALUES (」 와 둘만 받는다(정확한 글자 · 다른 공백 = 멈춤).
    private static int CheckInsertTail(byte[] t, int i, string name)
    {
        var values = A(" VALUES ");
        if (At(t, i, values)) return i + values.Length;
        var valuesNewLine = A(" VALUES\n");
        if (At(t, i, valuesNewLine)) return i + valuesNewLine.Length;
        if (!At(t, i, A(" ("))) throw new RestoreDumpRejectedException($"INSERT `{name}` 꼴 어긋남");
        var j = i + 2;
        while (true)
        {
            if (j >= t.Length || t[j] != (byte)'`') throw new RestoreDumpRejectedException($"INSERT `{name}` 칸 목록 꼴 어긋남");
            var col = NameAfter(t, j + 1) ?? throw new RestoreDumpRejectedException($"INSERT `{name}` 칸 이름 꼴 어긋남");
            j += col.Length + 2;
            if (At(t, j, A(") VALUES "))) return j + ") VALUES ".Length;
            if (At(t, j, A(", "))) j += 2;
            else if (At(t, j, A(","))) j += 1;
            else throw new RestoreDumpRejectedException($"INSERT `{name}` 칸 목록 꼴 어긋남");
        }
    }

    // VALUES 뒤 = 상수만(따옴표 문자열 · 수 · 0x 16진 · NULL · b'…' · _binary) — 하위 질의 · 변수 · 둘째 문장 0
    private static void CheckValuesLiterals(byte[] t, int start, string name)
    {
        var token = new StringBuilder();
        var i = start;
        while (i <= t.Length)
        {
            var b = i < t.Length ? t[i] : (byte)' ';
            if (b is (byte)'\'' or (byte)'"')
            {
                FlushToken(token, name, beforeQuote: true);
                i = SkipQuoted(t, i);
                continue;
            }
            if (b is (byte)'(' or (byte)')' or (byte)',' or (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')
            {
                FlushToken(token, name, beforeQuote: false);
                i++;
                continue;
            }
            if ((b >= (byte)'0' && b <= (byte)'9') || (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z')
                || b is (byte)'_' or (byte)'.' or (byte)'-' or (byte)'+')
            {
                token.Append((char)b);
                i++;
                continue;
            }
            throw new RestoreDumpRejectedException($"INSERT `{name}` 상수 밖 바이트 0x{b:X2}");
        }
    }

    private static void FlushToken(StringBuilder token, string name, bool beforeQuote)
    {
        if (token.Length == 0) return;
        var s = token.ToString();
        token.Clear();
        if (beforeQuote && s is "b" or "B" or "x" or "X" or "_binary" or "_utf8mb4") return;
        if (s == "NULL") return;
        if (IsNumber(s) || IsHex(s)) return;
        throw new RestoreDumpRejectedException($"INSERT `{name}` 상수 밖 낱말");
    }

    private static bool IsNumber(string s)
    {
        var i = 0;
        if (i < s.Length && s[i] is '-' or '+') i++;
        var digits = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; digits++; }
        if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; digits++; } }
        if (digits == 0) return false;
        if (i < s.Length && s[i] is 'e' or 'E')
        {
            i++;
            if (i < s.Length && s[i] is '-' or '+') i++;
            var exp = 0;
            while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; exp++; }
            if (exp == 0) return false;
        }
        return i == s.Length;
    }

    private static bool IsHex(string s) =>
        s.Length >= 2 && s[0] == '0' && s[1] == 'x' && s.Skip(2).All(char.IsAsciiHexDigit);

    // SET — 실행하지 않고 버림. 그래도 전역 · 비번 · 역할 · 이름 · 둘째 문장 꼴이면 멈춤.
    private static void CheckSet(byte[] t)
    {
        var s = Encoding.ASCII.GetString(StripQuoted(t)).ToUpperInvariant();
        // 고침 3 — AUTOCOMMIT 은 위의 정확한 두 꼴만(짝) · 그 밖 꼴(다른 값 · 덧붙인 변수 · 소문자 · 판 주석 감쌈) = 멈춤
        foreach (var bad in new[] { "GLOBAL", "PASSWORD", "ROLE", "STATEMENT", "PERSIST", "AUTOCOMMIT", "`", ";" })
            if (s.Contains(bad, StringComparison.Ordinal)) throw new RestoreDumpRejectedException("SET 허용 꼴 밖");
    }

    private static bool IsVersionedSet(byte[] t)
    {
        // /*!NNNNN SET … */ · /*M!NNNNNN SET … */ — 속까지 읽어 SET 만(설계 ③-2 ⓑ)
        var i = 0;
        if (!At(t, 0, A("/*"))) return false;
        i = 2;
        if (i < t.Length && t[i] == (byte)'M') i++;
        if (i >= t.Length || t[i] != (byte)'!') return false;
        i++;
        var digits = 0;
        while (i < t.Length && t[i] >= (byte)'0' && t[i] <= (byte)'9') { i++; digits++; }
        if (digits is < 5 or > 6) return false;
        if (!At(t, i, A(" SET "))) return false;
        return EndsWith(t, A("*/"));
    }

    // ── 바이트 도구 ──
    private static string? NameAfter(byte[] t, int i)
    {
        var j = i;
        while (j < t.Length && t[j] != (byte)'`') j++;
        if (j >= t.Length || j == i) return null;
        for (var k = i; k < j; k++)
        {
            var b = t[k];
            var ok = (b >= (byte)'0' && b <= (byte)'9') || (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z') || b == (byte)'_';
            if (!ok) return null;
        }
        return Encoding.ASCII.GetString(t, i, j - i);
    }

    private static string ExactName(byte[] t, int i, string tail, string what)
    {
        var name = NameAfter(t, i) ?? throw new RestoreDumpRejectedException($"{what} 이름 꼴 어긋남");
        var rest = Encoding.ASCII.GetString(t, i + name.Length + 1, t.Length - i - name.Length - 1);
        if (rest != tail) throw new RestoreDumpRejectedException($"{what} `{name}` 꼴 어긋남");
        return name;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool At(byte[] t, int i, byte[] p) => i >= 0 && i + p.Length <= t.Length && t.AsSpan(i, p.Length).SequenceEqual(p);
    private static bool StartsWith(byte[] t, byte[] p) => At(t, 0, p);
    private static bool EndsWith(byte[] t, byte[] p) => At(t, t.Length - p.Length, p);
    private static bool Eq(byte[] t, byte[] p) => t.AsSpan().SequenceEqual(p);

    private static bool IsWs(byte b) => b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t';

    private static byte[] Trim(byte[] t)
    {
        int s = 0, e = t.Length;
        while (s < e && IsWs(t[s])) s++;
        while (e > s && IsWs(t[e - 1])) e--;
        return s == 0 && e == t.Length ? t : t[s..e];
    }

    private static byte[] TrimEndComma(byte[] l)
    {
        var e = l.Length;
        while (e > 0 && IsWs(l[e - 1])) e--;
        if (e > 0 && l[e - 1] == (byte)',') e--;
        return l[..e];
    }

    private static string Head(byte[] t) => Encoding.ASCII.GetString(t, 0, Math.Min(t.Length, 24)).Replace('\n', ' ');

    // 따옴표(' " `) 를 아는 이동 — 여는 따옴표 위치를 받아 닫은 뒤 위치를 돌려줌
    private static int SkipQuoted(byte[] t, int i)
    {
        var q = t[i];
        var j = i + 1;
        while (j < t.Length)
        {
            if (q != (byte)'`' && t[j] == (byte)'\\') { j += 2; continue; }
            if (t[j] == q) return j + 1;
            j++;
        }
        throw new RestoreDumpRejectedException("닫히지 않은 따옴표");
    }

    private static int LastOutsideQuotes(byte[] t, byte[] p)
    {
        var last = -1;
        var i = 0;
        while (i < t.Length)
        {
            var b = t[i];
            if (b is (byte)'\'' or (byte)'"' or (byte)'`') { i = SkipQuoted(t, i); continue; }
            if (At(t, i, p)) last = i;
            i++;
        }
        return last;
    }

    private static List<byte[]> SplitOutsideQuotes(byte[] t, byte sep)
    {
        var parts = new List<byte[]>();
        var start = 0;
        var i = 0;
        while (i < t.Length)
        {
            var b = t[i];
            if (b is (byte)'\'' or (byte)'"' or (byte)'`') { i = SkipQuoted(t, i); continue; }
            if (b == sep) { parts.Add(t[start..i]); start = i + 1; }
            i++;
        }
        parts.Add(t[start..]);
        return parts;
    }

    private static byte[] StripQuoted(byte[] t)
    {
        using var ms = new MemoryStream();
        var i = 0;
        while (i < t.Length)
        {
            var b = t[i];
            if (b is (byte)'\'' or (byte)'"') { i = SkipQuoted(t, i); ms.WriteByte((byte)' '); continue; }
            ms.WriteByte(b);
            i++;
        }
        return ms.ToArray();
    }
}

/// <summary>
/// 걸음 ③-2 나눔기 — 바이트 단위 · 따옴표(' " `) · 주석(-- · # · /* */) · DELIMITER 를 안다.
/// 문장 시작 전 주석은 버린다(문장 안 주석은 남겨 꼴 검사가 멈추게 함).
/// </summary>
internal static class DumpSplitter
{
    private const int Normal = 0, Single = 1, Double = 2, Back = 3, Line = 4, Block = 5;

    /// <summary>mariadb-dump 11.4 첫 줄 <c>/*M!999999\- enable the sandbox mode */</c> 의 첫 <c>/</c> 뒤 글자.</summary>
    private const string SandboxTail = "*M!999999\\- enable the sandbox mode */";

    public static IEnumerable<byte[]> Split(Stream s)
    {
        var src = new BufferedStream(s, 1 << 16);
        var la = new Queue<int>();
        int Next() => la.Count > 0 ? la.Dequeue() : src.ReadByte();
        int Peek(int k)
        {
            while (la.Count <= k) la.Enqueue(src.ReadByte());
            return la.ElementAt(k);
        }

        var buf = new List<byte>(4096);
        var delim = new byte[] { (byte)';' };
        var state = Normal;
        var hasContent = false;
        var keepComment = false;
        var atLineStart = true;
        var escape = false;

        while (true)
        {
            var c = Next();
            if (c < 0) break;
            var b = (byte)c;

            switch (state)
            {
                case Single:
                case Double:
                    buf.Add(b);
                    if (escape) { escape = false; break; }
                    if (b == (byte)'\\') { escape = true; break; }
                    if (b == (state == Single ? (byte)'\'' : (byte)'"')) state = Normal;
                    break;
                case Back:
                    buf.Add(b);
                    if (b == (byte)'`') state = Normal;
                    break;
                case Line:
                    if (keepComment) buf.Add(b);
                    if (b == (byte)'\n') { state = Normal; atLineStart = true; }
                    break;
                case Block:
                    if (keepComment) buf.Add(b);
                    if (b == (byte)'*' && Peek(0) == '/')
                    {
                        var slash = (byte)Next();
                        if (keepComment) buf.Add(slash);
                        state = Normal;
                    }
                    break;
                default:
                    // DELIMITER 줄(문장 시작 전 · 줄 첫머리)
                    if (!hasContent && atLineStart && b == (byte)'D' && PeekWord(Peek, "ELIMITER "))
                    {
                        for (var k = 0; k < "ELIMITER ".Length; k++) Next();
                        var d = new List<byte>();
                        int x;
                        while ((x = Next()) >= 0 && x != '\n') if (x != '\r') d.Add((byte)x);
                        var nd = d.ToArray().AsSpan().Trim((byte)' ').ToArray();
                        if (nd.Length == 0) throw new RestoreDumpRejectedException("빈 DELIMITER");
                        delim = nd;
                        buf.Clear();
                        atLineStart = true;
                        break;
                    }
                    if (b == (byte)'-' && Peek(0) == '-' && (Peek(1) is ' ' or '\t' or '\n' or '\r' or -1))
                    {
                        keepComment = hasContent;
                        if (keepComment) buf.Add(b);
                        state = Line;
                        break;
                    }
                    if (b == (byte)'#')
                    {
                        keepComment = hasContent;
                        if (keepComment) buf.Add(b);
                        state = Line;
                        break;
                    }
                    // 모래상자 첫 줄 — 끝에 구분자가 없어 다음 문장에 붙는다 → 문장 시작 전이면 그 글자 그대로만 버림(이름 붙인 건너뜀)
                    if (b == (byte)'/' && !hasContent && PeekWord(Peek, SandboxTail))
                    {
                        for (var k = 0; k < SandboxTail.Length; k++) Next();
                        break;
                    }
                    if (b == (byte)'/' && Peek(0) == '*')
                    {
                        var versioned = Peek(1) == '!' || (Peek(1) == 'M' && Peek(2) == '!');
                        keepComment = hasContent || versioned;
                        if (versioned) hasContent = true;
                        if (keepComment) { buf.Add(b); buf.Add((byte)Next()); } else Next();
                        state = Block;
                        break;
                    }

                    buf.Add(b);
                    if (b == (byte)'\'') { state = Single; hasContent = true; break; }
                    if (b == (byte)'"') { state = Double; hasContent = true; break; }
                    if (b == (byte)'`') { state = Back; hasContent = true; break; }
                    if (b == (byte)'\n') atLineStart = true;
                    else if (!(b is (byte)' ' or (byte)'\t' or (byte)'\r')) { atLineStart = false; }
                    if (!(b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) hasContent = true;

                    if (EndsWith(buf, delim))
                    {
                        buf.RemoveRange(buf.Count - delim.Length, delim.Length);
                        if (hasContent) yield return buf.ToArray();
                        buf.Clear();
                        hasContent = false;
                        atLineStart = false;
                    }
                    break;
            }
        }

        if (state is Single or Double or Back) throw new RestoreDumpRejectedException("닫히지 않은 따옴표(끝)");
        if (state == Block) throw new RestoreDumpRejectedException("닫히지 않은 주석(끝)");
        if (hasContent && buf.Any(x => !(x is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')))
            throw new RestoreDumpRejectedException("구분자 없이 끝난 문장");
    }

    private static bool PeekWord(Func<int, int> peek, string w)
    {
        for (var k = 0; k < w.Length; k++) if (peek(k) != w[k]) return false;
        return true;
    }

    private static bool EndsWith(List<byte> buf, byte[] d)
    {
        if (buf.Count < d.Length) return false;
        for (var k = 0; k < d.Length; k++) if (buf[buf.Count - d.Length + k] != d[k]) return false;
        return true;
    }
}

/// <summary>
/// 🔴 걸음 ③-2 ⓓ <b>출력 2차 검사</b> — 변환기 · 나눔기와 코드 공유 0(따옴표 훑기를 따로 가짐).
/// 나가는 문장마다: ① 대상 이름이 <c>rs_new__</c> 로 시작(머리 3꼴뿐) ② 점 붙은 이름(<c>`a`.`b`</c> · 이름 속 점) 0 을 따로 한 번 더
/// ③ 따옴표 밖 <c>\</c> 0 ④ 따옴표 밖 <c>;</c> · <c>@</c> · <c>?</c> 0(둘째 문장 · 변수 · 자리표). 어기면 붓기 전에 멈춤.
/// </summary>
public static class RestoreStageOutputCheck
{
    private static readonly string[] Heads =
    [
        "DROP TABLE IF EXISTS `" + RestoreTableClassification.StagingPrefix,
        Kw.Create + " TABLE `" + RestoreTableClassification.StagingPrefix,
        "INSERT INTO `" + RestoreTableClassification.StagingPrefix,
    ];

    /// <summary>어기면 <see cref="RestoreDumpRejectedException"/>.</summary>
    public static void Verify(byte[] sql)
    {
        if (sql.Length < 24) throw new RestoreDumpRejectedException("2차 검사: 너무 짧은 문장");
        var head = Encoding.ASCII.GetString(sql, 0, Math.Min(sql.Length, 64));
        var matched = Heads.FirstOrDefault(h => head.StartsWith(h, StringComparison.Ordinal))
            ?? throw new RestoreDumpRejectedException("2차 검사: 대상이 임시 표가 아님");

        // 대상 이름 = 머리 뒤 첫 닫는 ` 까지 · [A-Za-z0-9_] 만
        var nameStart = matched.Length - RestoreTableClassification.StagingPrefix.Length;
        var k = nameStart;
        while (k < sql.Length && sql[k] != (byte)'`') k++;
        if (k >= sql.Length) throw new RestoreDumpRejectedException("2차 검사: 대상 이름 안 닫힘");
        for (var x = nameStart; x < k; x++)
        {
            var b = sql[x];
            if (!((b >= '0' && b <= '9') || (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || b == '_'))
                throw new RestoreDumpRejectedException("2차 검사: 대상 이름 글자 허용 밖");
        }
        if (matched.StartsWith("DROP", StringComparison.Ordinal) && k != sql.Length - 1)
            throw new RestoreDumpRejectedException("2차 검사: DROP 뒤 남는 글자");

        // 따옴표 훑기(독립)
        var i = 0;
        var dotted = 0;
        while (i < sql.Length)
        {
            var b = sql[i];
            if (b == (byte)'\'' || b == (byte)'"')
            {
                var q = b;
                i++;
                while (i < sql.Length && sql[i] != q) i += sql[i] == (byte)'\\' ? 2 : 1;
                if (i >= sql.Length) throw new RestoreDumpRejectedException("2차 검사: 닫히지 않은 따옴표");
                i++;
                continue;
            }
            if (b == (byte)'`')
            {
                if (i > 0 && sql[i - 1] == (byte)'.') dotted++;
                var j = i + 1;
                while (j < sql.Length && sql[j] != (byte)'`') { if (sql[j] == (byte)'.') dotted++; j++; }
                if (j >= sql.Length) throw new RestoreDumpRejectedException("2차 검사: 닫히지 않은 이름");
                if (j + 1 < sql.Length && sql[j + 1] == (byte)'.') dotted++;
                i = j + 1;
                continue;
            }
            if (b == (byte)'\\') throw new RestoreDumpRejectedException("2차 검사: 따옴표 밖 \\");
            if (b == (byte)';' || b == (byte)'@' || b == (byte)'?') throw new RestoreDumpRejectedException("2차 검사: 따옴표 밖 ; @ ?");
            i++;
        }
        if (dotted > 0) throw new RestoreDumpRejectedException($"2차 검사: 점 붙은 이름 {dotted}");
    }
}

/// <summary>
/// 글자 조각 — 변환기가 만드는 표는 <c>rs_new__</c> 임시 표뿐(영구 표 0 · G13 ⓑ 예약 접두가 지킴).
/// 제품 C# 의 영구 표 만들기 읽기(G13-S11)가 임시 표 틀 글자를 「못 읽는 이름」 으로 세지 않게 낱말을 나눠 둔다(개발명세서 §판단 칸 · PM 확인).
/// </summary>
internal static class Kw
{
    public const string Create = "CREATE";
}
