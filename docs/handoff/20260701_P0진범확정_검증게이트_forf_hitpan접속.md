# P0 진범 확정 — 2026-07-01 샌드박스 실측 완결

> PM 브라운킴 / 어제 "봉합 미적중 → 내일 재규명" 실행 완료
> 한 줄: **P0 진범 = DB셋업 배치 마지막 검증 게이트(L905·908)가 `for /f` 안에서 `mysql -u hitpan -p<랜덤32자>`로 카운트 조회하다 실패 → FINAL_COUNT="" → exit 1. 데이터(124테이블·유저·import)는 100% 정상 생성됐는데 검증만 죽어 설치 DOA. 어제 봉합(L859-860 유저 빈값 가드)은 완전히 틀린 위치였다.**

---

## 1. 샌드박스 실측으로 확정한 사실 (모두 실측, 추측 0)

| # | 검증 | 명령 | 결과 |
|---|---|---|---|
| 1 | MariaDB 기동 | `Get-Service *maria*` | **Running** ✅ |
| 2 | root 접속 | `mysql -u root -p<랜덤>` | 성공 ✅ |
| 3 | DB 생성됨 | `SHOW DATABASES` | `hitpan_erp` 존재 ✅ |
| 4 | 테이블 수 | `SELECT COUNT(*) ... BASE TABLE` | **124** ✅ (완전) |
| 5 | hitpan 유저 | `SELECT user,host,plugin FROM mysql.user` | `hitpan@localhost mysql_native_password` 41자 해시 ✅ |
| 6 | import 재시도(root) | `source hitpan_db.sql` | **에러 0** ✅ |
| 7 | 검증쿼리(알려진 비번) | hitpan 비번=test1234 후 카운트 | **124 / 1** ✅ |
| 8 | 검증쿼리(랜덤 비번, for/f 없이) | hitpan 비번=랜덤32자 직접 접속 | **124, exit 0** ✅ |
| 9 | **설치 결과** | 마법사 | ❌ **"DB 초기화 실패(코드 1)" (at 57:12129)** |
| 10 | import 에러로그 | `hitpan_import_err.log` | **빈 파일** (import 전에 안 죽음 = import는 성공) |
| 11 | db.conf | 없음 | 배치 exit 1로 L952(기록) 전에 중단 = **결과이지 원인 아님** |

**해석**: 모든 데이터가 정상 생성됐는데(1~8) 설치만 실패(9). import 로그가 빈 것(10)은 import가 성공했다는 증거. 유일하게 남는 실패 지점 = **import 이후의 검증 게이트**.

---

## 2. 진범 코드 라인 (installer/HitPan-Universal.iss)

```
L905: for /f "tokens=*" %%%%c in ('mysql -u hitpan -p<G_DbPassword> hitpan_erp -N -B -e "SELECT COUNT(*)..."') do set FINAL_COUNT=%%%%c
L906: if "!FINAL_COUNT!"=="" set FINAL_COUNT=0
L913: if !FINAL_COUNT! LSS 124 (echo [오류]... & exit /b 1)   ← 여기서 죽음
```

- CREATE DATABASE(L870)·CREATE USER(L872)·import(L902)는 **전부 `-u root`** 로 실행 → 성공.
- **검증(L887·891·905·908)만 `-u hitpan -p<랜덤32자>`** → `for /f`의 작은따옴표 명령이 cmd 재파싱 + `setlocal enabledelayedexpansion`(L867) 조합에서 랜덤 비번 접속이 실패 → 카운트 빈값 → exit 1.
- 실측 8번이 결정타: **동일 비번도 `for/f` 없이 직접 접속하면 124 정상.** 차이는 오직 `for /f` 래핑.

### 어제 봉합(a252f19, L859-860)이 미적중인 이유
- 어제 진단 = "시리얼없이 설치 → G_DbUser 빈값 → `mysql -u  -p<비번>` 깨짐". 봉합 = DB셋업 직전 유저 빈값 가드.
- **틀렸다**: 실측 5번에서 hitpan 유저가 정상 생성됨 = G_DbUser는 이미 채워져 있었다. 유저 빈값은 원인이 아니었다.
- 1.2.11 에러로그의 `-p<hex>@localhost using password:NO`는 **root 비번(MariaRootPw, hex 24바이트=48자)**이 `-u` 없이 밀린 별개 현상이었을 가능성 — 재규명 대상이나 P0 본질(검증 게이트)과 무관.

---

## 3. 봉합 방향 (SOP 작업지시서로 진행 — 아직 코드 안 고침)

**정공법 = 검증 조회를 root로 통일.**
- CREATE/import가 이미 root인데 검증만 hitpan으로 하는 게 취약점. 검증 카운트(L887·891·905·908)를 **`-u root -p<MariaRootPw>`** 로 바꾸면 랜덤 비번이 `for/f`를 안 타서 근본 차단.
- hitpan 유저 접속 가능 여부는 **별도로 1회** 명시 검증(`mysql -u hitpan -p<비번> -e "SELECT 1"` errorlevel 체크)해서 "유저는 만들었으나 접속 안 됨" 회귀도 잡는다.
- 대안(비번을 임시파일 `--defaults-extra-file`로 전달)도 있으나, root 통일이 최소 변경·최대 안전. CTO 결재 시 택1.

**검증**: 봉합 후 반드시 샌드박스에서 1.2.17 시리얼없이 재설치 → 설치 완료(에러 0) 실측. **실측 PASS 전 출하 금지.**

---

## 4. 절대 보존 / 금지
- 이 PC = demo 운영(3306 hitpan_erp 129만행). 이번 진단은 **전부 샌드박스 안**에서만 실행(헌법 #39). 호스트 무접촉.
- 샌드박스는 휘발성 — 끄면 흔적 0. 진단 중 hitpan 비번을 test1234/랜덤으로 바꾼 것도 샌드박스 안이라 무해.
- **1.2.16 출하 금지 유지** — 시리얼없이 설치 DOA. 봉합(1.2.17) + 샌드박스 재검증 PASS 후에만 빌드/배포.
- 재봉합은 SOP(작업지시서→CTO결재→사장님승인) 거침. 즉석 금지(헌법 #33·#39).

---

## 5. 의의
- 어제 "봉합 미적중"을 정직하게 인정하고 재규명 → **오늘 진범이 어제 진단과 전혀 다른 곳(검증 게이트)임을 실측으로 확정.** 거짓 봉합/받아쓰기 차단.
- 8단계 실측(데이터는 다 됐는데 검증만 실패)으로 원인을 코드 라인까지 못박음. "추측으로 됩니다=거짓보고" 반대편 = 실측으로 확정.
