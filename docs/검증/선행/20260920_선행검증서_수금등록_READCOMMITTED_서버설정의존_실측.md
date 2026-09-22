# [1-V] 선행검증서 — 수금·지급 등록 READ COMMITTED 의 서버 설정 의존 실측

> 오더: 9/18 인계1 §4-1 · 검증팀장 데이비드 박 · 2026-09-20 · 코드 무수정 · 커밋 없음
> 기준 코드 `origin/main` `43e1fe4d` (3판 머지본) · 시험 인스턴스 = 새로 띄운 33406 (운영 3306·격리본 33306 무접촉)

---

## 📇 카드 (≤40줄)

### 판정: 🔴 전제 성립 — CTO 가설 그대로 재현됐다 (#20 P0)

`log_bin=ON` + `binlog_format=STATEMENT` 인 MariaDB 11.4.10 에서
**READ COMMITTED 트랜잭션 안의 첫 INSERT 가 ERROR 1665 로 거절된다.** 추정이 아니라 실측이다.

### 설계가 반드시 알아야 할 사실

| # | 사실 | 근거 |
|---|---|---|
| F1 | 실패 메시지 = `ERROR 1665 (HY000): Cannot execute statement: impossible to write to binary log since BINLOG_FORMAT = STATEMENT and at least one table uses a storage engine limited to row-based logging. InnoDB is limited to row-logging when transaction isolation level is READ COMMITTED or READ UNCOMMITTED.` | §1 |
| F2 | **실패 지점 = `SELECT … FOR UPDATE` 와 INSERT/UPDATE.** `BEGIN` 통과 · 일반 `SELECT` 통과 · **`LOCK IN SHARE MODE` 도 통과** · **`FOR UPDATE` 에서 터진다.** 실제 앱은 INSERT 까지 못 간다 — `LegacyBalanceMatching.cs:189` 의 `SELECT balance_id … FOR UPDATE` 가 첫 거절 지점 | §1 |
| F3 | **범위 = 수금 등록·지급 등록 전부 (3경로 모두).** ① 이월잔액 매칭 → `GetForUpdateAsync` FOR UPDATE 에서 거절 ② 일반 `sales_delivery` 수금 → `EnsureCollectionTargetAllowedAsync` 의 `SELECT … FROM sales_deliveries … FOR UPDATE` 에서 거절 ③ ref 없는 순수 수금 → FOR UPDATE 는 없지만 `INSERT INTO collections` 에서 거절. **빠져나가는 경로가 없다** | §2 |
| F4 | **RC 를 쓰는 곳은 코드 전체에서 2곳뿐.** `BeginTransaction(` 47곳 중 `CollectionService.cs:132`·`:301` 만 `MatchIsolation` · 나머지 45곳(매출 확정·매입·재고·BOM·급여…)은 기본값(REPEATABLE READ) → **매출·매입은 영향 없다** | §2 |
| F5 | 대조군: 같은 인스턴스에서 `binlog_format` 만 **MIXED·ROW 로 바꾸면 RC INSERT 통과** → 변수는 격리수준이 아니라 binlog_format 이다 | §3 |
| F6 | 폴백① **응용 계정은 스스로 못 피한다.** DB 한정 ALL 권한 계정의 `SET SESSION binlog_format='ROW'` → `ERROR 1227 (42000): Access denied; you need (at least one of) the BINLOG ADMIN privilege(s) for this operation`. MariaDB 11.4 권한 이름 = **BINLOG ADMIN** (root 는 성공) | §4 |
| F7 | 폴백② **RR 로 바꾸면 에러는 없어지지만 돈이 틀어진다.** 같은 서버에서 RR 로 게이트 재실행 → 실패 2/24. 실제 메시지 `최종 R 음수 -20000.00` (G8-u) · `Expected InvalidOperationException, Actual: null` = **초과 매칭이 거절되지 않았다** (G8-x). **조용한 잔액 오류 ≫ 시끄러운 에러 — 대안으로 쓸 수 없다** | §4 |
| F8 | 폴백③ 서버 설정 읽기 비용 = `SELECT @@log_bin,@@binlog_format` 1회, 잠금 없는 시스템변수 조회. 연결당 1회 캐시 가능 | §4 |
| F9 | **위험 경로는 한 종류다.** 설치본이 MariaDB 를 새로 깔 때는 `msiexec … /quiet SERVICENAME=MariaDB PASSWORD=…` 뿐 — **binlog 인자·my.ini 생성 없음 = 기본값(log_bin OFF · MIXED) → 안전.** 위험은 **기존 MariaDB 재사용**(`:115`·`:1955` `NeedsMariaDB`) 경로에서 그 서버가 이미 `log_bin=ON`+`STATEMENT` 일 때뿐. STATEMENT 는 누가 일부러 지정해야 나온다 | §5 |
| F10 | 이 PC 운영 3306 = `log_bin 0 / MIXED` → 🟢 지금 이 PC 는 영향 없다 | §5 |

