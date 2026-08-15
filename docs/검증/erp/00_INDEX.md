# 00. 코드 분석 인덱스 (2026-05-14 새벽)

**사장님이 가장 먼저 보실 인덱스.**
**작성:** PM
**가동 사유:** 사장님 5/14 새벽 명령 (학습과제 하루 미루고 코드 분석. 주석·세미콜론까지)
**마감:** 5/14 06:00 (사장님 추가 2시간 결재 = 04:00~06:00)
**최종 산출물 수:** 31개 보고서 + 5개 DB 덤프

---

## 🎯 사장님 시간 절약용 추천 읽기 순서

1. **본 인덱스 (00)** ← 지금 여기
2. **[27. 사장님 보고용 최종](27_사장님_보고용_최종.md)** ← 가장 먼저
3. **[26. P0 6건 봉합 액션 플랜 line:번호](26_봉합_액션플랜_P0_6건.md)** ← 코드 작성용
4. **[10. 서브에이전트 26명 분담 + 시간표](10_서브에이전트_역할재정의.md)** ← 이번 주 운영
5. **[19. DB 실측 검증](19_DB_실측_검증.md)** ← 데이터 살아있는지

---

## 1. 어벤져스 8개 보고서 (1차 분석)

| # | 보고자 | 파일 | 핵심 |
|---|---|---|---|
| 01 | CTO (래리/데이비드 박급) | [01_CTO_fullstack.md](01_CTO_fullstack.md) | 거대 단일 tx (#20 오독) |
| 02 | DB 매니저 (Harvard·Oracle 30년) | [02_DB_manager.md](02_DB_manager.md) | innodb_flush_log_at_trx_commit=2 누락, ORDER BY 11개 |
| 03 | 보안팀장 (안랩 40년) | [03_security_lead.md](03_security_lead.md) | AES 4/5, raw_data 미구현, 보안 75/100 |
| 04 | 백엔드 매니저 | [04_backend_manager.md](04_backend_manager.md) | AsyncLocal 누수, Task.Run, HostedService 권고 |
| 05 | 프론트 + 웹디자이너 | [05_frontend_webdesign.md](05_frontend_webdesign.md) | 진행률 좀비 가시화 불가 |
| 06 | 설계팀장 + 본부장 | [06_arch_brownkim_chunsik.md](06_arch_brownkim_chunsik.md) | 5/31 게이트 위험 |
| 07 | 검증팀장 | [07_qa_lead.md](07_qa_lead.md) | V1~V7 + EVF 6대 |
| 08 | 법무 + ERP 매니저 | [08_legal_erp.md](08_legal_erp.md) | 본사 송신 0건 ✅ |

## 2. PM 종합 (3건)

| # | 파일 | 내용 |
|---|---|---|
| 09 | [09_헌법위반_전수표.md](09_헌법위반_전수표.md) | #1~#25 위반 매트릭스 |
| 10 | [10_서브에이전트_역할재정의.md](10_서브에이전트_역할재정의.md) | ★ 26명 분담 + 4일 시간표 |
| 11 | [11_PM_최종_종합.md](11_PM_최종_종합.md) | 결재 9건 |

## 3. 2차 심층 정독 (12~26, 세미콜론까지)

| # | 파일 | 분량 |
|---|---|---|
| 12 | [12_DB_전체스키마_전수.md](12_DB_전체스키마_전수.md) | 110+ 테이블 |
| 13 | [13_API_컨트롤러_전수.md](13_API_컨트롤러_전수.md) | 47개 컨트롤러 / 347개 엔드포인트 |
| 14 | [14_서비스_전수.md](14_서비스_전수.md) | 44개 + 45 인터페이스 |
| 15 | [15_Pages_전수.md](15_Pages_전수.md) | 107개 .razor |
| 16 | [16_Web_Services_전수.md](16_Web_Services_전수.md) | 39개 + Sidebar 11그룹 |
| 17 | [17_Infrastructure_전수.md](17_Infrastructure_전수.md) | DI 38 + 미들웨어 11 + AES 15컬럼 |
| 18 | [18_헌법_적용_매트릭스.md](18_헌법_적용_매트릭스.md) | 헌법 25 × 코드 |
| 19 | [19_DB_실측_검증.md](19_DB_실측_검증.md) | 1,135,929행 보존 |
| 21 | [21_MigrationController_전수정독.md](21_MigrationController_전수정독.md) | 261+68줄 |
| 22 | [22_MdbMigrationService_전수정독.md](22_MdbMigrationService_전수정독.md) | 1,541줄 / 36 메서드 |
| 23 | [23_MdbMigration_Razor_전수정독.md](23_MdbMigration_Razor_전수정독.md) | 344줄 / Snackbar 11종 |
| **24** | **[24_컬럼_348개_정공.md](24_컬럼_348개_정공.md)** | **04~05시 추가** |
| **25** | **[25_인덱스_670개_정공.md](25_인덱스_670개_정공.md)** | **04~05시 추가** |
| **26** | **[26_봉합_액션플랜_P0_6건.md](26_봉합_액션플랜_P0_6건.md)** | **★ 05~06시 추가** |
| **27** | **[27_사장님_보고용_최종.md](27_사장님_보고용_최종.md)** | **★ 사장님 첫 보고** |

## 4. DB 실측 덤프 (부속)

- `_db_columns_dump.txt` (348 행)
- `_db_indexes_dump.txt` (670 행)
- `_db_fks_dump.txt` (54 행)
- `_db_create_core5.txt` (220 행) — partners/items/employees/stock_ledger/collections
- `_db_create_migration4.txt` (116 행) — migration_jobs/checkpoints/errors/etax

## 5. 사장님 5대 P0 (이번 주 ~5/17)

1. 마이그 이슈 — WS-20260514-01 발행, V3 60만/60분 통과
2. 국세청 API — WS-20260514-02 신규
3. ERP 속도 — WS-20260514-03 신규
4. DB 전수조사 — WS-20260514-04 신규
5. AI 챗봇 — WS-20260514-05 신규 (PRD 11→4일 압축)

## 6. P0 보고서 12종 마감

5/15 09:00 → **5/16 09:00 (사장님 하루 연기 결재)**

## 7. 헌법 위반 P0 6건 → 보고서 26번 액션 플랜 line:번호 명시

1. #20 거대 단일 tx (MdbMigrationService.cs L:164-202)
2. #23 SAST/DAST CI/CD 가동 (.github/workflows 3개)
3. #5 migration_errors.raw_data AES INSERT
4. #13 OLEDB ORDER BY 11개 (DOCF8/DOCFS/DOCSW 등)
5. #3 stock_ledger 5K 청크
6. #25 Sticky 헤더 + 카드

## 8. 5/14 04:50 DB 실측 (마이그 보존 확인 ✅)

| 영역 | 실측 |
|---|---|
| collections | 614,212 |
| stock_ledger | 116,420 |
| 핵심 17 테이블 총합 | 1,135,929행 ✅ |
| ENGINE=InnoDB 위반 | 0건 ✅ |
| utf8mb4_unicode_ci 위반 | 0건 ✅ |
| 좀비 트랜잭션 | 종료 추정 |

## 9. 05:30 학습 마무리 — 보고서 27번부터 보시면 됩니다 🫡

## 10. 2026-08-15 기기 슬롯 과금 1차 (SOP [4] 최종 검증)

| 문서 | 판정 |
|---|---|
| [`20260815_작업리뷰서_기기슬롯_1차_계수단일화.md`](20260815_작업리뷰서_기기슬롯_1차_계수단일화.md) | 🔴 **조건부 승인** — P0 1건(R-1 신규설치 시 정책표 영구 공백) 봉합 후 재검증. 3관문 전부 실측 통과(빌드 0/0·시험 504·ddl-smoke PASS), 코드리뷰 6건 중 3건 재현·1건 반증, 충돌 4건 코드로 판정, 독립반증 4건 |
