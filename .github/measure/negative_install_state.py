#!/usr/bin/env python3
# 계측 전용 — 머지 금지. 작업리뷰서 §5 음성 대조 V-5.
# 변이마다 따로: 적용(겹마다 1건 확인) → 빌드 → 설치 칸 게이트 전체 → 빨강 시험 대조 → 원복.
# 컴파일 실패 · 0건 치환은 증거가 아니다 — 그 변이를 「무효」로 적고 끝에 실패로 낸다.
import pathlib
import re
import subprocess
import sys
import time

READER = "src/HitPan.Backoffice.API/Services/TenantInstallStateReader.cs"
CTRL = "src/HitPan.Backoffice.API/Controllers/TenantsAdminController.cs"
TEXT = "src/HitPan.Backoffice/Components/Pages/Platform/Components/TenantInstallText.cs"
TESTS = "src/HitPan.Tests/HitPan.Tests.csproj"
FILTER = "FullyQualifiedName~BackofficeInstallStateGate"
ALL = [
    "설치①", "설치②", "설치③", "설치④", "설치⑤", "설치⑥", "설치⑦",
    "G-6ⓐ1", "G-6ⓐ2", "G-6ⓑ", "G-6ⓒ", "G-6ⓓ", "G-6ⓔ",
    "G-7 판정식", "G-7 🔴 낱말", "G-7 시각", "G-7 🔴 금지", "G-7 구판", "G-8",
]
G6 = ["G-6ⓐ1", "G-6ⓐ2", "G-6ⓑ", "G-6ⓒ", "G-6ⓓ", "G-6ⓔ"]

U_EXISTS = """CASE WHEN EXISTS (SELECT 1 FROM tenant_update_state u
                                  WHERE u.tenant_id = t.tenant_id) THEN 1 ELSE 0 END AS HasUpdateReport"""
A_EXISTS = """CASE WHEN EXISTS (SELECT 1 FROM serial_verify_attempts a
                                  WHERE a.tenant_id = t.tenant_id
                                    AND a.result = 'installer-bootstrap') THEN 1 ELSE 0 END AS HasSerialPass"""
DECIDE_LINE = "if (lastErpStartAt is not null) return Installed;"
FILL_LINE = "await TenantInstallStateReader.FillAsync(db, new[] { row }, _logger, ct);"
QUERY_BLOCK = """var facts = await db.QueryAsync<InstallFact>(new CommandDefinition(
                ReadSql,
                new { Ids = rows.Select(r => r.TenantId).ToList() },
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: ct));
"""
HALF_BLOCK = """IEnumerable<InstallFact> facts;
            try
            {
                facts = await db.QueryAsync<InstallFact>(new CommandDefinition(
                    ReadSql,
                    new { Ids = rows.Select(r => r.TenantId).ToList() },
                    commandTimeout: CommandTimeoutSeconds,
                    cancellationToken: ct));
            }
            catch (MySqlException mx) when (mx.Number == 1146)
            {
                var half = mx.Message.Contains("tenant_update_state")
                    ? ReadSql.Replace(@\"%s\", "0 AS HasUpdateReport")
                    : ReadSql.Replace(@\"%s\", "0 AS HasSerialPass");
                facts = await db.QueryAsync<InstallFact>(new CommandDefinition(
                    half,
                    new { Ids = rows.Select(r => r.TenantId).ToList() },
                    commandTimeout: CommandTimeoutSeconds,
                    cancellationToken: ct));
            }
""".replace('\\"', '"') % (U_EXISTS, A_EXISTS.replace('"', '""'))


class ZeroMatch(Exception):
    pass


def once(text, old):
    # 겹친 변이의 한 겹만 0건이어도 「무효」 로 잡히게 겹마다 던진다.
    if text.count(old) != 1:
        raise ZeroMatch(old[:40])


def move_call_before_payments(text):
    marker = "// 결제 이력 최근 10건 (메타만, 카드정보 0건)"
    once(text, FILL_LINE)
    once(text, marker)
    without = text.replace("            " + FILL_LINE + "\n", "", 1)
    return without.replace("            " + marker, "            " + FILL_LINE + "\n\n            " + marker, 1)


def sub(old, new):
    def apply(text):
        once(text, old)
        return text.replace(old, new)
    return apply


