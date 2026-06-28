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

## 자동화 4건 (Run-CommsScenarios.ps1, 작성 완료)
- **S-A** cloudflared 서비스 kill → 워치독 sc.Start() 재기동 (PASS=5분내 Running)
- **S-B** MariaDB stop → 워치독 재기동
- **S-C** HitPan.API kill → 워치독 schtasks /Run 재기동 (PASS=5분내 /health 200)
- **S-D** TunnelSecret 무효화 → WS28C 재생성 (반자동: EventLog 병행)

실행:
```
powershell -ExecutionPolicy Bypass -File Run-CommsScenarios.ps1 -Confirm
```
- `-Confirm` 없으면 거부(오발사 방지)
- PRIMARY_DOMAIN=demo 감지 시 자동 중단(헌법 #39)
- 결과 → `reports/comms-scenarios-{타임스탬프}.md`

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
