# 외부 지적서 원문 — MySqlConnector 판 올림 [4-X] 1/3 (HQA · 무수정)

| 항목 | 내용 |
|---|---|
| 의뢰 | [외부검증의뢰 MySqlConnector 1차](20261030_외부검증의뢰_MySqlConnector판올림_1차.md) · 발행 #733 머지 `5bc34896` 10/30 17:32 KST |
| 회신 | https://github.com/gisskso-lab/hitpan-erp/pull/733#issuecomment-6519566937 · 2026-10-30T09:33:23Z(18:33 KST) — 발행 1시간 뒤 |
| 기준 SHA | `6b15caf92ea124ddc8e06d664560c83ba2484d59`(#729) |
| 상태 | 🟡 HQA 「확정 결함 0 / 검증 미완료」 → PM 대응 · 3인 · CTO 대조 대기 |

## 회신 원문

## HQA ARGOS — MySqlConnector 정식 [4-X] **1/3 독립 회신 (부분 측정 · 최종 판정 보류)**

**의뢰:** #733의 main 머지 `5bc348968ec801f0098c833acbedaf458592fe1b`(2026-10-30 17:32:14 KST)에 따라 발행된 1/3. **고정 대상 SHA:** `6b15caf92ea124ddc8e06d664560c83ba2484d59` (#729). 범위 = 프로젝트 파일 8개 + 레포 SQL 및 실행 경로. 이 회신은 대표 머지 결재·운영 안전 확인이 아니며, 2/3·3/3 회신도 아닙니다.

### 질문에 대한 현재 답
**직접 확인한 제한 범위에서는 2.5.0→2.6.2 변경 때문에 매개변수 미치환·문장 누락·합쳐짐이 발생한 *재현 사례 0건*. 단, 전수 SQL 검사와 두 드라이버 버전의 LF/CRLF 실제 실행 대조를 하지 못했으므로 ‘레포 전체 발견 없음’ 또는 ‘SQL 해석 변화 없음’으로 판정하지 않습니다. 상태: 🟡 미완료/추가 검증 필요.**

### 독립 확인(같은 SHA의 내부 [3-V]/[4] 판정문·발견 장부는 판단 근거에서 제외)
1. **참조판:** 8개 `.csproj`의 XML 주석을 제외한 *활성* `PackageReference Include="MySqlConnector"`는 파일마다 정확히 1개, 모두 **2.6.2**. Application의 활성 `NoWarn NU1902` 0. #729 diff의 C# `.cs` 변경 0. 주석에 남은 종전 2.5.0은 활성 참조가 아닙니다.
2. **실제 SQL 실행 경로:** `src/HitPan.Application/Services/MigrationRunner.cs:110~137`은 `DB-*.sql`을 UTF-8로 읽고 전체 SQL을 Dapper `ExecuteAsync` 한 번에 전달합니다. `src/HitPan.Infrastructure/Persistence/MigrationDbConnectionFactory.cs:52~66`에는 `AllowUserVariables=true`가 있어 SQL의 `@col_exists`, `@sql`을 매개변수로 오인하는 기존 유형에 대한 방어 설정을 확인했습니다. 이는 2.6.2의 실제 해석 성공을 직접 측정한 것은 아닙니다.
3. **SQL 원문 표본:** `DB-147_form_templates_tear_line.sql:43~49`, `DB-148_form_watermark_print_background.sql:52~72, 91~110`, `DB-149_show_watermark_three_values.sql:29~41`의 `SET @...`, 문자열 안 따옴표 이스케이프, `PREPARE/EXECUTE/DEALLOCATE`를 정적으로 대조했습니다. 세 파일에는 `DELIMITER` 지시문 0건. 이 표본의 매개변수/문장 손실은 **실행으로 확인하지 못했습니다**.
4. **신규 설치 SQL은 다른 실행기:** `installer/hitpan_db_clean.sql` 5,068행에 `DELIMITER` 지시문 6개(예: 2791~2793, 3987~3999), 버전 조건 주석과 트리거가 있습니다. 하지만 `.github/workflows/build-and-test.yml:350~355`와 `scripts/ddl-smoke-test.sh:22~43`의 이 파일 적재는 **mysql CLI 입력 리다이렉션**이며, MySqlConnector 2.6.2의 파서 시험으로 볼 수 없습니다. 반대로 이 SQL을 그대로 `MySqlCommand`로 보내는 경로가 있다면 `DELIMITER`는 CLI 지시문이므로 별도 검증 대상입니다. *그런 실제 경로는 이번 검토에서 확인하지 못했습니다.*
5. **LF/CRLF:** 위 SQL 4개(신규 설치 DDL + DB-147/148/149)의 저장 원문은 LF였으며, LF→CRLF→LF 텍스트 정규화 시 본문 동일을 확인했습니다. **이 검사는 문자열 보존만 증명하며 두 줄바꿈을 2.5.0·2.6.2 파서에 넣어 실행한 결과가 아닙니다.**
6. **버전 변경의 관련성:** MySqlConnector 공식 버전 이력(https://mysqlconnector.net/overview/version-history/)의 **2.6.0**에 주석 시작 문자 처리·인용된 매개변수 종료·매개변수 재작성 관련 수정이 명시되어 있어, 질문의 파서 변화 가능성은 실제 업스트림 변경과 관련 있습니다. 다만 해당 변경이 위 SQL을 깨뜨린다는 직접 증거는 없습니다.

### 기존 CI와 구분
`41015242496`의 6잡 success, Build 로그의 오류/경고 0, DB Integrity `813/813` 및 DDL Smoke success는 **원본 job 단계·로그를 직접 재판독**했습니다. 이것은 #729의 기존 HQA 예비 대조(#6517631576)와 동일한 증거이므로 **새 발견으로 중복 계상 0**. CI가 DB-147/148/149 전부를 *두 버전·두 줄바꿈 조합*으로 실행했다는 증거는 찾지 못했습니다. TPM 실물 시험은 SKIP, 운영 DB/고객 환경은 HQA 미접촉입니다.

### PM·[3-V]·[4]·CTO 요청
**같은 고정 SHA**에서 (a) 실제 MySqlConnector 경유 SQL 목록·실행기(DDL CLI와 분리), (b) `DB-*.sql` 전수 정적 검사 및 C# SQL 문자열의 인용/주석/매개변수 표본, (c) 승인된 격리 CI 환경에서 2.5.0↔2.6.2 × LF↔CRLF의 결과/오류/실행수/스키마 대조군을 제시해 주십시오. (c)는 **HQA가 신규 CI를 실행하라는 요청이 아니라 검증 공백의 명시**입니다. PM은 수용·반박·추가검증 필요 여부를, CTO는 이 부분회신을 ‘전수 통과’로 바꾸지 않은 상태에서 대조 바랍니다.

**결론:** 이 1/3의 질문은 **확정 결함 0 / 검증 미완료**. 위험 해소·전체 통과·#729 머지 승인 판정은 보류. HQA 신규 CI dispatch/재실행, 실물 DB·운영 접속, 코드/워크플로 변경, 머지 **0**. 공개 점검: 실제 비밀값·토큰·고객정보·범위 밖 취약 상세 없음.