MUTATIONS = [
    ("a", "판정을 IsLocked 로", READER,
     sub("row.InstallState = Decide(f.LastErpStartAt, f.HasUpdateReport, f.HasSerialPass);",
         "row.InstallState = row.IsLocked == 1 ? Installed : None;"),
     ["설치①", "설치②", "설치③", "설치⑤", "설치⑥"]),
    ("b", "시리얼 통과를 완료로", READER,
     sub(DECIDE_LINE, "if (lastErpStartAt is not null || hasSerialPass != 0) return Installed;"),
     ["설치②", "설치⑥", "G-7 판정식"]),
    ("c1", "u EXISTS → LEFT JOIN", READER,
     lambda t: sub("FROM tenants t\n", "FROM tenants t\n        LEFT JOIN tenant_update_state u ON u.tenant_id = t.tenant_id\n")(
         sub(U_EXISTS, "CASE WHEN u.tenant_id IS NOT NULL THEN 1 ELSE 0 END AS HasUpdateReport")(t)),
     ["설치④"]),
    ("c2", "a EXISTS → LEFT JOIN", READER,
     lambda t: sub("FROM tenants t\n", "FROM tenants t\n        LEFT JOIN serial_verify_attempts a ON a.tenant_id = t.tenant_id AND a.result = 'installer-bootstrap'\n")(
         sub(A_EXISTS, "CASE WHEN a.tenant_id IS NOT NULL THEN 1 ELSE 0 END AS HasSerialPass")(t)),
     ["설치④"]),
    ("d", "예외 보호 제거", READER,
     sub("catch (Exception ex) when (ex is not OperationCanceledException)",
         "catch (Exception ex) when (ex is OperationCanceledException)"),
     G6),
    ("e", "판정 없음 → 미설치", TEXT,
     sub('_ => "확인 불가"', '_ => "미설치"'),
     G6 + ["G-7 🔴 낱말", "G-7 구판"]),
    ("f", "commandTimeout 제거", READER,
     sub("                commandTimeout: CommandTimeoutSeconds,\n", ""),
     ["G-6ⓓ"]),
    ("g", "상세 호출을 결제 앞으로", CTRL, move_call_before_payments, ["G-6ⓔ"]),
    ("h", "업데이트 보고를 완료로", READER,
     sub(DECIDE_LINE, "if (lastErpStartAt is not null || hasUpdateReport != 0) return Installed;"),
     ["설치⑥", "G-7 판정식"]),
    ("i", "업데이트 보고 재료 제거", READER,
     sub(U_EXISTS, "0 AS HasUpdateReport"),
     ["설치⑥", "G-6ⓐ2", "G-8"]),
    ("j", "재료 표마다 따로 보호", READER, sub(QUERY_BLOCK, HALF_BLOCK), ["G-6ⓐ1", "G-6ⓐ2"]),
    ("k", "성공 보고만 재료로", READER,
     sub("WHERE u.tenant_id = t.tenant_id) THEN 1", "WHERE u.tenant_id = t.tenant_id AND u.current_version IS NOT NULL) THEN 1"),
     ["설치⑥"]),
    ("l", "목록 시각 되살림", CTRL,
     sub("            foreach (var row in rows) row.LastErpStartAt = null;\n", ""),
     ["설치⑦"]),
]

# 계획에 달려 어느 쪽이든 판정에서 뺀다 — 기록은 남긴다.
OPTIONAL = {"c1": {"G-8"}, "c2": {"G-8"}}


def run(args, log):
    with open(log, "w", encoding="utf-8") as f:
        return subprocess.run(args, stdout=f, stderr=subprocess.STDOUT).returncode


def build(log):
    return run(["dotnet", "build", TESTS, "--configuration", "Release", "--no-restore", "--nologo", "-v", "q"], log)


def gate(log):
    run(["dotnet", "test", TESTS, "--configuration", "Release", "--no-build", "--nologo",
         "--filter", FILTER, "--logger", "console;verbosity=detailed"], log)
    text = pathlib.Path(log).read_text(encoding="utf-8", errors="replace")
    red = sorted({n for n in ALL for line in re.findall(r"^\s*Failed (.+)$", text, re.M) if line.startswith(n)})
    passed = len(set(n for n in ALL for line in re.findall(r"^\s*Passed (.+)$", text, re.M) if line.startswith(n)))
    return red, passed, "[SKIP]" in text


def judge(name, title, expected, summary):
    t0 = time.time()
    red, passed, skip = gate(f"neg-{name}.log")
    want = sorted(expected)
    free = OPTIONAL.get(name, set())
    ok = sorted(set(red) - free) == want and passed + len(red) == len(ALL) and not skip
    row = f"{name}\t{title}\t{'일치' if ok else '불일치'}\t빨강={','.join(red) or '-'}\t기대={','.join(want) or '-'}\t통과={passed}\t{int(time.time() - t0)}초"
    print(row, flush=True)
    summary.write(row + "\n")
    summary.flush()
    return ok


def main():
    failed = 0
    with open("neg-results.tsv", "w", encoding="utf-8") as summary:
        for name, title, path, mutate, expected in MUTATIONS:
            p = pathlib.Path(path)
            before = p.read_bytes()
            try:
                after = mutate(before.decode("utf-8")).encode("utf-8")
            except ZeroMatch as zero:
                print(f"{name}\t{title}\t무효\t0건 치환\t{zero}", flush=True)
                summary.write(f"{name}\t{title}\t무효\t0건 치환\n")
                failed += 1
                continue
            p.write_bytes(after)
            if build(f"neg-{name}-build.log") != 0:
                tail = pathlib.Path(f"neg-{name}-build.log").read_text(encoding="utf-8", errors="replace")
                errors = [l for l in tail.splitlines() if " error " in l][:3]
                print(f"{name}\t{title}\t무효\t빌드 실패\t{' | '.join(errors)}", flush=True)
                summary.write(f"{name}\t{title}\t무효\t빌드 실패\n")
                failed += 1
            elif not judge(name, title, expected, summary):
                failed += 1
            p.write_bytes(before)
        # 원복 대조 — 변이를 다 되돌린 뒤 18건이 다시 초록이어야 위 빨강이 변이 탓이다.
        if subprocess.run(["git", "diff", "--quiet", "--", READER, CTRL, TEXT]).returncode != 0 or build("neg-base-build.log") != 0:
            print("base\t원복\t무효", flush=True)
            failed += 1
        elif not judge("base", "원복", [], summary):
            failed += 1
    print(f"무효 또는 불일치 {failed}건", flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
