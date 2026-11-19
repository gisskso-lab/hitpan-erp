# 통신 무결성 17 시나리오 — 테스트 환경 구축 + 실측 가이드

> 헌법 #27 베타 게이트 / #39 운영 검증 금지 — **반드시 별도 테스트 환경에서만**

## 왜 별도 환경인가
17 시나리오는 cloudflared·MariaDB·API 를 **실제로 죽여서** 워치독이 5분 내 복구하는지 계측한다. demo(사장님 PC 운영본)에서 하면 사장님 운영이 다운된다(헌법 #39 — 오늘 demo 수술 사고의 교훈). 그래서 테스트 전용 터널·DB·포트로 격리한다.

## 테스트 환경 격리 4요소 (사장님 인프라 결재 필요 — #29)
| 요소 | 운영(demo) | 테스트 |
|---|---|---|
| 터널 | demo 터널(e03a3b95…) | **test-tunnel 신규 발급** (Cloudflare, 사장님 결재) |
| 도메인 | demo.hitpan.kr / api-demo | **test.hitpan.kr / api-test.hitpan.kr** |
| DB | hitpan_erp | **hitpan_e2e** (별도 스키마) |
| 포트 | 5234/5257 | **15234/15257** (충돌 회피) |
| db.conf | PRIMARY_DOMAIN=demo… | PRIMARY_DOMAIN=test… (스크립트 안전가드가 demo면 차단) |

## 자동화 (Run-CommsScenarios.ps1, 봉합 완료 — 작2 2026-06-28)
- **S-A** cloudflared-test 서비스 kill → 워치독 sc.Start() 재기동 (PASS=5분내 Running)
- **S-B** MariaDB-test stop → 워치독 재기동
- **S-C** HitPan.API(test 슬롯) kill → 워치독 schtasks /Run 재기동 (PASS=5분내 /health 200)
- **S-D** TunnelSecret 무효화 → WS28C 재생성 (**반자동**: EventLog 병행. 실측 자동은 S-A·B·C 3건)

실행:
```
powershell -ExecutionPolicy Bypass -File Run-CommsScenarios.ps1 -Confirm
```

### 🔒 안전망 (작2 봉합 — fail-safe, 헌법 #39)
1. **`-Confirm` 없으면 거부**(오발사 방지)
2. **화이트리스트 fail-safe**: `C:\HitPanTest\HITPAN_TEST_ENV.marker` 가 없으면 **무조건 차단**.
   - 기존 "PRIMARY_DOMAIN=demo면 차단"은 db.conf 없으면 무력화(fail-open)됐다 → "test 마커 있을 때만 실행"(fail-closed)으로 전환.
   - demo PC엔 이 마커가 절대 없으므로 demo에서 실행 시 100% 차단.
3. **마커 잔존 방어**: 마커가 있어도 demo 서비스(`cloudflared`, 접미사 없음)가 Running이면 차단(위험상태).
4. **서비스명 가드**: `-test` 접미사 없는 서비스명은 죽이지 않고 SKIP(demo 서비스 보호).
5. **API 경로 가드(MUST)**: `HitPan.API` 는 ProcessPath 가 `C:\HitPanTest\` 하위일 때만 kill. demo API(같은 exe명)는 절대 안 죽임.
6. **2차 방어 유지**: db.conf 있고 PRIMARY_DOMAIN=demo면 추가 차단.
7. 결과 → `reports/comms-scenarios-{타임스탬프}.md`

### 📄 test 마커 파일 스펙 (`C:\HitPanTest\HITPAN_TEST_ENV.marker`)
test 슬롯 구축(작1 5-4) 시에만 생성. **운영 배포본·demo엔 절대 미포함.**
```
ENV=TEST
SLOT=e2e-comms
CLOUDFLARED_SVC=cloudflared-test
MARIADB_SVC=MariaDB-test
```
- `ENV=TEST` 줄 필수(없으면 마커 무효 차단).
- `CLOUDFLARED_SVC`·`MARIADB_SVC` = 이 슬롯의 실제 서비스명(마커=서비스명 단일 진실원). 스크립트가 이 값만 죽인다.

### ✅ 반증 테스트 (데이비드 박, 완료 게이트 — 만든 사람 ≠ 검증)
실측 전 반드시 통과:
1. **마커 없는 상태**(=demo 모사)에서 `-Confirm` 실행 → **반드시 exit 1 차단**되는지 실증.
2. **마커 + demo 서비스 Running** → 차단되는지.
3. **`-test` 없는 서비스명** 줘도 죽이지 않고 SKIP 하는지.
4. demo `cloudflared`·`HitPan.API`(`C:\Program Files\HitPan\`)를 죽이는 경로가 **도달 불가**임을 코드로 반증.
→ 4개 통과 전 실측(`-Confirm` on test) 금지.

### ⚠️ 인코딩 (작2): 스크립트는 UTF-8 BOM 으로 저장됨
Windows PowerShell 5.1 이 BOM 없는 UTF-8 한글을 CP949로 오독해 가짜 구문오류를 낸다. BOM 유지 필수(편집 시 인코딩 깨지면 재저장).

## 수동 시나리오 (자동화 불가 — 별도 세션, 물리·환경)
| # | 시나리오 | 방법 | PASS 기준 |
|---|---|---|---|
| S01 | Windows Update 강제 재부팅 | `shutdown /r /t 60` | 재부팅 후 5분내 /health 200 |
| S02/03 | 정전(UPS/무UPS) | 콘센트 분리→복전 | 5분내 자동복구 |
| S04/05/06 | 회선·공유기·DNS | 랜선 분리·공유기 재부팅 | 회선 복귀 후 자동 재연결 |
| S07/08/09 | 백신 격리(Defender·V3·알약) | 격리 시뮬 | 🔴 현 워치독 미감지 — 갭. 설치EXE 예외등록 검증 |
| S12/13 | 물리(SSD·RAM) | — | 사전예방만, 복구 불가 |

## 전체 PASS 게이트 (헌법 #27)
- 자동 4건 + 수동 13건 = **17/17 PASS = 베타 발진 허가**
- 현재: PASS 0건(설계만) → 이 가이드로 실측 시작
- 🔴 우선 갭: 보안SW 격리(S07~09) 워치독 미감지 = 베타 전 보강 필요

## 다음 세션 순서
1. 사장님 인프라 결재(test-tunnel·도메인·DB) — #29
2. 테스트 환경 구축(미니 ERP 설치 .iss 로 test 슬롯)
3. Run-CommsScenarios.ps1 -Confirm 실측 → 자동 4건 PASS/FAIL 확정
4. 수동 13건 순차 실측
5. 17/17 PASS → 베타 GO / FAIL분 봉합(작업지시서→결재)

---

## (2026-11-19 추가 · W-G1-53) 새 장애 시험 도구 — 위 「자동화」 절의 옛 러너는 이제 쓰지 않는다

> 근거: 설계 `docs/설계/설치배포/20261118_WG1-53_장애시험도구재설계_설계.md` · 개발명세서 `docs/개발/설치배포/20261118_개발명세서_WG1-53_장애시험도구재설계.md`. 위 절들은 기록으로 남겨 둔다(지우지 않음).

### 옛 도구는 실행되지 않는다
- `Run-CommsScenarios.ps1` · `Build-TestSlot.ps1`(이 폴더) · `tests/scenarios/Run-All-Scenarios.ps1` · `S06` · `S07` · `S10` · `S14` · `S15` · `S16` = **9파일 맨 앞(param 바로 뒤)에 무조건 거절 블록** — 안내 두 줄을 찍고 `exit 1`. 우회 스위치 없음 · 본문은 기록으로 보존.
- 위 「자동화 · 안전망 · test 마커 파일 스펙 · 반증 테스트 · 다음 세션 순서」 절은 옛 러너 기준이다. **마커 형식도 바뀌었다**(아래).
- (11/19 [3-V] M6) **위 9파일 밖 옛 진입점** `tests/scenarios/W1-Gate-Checklist.ps1` · `Pre-W1-Gate-Check.ps1` · `S18` · `S19` · `S20` 은 이번 거절 대상이 **아니다**(거절 블록 없음 · 별건) — 그러나 **기본 대상이 운영 도메인**이라 **쓰지 말 것**(인자 없이도 돌리지 말 것).

### 새 러너 — `Run-RoutinePcScenarios.ps1`(이 폴더)
| 쓰는 법 | 하는 일 |
|---|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File Run-RoutinePcScenarios.ps1` | **판정만** — 가드 7칸 판정 · 상태 읽기만. 끊기 0 · 파일 쓰기 0. 가드 거절이면 외부 GET 도 0 |
| 위 + `-Confirm` | 가드 7칸이 **전부** 통과하고 보고서 폴더가 저장소 밖일 때만 S-A → S-B → S-C → S-D 한 건씩. 실패 · 측정불가면 그 자리에서 멈추고 인계 쪽지 |
| `-ReportDir <폴더>` | 보고서 · 실행 기록(`run-history.jsonl`) 폴더. 기본 `C:\HitPanTest\reports`. `X:\` 로 시작하는 저장소 밖 폴더만(UNC · 장치 경로 · 상대 · 저장소 안 = 판정 모드로 내려감) |

- 다루는 범위 = **4 ID**: S-A `cloudflared` 서비스 멈춤 · S-B `MariaDB` 서비스 멈춤 · S-C 설치본 `api\HitPan.API.exe` 프로세스 끊기 · S-D 터널 자격증명 무효화(**사람 손 1회 · 반자동**). 보고서에 「17 중 몇」 은 적지 않는다(이 도구가 17 시나리오를 다 재는 것이 아님 · 헌법 #32).
- 합격 = 「자동 3 PASS + 반자동 1 완료」 한 줄뿐 · 그때만 종료 코드 0. PASS 문구 = 「5분 안 복구됨」(누가 살렸는지는 별도 칸 · 판정에 안 넣음).
- 도구는 **손으로 살리지 않는다**(시작 · 재시작 명령 0). 멈추고 인계 쪽지가 나오면 쪽지의 「사람이 할 일 · 안전 복구 순서」 대로 — 사람이 도구 밖에서 하는 시작은 헌법 #29 사전 승인 범위 안에서만.
- 같은 대상 60분 안 5회째 = 미실행(쿨다운 보호). 실행 기록은 보고서 폴더에 남으니 **늘 같은 보고서 폴더**를 쓴다.

### 가드 7칸(하나라도 아니면 거절 · 끊기 0)
| 칸 | 맞아야 하는 것 |
|---|---|
| G1 | 마커 파일 `C:\HitPanTest\HITPAN_TEST_ENV.marker` 있음 · `ENV=TEST` |
| G2 | 마커 `MACHINE` = 이 PC 기기 식별값 해시 |
| G3 | 마커 `EXPIRES` = 지금보다 뒤 · 72시간 안 |
| G4 | 설치본 `db.conf` 의 `PRIMARY_DOMAIN` + `TENANT_CODE` 해시 쌍이 허용목록 한 줄과 같음(demo 도메인 = 거절) |
| G4a | 허용목록 파일(작업 사본) = 저장소 `origin/main` 승인본과 같음 |
| G5 | `HitPanWatchdog` · `cloudflared` · `MariaDB` Running + 원점 `http://127.0.0.1:{API_PORT}/health` 200 |
| G6 | 관리자 권한 |

### 새 마커 형식(옛 `SLOT` · `*_SVC` 줄은 읽지 않음)
```
# 시험 PC 마커 — 저장소에 커밋하지 않는다
ENV=TEST
MACHINE=<기기 식별값 해시 64자>
EXPIRES=2026-11-20T09:00:00Z
```
- `ENV` · `MACHINE` · `EXPIRES` 각 한 번만(두 번 = 마커 무효). `#` 줄 · 빈 줄은 건너뜀.
- `MACHINE` = 레지스트리 `HKLM\SOFTWARE\Microsoft\Cryptography` 의 `MachineGuid` 값을 앞뒤 공백 빼고 소문자로 바꾼 뒤 UTF-8 SHA-256(소문자 16진 64자).
- `EXPIRES` = UTC · 끝 `Z` · 초까지. 72시간 넘게 잡으면 거절 — 시험 날마다 다시 적는다.

### 허용목록 — `test-pc-allowlist.json`(이 폴더 · 지금은 빈 목록)
```
{
  "entries": [
    { "primaryDomainSha256": "<도메인 해시 64자>", "tenantCodeSha256": "<테넌트 코드 해시 64자>" }
  ]
}
```
- 해시 = 값(앞뒤 공백 빼고 소문자) UTF-8 SHA-256 소문자 16진. 원문 도메인 · 테넌트 코드는 파일에 적지 않는다.
- 지금은 `entries` 가 비어 있어 어느 PC 도 G4 를 통과하지 못한다(빈 목록 = 전부 거절). 시험 PC 한 줄은 **대표 결재 뒤 PR 로만** 더하고, 그 PC 의 저장소 사본이 `origin/main` 과 같아야 G4a 를 통과한다.

### S-D 손 절차(반자동 · 사람 손 1회)
1. 러너가 「손 안내」 줄을 찍으면 정해진 절차대로 시험 PC 터널 자격증명을 사람이 무효화한다(도구는 무효화 · 되살림을 하지 않는다).
2. **무효화 직후 바로** 「누가」 를 적고 Enter — Enter 친 순간이 관측 시작 시각이다. 늦게 치면 그 사이 복구가 지나가 「실패(끊김 미반영)」 가 날 수 있다(합격 0 쪽).
3. 「누가」 = 글자 · 숫자 · 공백 · `._()-` · 가운뎃점(`·`) 40자까지 — 안내대로 `QA · 홍길동` 꼴이면 된다. 긴 16진 꼴(토큰 · 해시 모양)은 받지 않는다.
4. 무효화를 **하지 않았으면** 첫 물음에 빈 칸으로 Enter = 「미실행(손 기록 없음)」.
5. 적은 것이 형식에 안 맞으면 **한 번 더 묻는다**. 두 번째도 안 맞으면(빈 칸 포함) 「무효화했음 · 누가 = 거절됨」 으로 보고 S-D = 실패 · 대상 상태 불명 · **인계 쪽지** — 터널 자격증명이 죽은 채 남지 않게 사람이 쪽지대로 확인한다.

### 결과 읽기
- 종합 블록(콘솔 맨 끝 · 보고서 파일 맨 위)의 첫 줄이 판정이다. `합격(…)` 이 아니면 미합격. 정상 끝 없이 빠지면(Ctrl+C · 예외) 첫 줄이 「관측 중단 — 미합격 …」 이고 종료 코드 1.
- 한국어 Windows 콘솔(코드 페이지 949)에선 「—」 · 「⚠」 가 「?」 로 보인다. 정확한 글자는 보고서 파일(`routine-pc-{UTC}.txt` · UTF-8)을 본다.
- 「⚠ 대상 상태 불명 · 사람 확인 필요」 · 「시험 종료 아님 · 인계 필요(§5-4)」 줄이 있으면 인계 쪽지대로 처리하기 전엔 시험이 끝난 것이 아니다.

### 시험(가짜 입력만 · 진짜 끊기 0)
```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/scenarios/comms-integrity/Test-CommsGuard.ps1
```
- 가짜 세계(시계 · 서비스 · HTTP · 이벤트)로 러너 · 판정 모듈(`CommsGuard.psm1`)을 돌린다. 실물 서비스 · 레지스트리 · 외부 HTTP 호출 0. CI(`watchdog-ci.yml` scenario-lint)도 같은 명령.
- 끝 줄 「Test-CommsGuard 끝 — 경우 n/n 일치 · 세부 확인 k/k · 진짜 끊기 호출 0」 · 종료 코드 0 이어야 통과.
