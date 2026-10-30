# 외부검증 의뢰 1/3 — MySqlConnector 2.5.0 → 2.6.2 판 올림 · SQL 해석 변화

| 항목 | 내용 |
|---|---|
| 발행 시각 | 발행 = 이 문서가 main 에 머지된 시각(미회신 기산점) |
| 기준 SHA | `6b15caf92ea124ddc8e06d664560c83ba2484d59` — 브랜치 `fix/20261030-MySqlConnector-판올림`(PR #729 초안) 머리 |
| 🔴 SHA 고정 링크 | 아래 링크는 전부 `blob/6b15caf9…/…` · `blob/main` 을 쓰지 않았다 · PR #729 는 회신 또는 미회신 처리까지 force-push·rebase·코드 커밋을 하지 않는다(문서 커밋만) |
| CI | Build & Test 6잡 🟢 — https://github.com/gisskso-lab/hitpan-erp/actions/runs/41015242496 |
| 변경 범위 | 프로젝트 파일 8개의 `MySqlConnector` 참조 판 2.5.0 → 2.6.2 · 꺼 두었던 꾸러미 경고 억제 1줄을 주석으로 돌림 · C# 코드 0줄 |
| 라운드 | 1 / 최대 3 |
| 미회신 | 발행 후 **24시간** — 회신 없으면 「미회신(≠ 통과)」으로 적고 대표가 진행/대기를 정한다 |
| 대상 결정 | 3인(PM·검증팀장·보안상무) 2:1 대상 → CTO 🟡 조건부 → **대표(최종 범위) 🟢 10/30** → 발행 |
| 이미 앎 | 귀하의 예비 독립대조(PR #729 댓글 #6517631576 · 위 CI 런 원본 로그 판독) — 이번 회신에서 같은 내용은 새 발견으로 세지 않는다 |
| 상태 | 이 의뢰로 어떤 단계도 닫히지 않는다 |

## 🔴 부탁
- 이 레포는 공개다. 검토 중 **이 범위 밖**의 결함을 보시면 공개 댓글에 파일·줄·재현을 적지 말고 「범위 밖 발견 N건 — 비공개 전달 요청」 한 줄만 남겨 주십시오.
- 권한은 **의심 제기**뿐입니다(판정·수정·게시 없음).
- 같은 SHA 와 main 에는 우리 쪽 판정 문서(검토서·이슈 기록·명세서·작업지시서 등)가 함께 있습니다. **그 문서들은 읽지 말고 코드와 SQL 로만 판단**해 주십시오 — 외부 판단이 우리 결론에 끌리지 않게 하려는 것입니다.

## 대상 (기준 SHA 고정)

**① 바뀐 파일 — 프로젝트 파일 8개**

| # | 파일 |
|---|---|
| 1 | [`src/HitPan.API/HitPan.API.csproj`](https://github.com/gisskso-lab/hitpan-erp/blob/6b15caf92ea124ddc8e06d664560c83ba2484d59/src/HitPan.API/HitPan.API.csproj) |
| 2 | [`src/HitPan.Application/HitPan.Application.csproj`](https://github.com/gisskso-lab/hitpan-erp/blob/6b15caf92ea124ddc8e06d664560c83ba2484d59/src/HitPan.Application/HitPan.Application.csproj) |
| 3 | [`src/HitPan.Backoffice.API/HitPan.Backoffice.API.csproj`](https://github.com/gisskso-lab/hitpan-erp/blob/6b15caf92ea124ddc8e06d664560c83ba2484d59/src/HitPan.Backoffice.API/HitPan.Backoffice.API.csproj) |
| 4 | [`src/HitPan.Infrastructure/HitPan.Infrastructure.csproj`](https://github.com/gisskso-lab/hitpan-erp/blob/6b15caf92ea124ddc8e06d664560c83ba2484d59/src/HitPan.Infrastructure/HitPan.Infrastructure.csproj) |
| 5 | [`src/HitPan.IntegrationTests/HitPan.IntegrationTests.csproj`](https://github.com/gisskso-lab/hitpan-erp/blob/6b15caf92ea124ddc8e06d664560c83ba2484d59/src/HitPan.IntegrationTests/HitPan.IntegrationTests.csproj) |
| 6 | [`tools/MdbMigrationBaselineRun/MdbMigrationBaselineRun.csproj`](https://github.com/gisskso-lab/hitpan-erp/blob/6b15caf92ea124ddc8e06d664560c83ba2484d59/tools/MdbMigrationBaselineRun/MdbMigrationBaselineRun.csproj) |
| 7 | [`tools/MigrationIdempotencyCheck/MigrationIdempotencyCheck.csproj`](https://github.com/gisskso-lab/hitpan-erp/blob/6b15caf92ea124ddc8e06d664560c83ba2484d59/tools/MigrationIdempotencyCheck/MigrationIdempotencyCheck.csproj) |
| 8 | [`tools/MigrationSmokeTest/MigrationSmokeTest.csproj`](https://github.com/gisskso-lab/hitpan-erp/blob/6b15caf92ea124ddc8e06d664560c83ba2484d59/tools/MigrationSmokeTest/MigrationSmokeTest.csproj) |

**② 판 올림의 영향을 받는 것 — 이 레포의 SQL**
- C# 파일 안의 SQL 문자열(Dapper · 직접 명령) — `src/`·`tools/` 아래 `.cs`
- `.sql` 파일과 그것을 읽어 실행하는 코드 — 특히 [`src/HitPan.API/Migrations/SQL/`](https://github.com/gisskso-lab/hitpan-erp/tree/6b15caf92ea124ddc8e06d664560c83ba2484d59/src/HitPan.API/Migrations/SQL) · 신규 설치 스키마 [`installer/hitpan_db_clean.sql`](https://github.com/gisskso-lab/hitpan-erp/blob/6b15caf92ea124ddc8e06d664560c83ba2484d59/installer/hitpan_db_clean.sql) 과 **그 실행 경로**(어떤 프로그램이 실행하는가 · MySqlConnector 를 거치는가)

## 질문 (하나)
**MySqlConnector 2.5.0 → 2.6.2 의 SQL 해석 변화(주석 인식 · 문장 나눔 · 매개변수 인식) 때문에, 이 레포의 SQL 에서 매개변수가 치환되지 않거나, 문장이 빠지거나 합쳐지는 자리가 있는가?**
- 체크아웃 줄바꿈 **LF · CRLF 각각**에서 봐 주십시오.
- 답은 **파일:줄 + SQL 조각**으로 · 없으면 「본 범위 · 본 방법」 과 함께 「발견 없음」 으로.
- 실행으로 확인하셨다면 판·환경을, 읽기만으로 판단하셨다면 그렇다고 적어 주십시오.