### ⚪ 미실측 (권한 밖 · 추측 금지)
- 사장님 PC · 고객 PC 실서버의 `log_bin`/`binlog_format` — **안 봤다. 「모른다」가 사실.**
- F9 의 "위험 경로 한 종류" 는 설치 코드 분석이지 **현장 표본이 아니다.** 빈도를 숫자로 말하면 받아쓰기(#32).

### 설계가 먼저 답해야 할 질문
- Q1. **RR 폴백은 죽었다(F7).** 남은 축: (a) 등록 전 `@@log_bin/@@binlog_format` 확인 → STATEMENT 면 **고객 문구로 먼저 막기**(저장 직전 1665 가 아니라) (b) 설치·기동 때 서버 설정을 점검해 진입 자체를 막기 (c) 고객 DB 계정에 `BINLOG ADMIN` 부여 후 세션 ROW — **(c)는 권한 확대라 별도 결재 사안**
- Q2. 막을 때 **어느 지점**인가 — 수금·지급 등록 입구만인가, 기동 시 전수 점검인가. 저장 직전 실패는 #20(흐름 안 끊김) 위반이다
- Q3. 이미 STATEMENT 인 고객 PC 가 있다면 **지금도 수금·지급이 전부 안 되고 있다.** 현장 확인 주체·방법을 누가 정하나 (검증은 권한 밖)
- Q4. `LegacyBalanceMatching.cs:185` RC 강제 가드는 남기나 — 설계가 어떤 축을 골라도 이 가드와 정합해야 한다

---

## 본문

### §1. 거절 재현 (시험 인스턴스 33406)

시험 인스턴스 = `C:/HitPanBuild/mariadb-stmt-0920` (새 datadir · `mariadb-install-db.exe` 부트스트랩 · **서비스 등록 안 함**, 포그라운드 프로세스 · #29 준수).
설정: `log_bin=ON` · `binlog_format=STATEMENT` · `utf8mb4_unicode_ci` · `server_id=9920`.

확인값:
```
SELECT @@log_bin,@@binlog_format,@@global.tx_isolation,@@version;
→ 1   STATEMENT   REPEATABLE-READ   11.4.10-MariaDB-log
```

단계별 raw 재현 (InnoDB 테이블) — **문장 종류별로 갈린다**:
```
A: SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED;   → OK
B: BEGIN;                                                     → OK
C: SELECT COUNT(*) FROM t;               (일반 읽기)          → OK
D: SELECT … LOCK IN SHARE MODE;          (공유 잠금 읽기)     → OK  ★
E: SELECT … FOR UPDATE;                  (배타 잠금 읽기)     → ERROR 1665  ★
F: INSERT INTO t VALUES (1,'x');                              → ERROR 1665
```
★ = 설계가 놓치기 쉬운 갈림. `LOCK IN SHARE MODE` 는 통과하고 `FOR UPDATE` 만 막힌다.
(E 는 매칭 행이 0건이어도 거절됐다 → **행 유무가 아니라 문장 종류로 막는다.**)

전문:
```
ERROR 1665 (HY000) at line 12: Cannot execute statement: impossible to write to binary log
since BINLOG_FORMAT = STATEMENT and at least one table uses a storage engine limited to
row-based logging. InnoDB is limited to row-logging when transaction isolation level is
READ COMMITTED or READ UNCOMMITTED.
```

**판정 근거**: BEGIN 은 통과한다. 그래서 "트랜잭션이 안 열린다"가 아니라 **"열리고 검사도 다 지나간 뒤 첫 INSERT 에서 거절된다"** 가 정확한 증상이다.
`CreateCollectionAsync` 순서는 `BeginTransaction(RC)` → `EnsureCollectionTargetAllowedAsync`(SELECT) → `INSERT INTO collections` 이므로 **INSERT 에서 터진다**.

### §2. 영향 범위

`src/HitPan.Application/Services/CollectionService.cs`
- `:132` `CreateCollectionAsync` — `using var tx = _db.BeginTransaction(LegacyBalanceMatching.MatchIsolation);`
- `:301` `CreatePaymentAsync` — 같은 줄
- `:217` `DeleteCollectionAsync` / `:382` `DeletePaymentAsync` — 인자 없는 `BeginTransaction()` = 기본 RR → **삭제는 영향 없다**

`src/HitPan.Application/Services/LegacyBalanceMatching.cs:209`
```
public const IsolationLevel MatchIsolation = IsolationLevel.ReadCommitted;
```
같은 파일 `:185-186` 은 RC 가 아니면 `NotSupportedException` 을 던지는 방어선이다 — **RR 로 바꾸려면 이 방어선도 같이 봐야 한다**(설계 주의).

전수 grep `BeginTransaction(` = 47곳(주석 2곳 포함). 그중 격리수준을 명시한 곳은 위 2곳뿐.
나머지(`SalesService` 4곳 · `PurchaseService` · `StockService` 2곳 · `BomService` 3곳 · `ApprovalService` 3곳 · `PayrollService` 2곳 · `FinanceService` 2곳 · `MdbMigrationService` 등)는 전부 기본값.
→ **매출 확정·매입·재고·BOM·회계는 STATEMENT 서버에서도 정상 동작한다.** 구멍은 수금·지급 등록 두 입구다.

중요: 이월잔액 매칭 건이냐 아니냐를 **가리지 않는다**. 트랜잭션을 RC 로 여는 것이 메서드 진입부이고, 일반 `sales_delivery` 대상 수금 1건도 같은 `INSERT INTO collections` 를 지난다. → **모든 수금·지급 등록 거절**.

### §3. MIXED · ROW 대조군

같은 33406 인스턴스에서 `binlog_format` 만 바꿔 같은 RC INSERT 를 재시도:
```
SET SESSION binlog_format='MIXED'; SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN; INSERT INTO rc_probe.t VALUES (10,'mixed'); COMMIT;   → MIXED_RC_INSERT_OK
SET SESSION binlog_format='ROW';   (동일)                     → ROW_RC_INSERT_OK
```
**대조군 통과.** 격리수준(RC)만으로는 안 터진다. `log_bin=ON` **그리고** `binlog_format=STATEMENT` 둘 다 있어야 터진다. 조건은 AND 다.

### §4. 폴백 3가지 — 사실만

**① 응용 계정이 세션에서 피할 수 있나 → 못 한다.**
설치본 `hitpan` 급 권한(해당 DB 한정 `ALL PRIVILEGES`, 전역은 `USAGE`)을 그대로 만든 계정으로:
```
SET SESSION binlog_format='ROW';
→ ERROR 1227 (42000): Access denied; you need (at least one of) the BINLOG ADMIN privilege(s) for this operation
```
같은 문장을 `root` 로 실행 → 성공. **필요 권한 이름 = `BINLOG ADMIN` (MariaDB 11.4).**
→ 연결 문자열/세션 수준 자동 회피는 **불가**. 고객 DB 계정에 BINLOG ADMIN 을 주는 것은 별도 결재 사안이다.

**③ 서버 설정 읽기 비용** — `SELECT @@log_bin, @@binlog_format;` 시스템변수 1회 조회. 테이블·잠금 접근 없음. 연결당 1회 캐시하면 등록 경로에 추가 왕복 0.

**② RR 로 열면 어떻게 되나 → 🔴 안 된다. 돈이 틀어진다.**
버릴 worktree(`C:/HitPanBuild/wt-rc`, `43e1fe4d` detached)에서 `MatchIsolation` 만 `RepeatableRead` 로 바꿔 같은 33406(STATEMENT) 에 게이트 24건을 돌렸다.
**메인 레포 작업 트리는 안 건드렸고(`git status --porcelain src/` 비어 있음 확인), 커밋 0, worktree 는 제거했다.**

| 격리수준 | 서버 | 결과 |
|---|---|---|
| ReadCommitted (현행 3판) | STATEMENT | **실패 20 / 통과 4** — 전부 ERROR 1665 |
| RepeatableRead | STATEMENT | **실패 2 / 통과 22** — 1665 는 사라졌으나 **정확성 게이트 2건이 깨진다** |

깨진 2건의 실제 메시지 (테스트 계약 가드가 아니라 **진짜 값이 틀린 것**):
```
G8-u 병렬이슈44 두 연결 …  → 최종 R 음수 -20000.00 (receivable=True)
                              (LegacyBalanceMatchGateTests.cs:714)
G8-x 같은 거래처 동시(각 R 이하 · 합 초과) …
                            → Assert.IsType() Failure: Value is null
                              Expected: typeof(System.InvalidOperationException)  Actual: null
                              (LegacyBalanceMatchGateTests.cs:810)
```
해석: RR 이면 뒤 연결이 **앞 연결 커밋 전 스냅숏의 옛 R** 로 판정한다 →
- G8-x: 합이 R 을 넘는데도 **거절이 안 된다**(`Actual: null` = 예외가 안 났다)
- G8-u: 그 결과 **남은 잔액이 −20,000 원**으로 내려간다

**즉 RR 은 "느리지만 맞는" 대안이 아니다. 에러가 조용해지고 대신 잔액이 틀어진다.**
에러(1665)는 고객이 알아채지만 음수 잔액은 아무도 모른 채 쌓인다 — 설계는 이 교환을 대안으로 삼으면 안 된다.
덧붙여 `LegacyBalanceMatching.cs:185` 가 RC 가 아닌 트랜잭션을 `NotSupportedException` 으로 막고 있어, RR 전환은 한 줄 변경이 아니다.

### §5. 현장 분포 근거

| 경로 | 근거 | 서버 설정 |
|---|---|---|
| 이 PC 운영 3306 (읽기만) | `SELECT @@log_bin,@@binlog_format,@@global.tx_isolation,@@version` → `0 / MIXED / REPEATABLE-READ / 11.4.10-MariaDB` | 🟢 **안전** (log_bin OFF) |
| 기존 격리본 33306 (읽기만) | `0 / MIXED` | 🟢 안전 |
| 설치본이 **새로 깔 때** | `installer/HitPan-Universal.iss:1957-1958`<br>`Exec('msiexec.exe', Format('/i "%s\mariadb.msi" /quiet SERVICENAME=MariaDB PASSWORD="%s"', …))` | **my.ini 를 따로 만들지 않고 binlog 인자도 안 준다 = msi 기본값.** MariaDB 11.4 기본은 `log_bin` OFF · `binlog_format` MIXED → 🟢 안전 |
| 설치본이 **기존 MariaDB 를 재사용할 때** | `installer/HitPan-Universal.iss:115` `Check: NeedsMariaDB` · `:1955` `if NeedsMariaDB then begin` → **미설치일 때만 msi 가 돈다** | 🔴 **그 PC 의 기존 설정을 그대로 쓴다. 히트판이 못 정한다.** |

→ 위험은 **"이미 MariaDB 가 깔려 있고, 그 서버가 log_bin=ON + binlog_format=STATEMENT 로 운영되던 PC"** 한 종류다.
기본값으로만 깔린 서버는 log_bin 이 꺼져 있어 안전하고, log_bin 만 켜도 기본 포맷이 MIXED 라 안전하다.
**STATEMENT 는 누군가 일부러 지정해야 나온다.** 다만 그런 PC 가 실제로 있느냐는 §6 대로 **미실측**이다.

### §6. 미실측 (권한 밖 · 추측 금지)
- 사장님 PC · 고객 PC 실서버의 `log_bin` / `binlog_format` — **안 봤다. 「모른다」가 사실이다.**
- 위 §5 표의 "위험은 한 종류뿐" 은 **설치 경로 분석**이지 현장 표본이 아니다. 빈도를 숫자로 말하면 받아쓰기다(#32).

### §7. 시험 환경 정리 (#29·#39 준수 기록)
- 시험 인스턴스: 새 datadir `C:/HitPanBuild/mariadb-stmt-0920` · 포트 33406 · **Windows 서비스 등록 안 함**(포그라운드 프로세스) · 시험 후 `SHUTDOWN` 으로 정상 종료 확인(`33406 LISTENING 0`).
- 운영 3306 · 기존 격리본 33306 · `HitPan.API` · `HitPan.Watchdog` · `appsettings*` **무접촉** (3306·33306 LISTENING 유지 확인).
- 코드 변경은 detached worktree 안에서만, 시험 후 `git worktree remove --force` 로 제거. **메인 레포 커밋 0 · 작업 트리 변경 0.**
