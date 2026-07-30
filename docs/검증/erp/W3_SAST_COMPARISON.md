# W3 SAST 도구 비교 — 히트판 ERP 헌법 #23 정적 분석 단계

> **문서번호:** W3-SAST-COMPARISON
> **작성일:** 2026-05-13
> **작성주체:** 보안 매니저 + AI수석 합동 사전 학습
> **목적:** 헌법 #23(AI 협업 코드 5중 검증) ③ SAST 단계 도구 선정
> **베타 D-Day:** 2026-07-15 (D-63)
> **범위:** 순수 조사 문서. 도구 설치/실행은 작17 작지서 발행 이후.

---

## 1. 배경 및 요구사항

### 1.1 헌법 맥락
- **헌법 #23 (바이브코딩 5중 검증):** AI(Claude·Cursor) 협업 코드는 ① 작업지시서 보안 요구사항 → ② 매니저 리뷰 → ③ **SAST(본 문서)** → ④ DAST(OWASP ZAP) → ⑤ 데이터 최소주의 검증 5중 통과 후 머지.
- **헌법 #18 v3:** 본사 ↔ 고객사 ERP 데이터 흐름 분리. 본사로 업무 데이터 전송 코드는 **컴파일 타임에 차단** 필요.
- **헌법 #22:** 데이터 최소주의. 개인정보/금융정보가 본사 경계를 넘는 흐름을 SAST가 검출해야 함.
- **헌법 #19:** errors 0 + warnings 0. SAST가 새 경고를 만들면 빌드 실패로 직결되므로 baseline 관리 필수.

### 1.2 히트판 ERP 기술 스택
| 영역 | 스택 | SAST 시사점 |
|---|---|---|
| Backend | ASP.NET Core 8 (C#) | C# 8/.NET 8 룰셋 필수 |
| Frontend | Blazor WebAssembly + MudBlazor | JS/WASM 경계 분석 필요 |
| ORM | Dapper(주력) + EF Core(병용) | **Dapper 문자열 연결 SQLi 탐지 핵심** |
| DB | MariaDB 11.4.10 | MySql.Data 드라이버 분석 |
| Auth | JWT + Refresh Token | 시크릿 키 하드코딩 탐지 |
| Deploy | Cloudflare Tunnel / EXE 설치형 | 인스톨러 내 시크릿 검출 |

### 1.3 베타 출시 D-63 일정 제약
- W3(현재) ~ W5(6/3): SAST 도입·튜닝
- W6 ~ W8: DAST + EVF 6대 영역
- W9(7/8 ~ 7/15): 베타 배포
- → **SAST는 W3~W5 3주 내 PR 게이트로 안정화 필수**

---

## 2. 4종 도구 개요

### 2.1 CodeQL (GitHub Advanced Security)
- **제작:** GitHub (Semmle 인수)
- **방식:** 코드를 데이터베이스로 변환 후 QL(Query Language) 쿼리로 취약점 탐지
- **C# 지원:** 1급 시민. .NET 8 공식 지원. Roslyn 기반 빌드 통합.
- **룰셋:** `security-extended`, `security-and-quality` 2종. OWASP Top 10 / CWE Top 25 광범위.
- **라이선스:** **퍼블릭 리포 무료**. 프라이빗 리포는 **GitHub Advanced Security 유료**($49/활성기여자/월).
- **CI 통합:** GitHub Actions 공식 `github/codeql-action@v3` 1-2시간 작업.
- **장점:** 데이터 흐름 분석(taint tracking) 최강. 헌법 #22 데이터 최소주의 검증에 가장 적합. 커스텀 쿼리로 "본사 컨트롤러에서 업무 테이블 SELECT 차단" 같은 헌법 #18 룰 작성 가능.
- **단점:** 빌드 시간 5~15분 추가. 프라이빗 리포 유료. QL 학습 곡선.

### 2.2 Snyk Code
- **제작:** Snyk (DeepCode 인수)
- **방식:** AI 기반 시맨틱 분석. 클라우드 SaaS + CLI.
- **C# 지원:** 정식 지원. .NET 8 OK. Blazor 서버 컴포넌트는 부분 지원.
- **라이선스:** Free Tier(개발자 1명, 100 테스트/월) / Team $25/dev/월 / Enterprise 별도.
- **CI 통합:** `snyk/actions/dotnet` GitHub Action. 30분~1시간 도입.
- **장점:** False positive 업계 최저 수준(공식 5~10%). 자동 수정 제안(Snyk Fix). UI 친화적, 한국 사용자 사례 많음.
- **단점:** 코드가 Snyk 클라우드로 업로드됨 → **헌법 #18 v3 위반 여지** (소스 자체는 업무 데이터 아니지만 사장님 결재 필요). On-prem 옵션은 Enterprise 한정.

### 2.3 Roslyn Analyzers (SecurityCodeScan + Microsoft.CodeAnalysis.NetAnalyzers)
- **제작:** Microsoft + 커뮤니티
- **방식:** Roslyn 컴파일러에 분석기 NuGet 패키지 주입. 빌드 시 즉시 경고/오류.
- **C# 지원:** 네이티브. .NET 8 완벽.
- **주요 패키지:**
  - `Microsoft.CodeAnalysis.NetAnalyzers` (Microsoft 공식, CA 시리즈)
  - `SecurityCodeScan.VS2019` (OWASP Top 10 특화, SCS 시리즈)
  - `Roslynator.Analyzers` (코드 품질)
  - `SonarAnalyzer.CSharp` (SonarSource 무료 룰)
- **라이선스:** **전부 무료/오픈소스**.
- **CI 통합:** `.csproj` PackageReference만 추가. CI 별도 설정 불필요. 5분 도입.
- **장점:** 빌드 통합 0초 오버헤드(이미 Roslyn 도는 중). 헌법 #19(warnings 0) 강제화에 최적. 에디터에서 즉시 표시.
- **단점:** 데이터 흐름 분석 얕음. 파일 간 taint tracking 약함. Dapper string concat SQLi는 잡지만 다중 함수 경유는 놓침.

### 2.4 TruffleHog
- **제작:** Truffle Security
- **방식:** Git 히스토리 + 파일 시스템 정규식 + 검증 API 호출로 **시크릿 탐지 전용**.
- **C# 지원:** 무관(언어 비의존, 텍스트 패턴 기반).
- **탐지 대상:** AWS 키, Cloudflare 토큰, JWT 시크릿, DB 비번, GitHub PAT 등 800+ detector.
- **라이선스:** **오픈소스 무료** (트러플허그 OSS). Enterprise는 유료(조직 대시보드).
- **CI 통합:** `trufflesecurity/trufflehog@main` Action. 10분 도입.
- **장점:** 검증된 시크릿(verified=true)만 알림 → false positive 거의 0. 히스토리 스캔으로 과거 누출 즉시 발견. 헌법 #21(appsettings.json 수정 금지)과 함께 시크릿 누출 차단.
- **단점:** 시크릿 외 취약점은 못 잡음. 단독 사용 불가, 보조 도구.

---

## 3. 비교 표

| 평가 기준 | CodeQL | Snyk Code | Roslyn Analyzers | TruffleHog |
|---|---|---|---|---|
| **C#/.NET 8 지원** | A+ (네이티브) | A | A+ (네이티브) | N/A (언어 비의존) |
| **Blazor WASM 지원** | B+ (서버 측 OK, 클라이언트 부분) | B | B+ | N/A |
| **Dapper SQLi 탐지** | A+ (taint tracking) | A | B (단일 메서드 한정) | N/A |
| **EF Core 분석** | A | A | A | N/A |
| **OWASP Top 10 커버** | 10/10 | 10/10 | 7/10 (A3·A5·A7 강함) | A2(시크릿)만 |
| **시크릿 스캐닝** | B (보조 기능) | B+ | C (CA5394 정도) | **A+ (전문)** |
| **False Positive** | 중 (튜닝 필요) | **저** | 중~고 | **극저** (검증된 것만) |
| **가격 (프라이빗 리포)** | $49/기여자/월 | $25/dev/월 (Team) | **무료** | **무료** (OSS) |
| **가격 (퍼블릭 리포)** | **무료** | Free Tier 한정 | **무료** | **무료** |
| **GitHub Actions 통합** | A (1~2h) | A (30m~1h) | A+ (5m, csproj만) | A+ (10m) |
| **빌드 시간 영향** | +5~15분 | +3~10분 (클라우드) | **0초** (이미 도는 중) | +30초~2분 |
| **커스텀 룰 작성** | A+ (QL 강력) | C (제한적) | A (분석기 직접 작성) | B (정규식 detector) |
| **헌법 #18 룰 작성** | **가능** (taint sink 지정) | 부분 가능 | 가능하나 수고 큼 | 해당 없음 |
| **헌법 #22 데이터 흐름** | **A+** | A | B | N/A |
| **한국어 자료** | 중 (영문 위주) | 중 (한국 지사 있음) | **A+** (MS Docs 한글) | 저 |
| **소스 외부 전송 여부** | 옵션 (자체 호스팅 가능) | **있음** (클라우드 SaaS) | **없음** (로컬 빌드) | **없음** (로컬 실행) |
| **데이터 주권 (헌법 #18)** | A | C (결재 필요) | **A+** | **A+** |
| **D-63 도입 현실성** | A (1주) | A+ (수일) | **A+ (즉시)** | **A+ (즉시)** |
| **유지보수 부담** | 중 | 저 | 저 | 저 |
| **종합 점수 (히트판)** | **9.0/10** | 7.5/10 | **8.5/10** | **9.0/10 (보조 1위)** |

---

## 4. 항목별 심층 분석

### 4.1 Dapper SQL 인젝션 탐지력
히트판은 Dapper 주력 ORM. 다음 같은 패턴이 위험:
```csharp
// 위험 패턴 (Dapper 문자열 결합)
var sql = $"SELECT * FROM partners WHERE name = '{userInput}'";
var rows = conn.Query<Partner>(sql);
```
- **CodeQL:** `cs/sql-injection` 쿼리가 Dapper `Query<T>`, `Execute`, `QueryAsync` 등을 sink로 인식. taint source(HttpRequest 파라미터)에서 sink까지 다중 함수 경유도 추적. **A+**.
- **Snyk Code:** Dapper 시그니처 데이터셋 포함. 동등 수준. **A**.
- **Roslyn (SecurityCodeScan):** SCS0026 룰이 Dapper 인지. 단, 같은 메서드 내 string concat만 탐지. 헬퍼 메서드 경유 시 놓침. **B**.
- **TruffleHog:** 해당 없음.

### 4.2 Blazor WebAssembly 특수성
- WASM 클라이언트 코드는 사용자 PC에서 실행 → **시크릿/토큰을 절대 포함하면 안 됨**.
- `wwwroot/appsettings.json`이 클라이언트에 노출됨 → TruffleHog로 빌드 산출물도 스캔 필수.
- XSS는 MudBlazor 컴포넌트 자체가 어느 정도 방어하나, `MarkupString` 사용처는 SAST 필수 탐지.
- **CodeQL의 `cs/xss` 쿼리가 Blazor `MarkupString` 인지**. Roslyn은 부분 지원.

### 4.3 헌법 #18 v3 / #22 커스텀 룰 작성
**시나리오:** 본사 백오피스 컨트롤러(`HitPan.BackOffice.API`)에서 고객사 업무 테이블(`sales_orders`, `purchase_orders`, `journal_lines` 등)을 SELECT/INSERT 시도 시 빌드 차단.

- **CodeQL 접근:**
  ```ql
  // 의사 코드
  from MethodCall mc, StringLiteral sql
  where mc.getEnclosingNamespace().matches("HitPan.BackOffice%")
    and sql.getValue().matches("%FROM sales_orders%")
  select mc, "헌법 #18 위반: 본사에서 업무 테이블 접근"
  ```
- **Roslyn 접근:** `DiagnosticAnalyzer` 직접 작성. C# 분석기 프로젝트 신설 필요. 1~2일 작업.
- **Snyk:** 커스텀 룰 제한적.
- **결론:** 헌법 #18/#22 강제화는 **CodeQL이 압도적**.

### 4.4 가격 시뮬레이션 (히트판 팀 규모)
- 현재 활성 기여자 추정: 사장님 + PM + 백엔드 3 + 보안 3 + 프론트 1 = **8명** (서브에이전트는 GitHub 계정 1개 공유 가정)
- 프라이빗 리포 `gisskso-lab/hitpan-erp`.

| 도구 | 월 비용 (프라이빗) | 연 비용 |
|---|---|---|
| CodeQL (GHAS) | $49 × 8 = $392 | $4,704 |
| Snyk Code Team | $25 × 8 = $200 | $2,400 |
| Roslyn Analyzers | $0 | $0 |
| TruffleHog OSS | $0 | $0 |
| **권장 조합 (Roslyn + TruffleHog + CodeQL)** | $392 | $4,704 |

> 베타 단계(7~9월)는 GHAS 트라이얼(30일 무료) + 퍼블릭 fork 임시 활용으로 비용 이연 가능. 정식 출시 후 결제 결재 필요.

---

## 5. 히트판 권장 조합

### 5.1 결론: **3종 조합 (Roslyn Analyzers + TruffleHog + CodeQL)**

| 도구 | 역할 | 게이트 위치 |
|---|---|---|
| **Roslyn Analyzers** | 1차 방어. 빌드 시 즉시 차단. 헌법 #19 warnings 0 강제화. | **로컬 빌드 + PR Build CI** |
| **TruffleHog** | 시크릿 누출 전용. Git 히스토리 + PR diff + 빌드 산출물(wwwroot) 스캔. | **PR pre-merge + 야간 히스토리 풀스캔** |
| **CodeQL** | 심층 데이터 흐름 분석. 헌법 #18/#22 커스텀 쿼리. | **머지 후 야간 배치 + 주 1회 풀스캔** |

### 5.2 Snyk Code 제외 이유
- **헌법 #18 v3 데이터 주권 충돌:** 소스 코드가 Snyk 클라우드로 업로드됨. 비록 업무 데이터는 아니지만, 본사 데이터 최소주의 헌법 #22 정신과 맞지 않음. On-prem은 Enterprise 한정으로 D-63 일정·예산에서 비현실적.
- CodeQL이 데이터 흐름 분석에서 동급 이상이며 self-hosted runner 옵션 존재.

### 5.3 단일 도구 vs 다중 조합
- **단일 도구는 권장하지 않음.** 4종 도구가 커버하는 영역이 명확히 다름.
- **최소 구성 (Phase 1):** Roslyn + TruffleHog (둘 다 무료, 즉시 도입)
- **확장 구성 (Phase 2):** + CodeQL (헌법 #18/#22 커스텀 룰 작성 후)

---

## 6. 도입 일정 (W3 ~ W5, 3주)

### W3 (2026-05-13 ~ 05-19) — Roslyn + TruffleHog 즉시 도입
- **D1 (5/13):** 본 문서 사장님 결재
- **D2 (5/14):** 작17_SAST_도입 작지서 발행
- **D3~D4 (5/15~16):** Roslyn NuGet 패키지 추가
  - `Microsoft.CodeAnalysis.NetAnalyzers` (Microsoft 공식)
  - `SecurityCodeScan.VS2019`
  - `SonarAnalyzer.CSharp`
  - 모든 `.csproj`에 `<AnalysisLevel>latest</AnalysisLevel>` + baseline `.editorconfig` 작성
- **D5 (5/17):** TruffleHog GitHub Action 워크플로우 추가
  - PR diff 스캔: `trufflehog --only-verified`
  - 야간 히스토리 풀스캔
- **D6~D7 (5/18~19):** 기존 경고 baseline 처리, CI 게이트화

### W4 (2026-05-20 ~ 05-26) — CodeQL 도입
- **D1~D2:** GitHub Advanced Security 트라이얼 활성화 + `codeql-config.yml` 작성
- **D3~D4:** `security-extended` 쿼리팩 전수 스캔, false positive 정리
- **D5~D7:** PR 게이트 등록 + 야간 풀스캔 cron 설정

### W5 (2026-05-27 ~ 06-02) — 헌법 #18/#22 커스텀 쿼리
- **D1~D3:** 헌법 #18 위반 패턴 CodeQL 쿼리 작성
  - `hitpan/backoffice-touches-tenant-table.ql`
  - `hitpan/erp-pushes-to-backoffice.ql`
- **D4~D5:** 헌법 #22 데이터 최소주의 taint 쿼리
  - 개인정보(주민번호·계좌·전화) sink 추적
- **D6~D7:** 5중 검증 통합 리허설 (작업지시서 → 매니저 → SAST → DAST 모의 → 데이터 최소주의)

### W6 이후 — 운영 단계
- 주 1회 풀스캔 리포트 → 보안 매니저 검토
- 신규 PR은 3종 모두 PASS 필수 (헌법 #19 정합성)

---

## 7. CI/CD 통합 위치

```
[로컬 IDE]
   ↓ Roslyn (실시간 빨간줄)
[git commit]
   ↓
[git push → PR]
   ↓
[GitHub Actions PR 워크플로우]
   ├─ 1. dotnet build (Roslyn 분석기 자동 실행) → warnings 0 게이트
   ├─ 2. TruffleHog (PR diff, --only-verified)
   └─ 3. CodeQL (incremental, security-extended)
   ↓ 3종 PASS
[매니저 리뷰 (헌법 #23 ②)]
   ↓
[머지 to develop]
   ↓
[야간 배치 02:00 KST]
   ├─ TruffleHog 히스토리 풀스캔
   ├─ CodeQL 풀 데이터베이스 재빌드
   └─ 헌법 #18/#22 커스텀 쿼리
   ↓
[일일 보안 리포트 → 보안 매니저 + PM]
```

---

## 8. 작17_SAST_도입 작지서 초안 (1쪽)

```
[작업지시서 작17 — SAST 3종 통합 도입]

발행일: 2026-05-14
발행처: PM(닥터스트레인지) + 보안 매니저
수신: 백엔드 개발팀(3명), DevOps 담당
근거: 헌법 #23 ③ SAST 단계, 본 문서 W3_SAST_COMPARISON.md

[배경]
바이브코딩 5중 검증 ③번 SAST 단계 미구현. 베타 D-63.

[목표]
Roslyn Analyzers + TruffleHog + CodeQL 3종 도입. PR 게이트화.

[작업 범위]
1. Roslyn (D3~D4, 5/15~16)
   - Directory.Build.props 신설
   - NuGet 추가: Microsoft.CodeAnalysis.NetAnalyzers,
     SecurityCodeScan.VS2019, SonarAnalyzer.CSharp
   - .editorconfig baseline 작성 (기존 경고는 suppress, 신규만 차단)

2. TruffleHog (D5, 5/17)
   - .github/workflows/trufflehog.yml 신설
   - PR diff: --only-verified
   - 야간 cron: --since-commit=HEAD~30days

3. CodeQL (W4, 5/20~26)
   - .github/workflows/codeql.yml
   - codeql-config.yml: security-extended 쿼리팩
   - GHAS 트라이얼 활성화 (사장님 결재 #1)

4. 헌법 #18/#22 커스텀 (W5, 5/27~6/2)
   - .github/codeql/hitpan-queries/ 신설
   - QL 쿼리 2개 작성 (본 문서 §4.3)

[절대 준수]
- 헌법 #19: 신규 PR warnings 0
- 헌법 #21: appsettings.json은 분석 대상에서 제외 (수정 금지)
- 기존 빌드 경고는 baseline 분리, 별도 작지서로 전수 정리

[검증 게이트]
- 매니저 리뷰 (보안·백엔드·DB)
- 사장님 결재 #1: GHAS 트라이얼 → 정식 결제 ($392/월)
- W5 종료 시 5중 검증 리허설 통과

[산출물]
- .github/workflows/{codeql,trufflehog}.yml
- Directory.Build.props, .editorconfig
- codeql/hitpan-queries/*.ql 2개
- docs/migration/W5_SAST_BASELINE.md (현재 경고 분류 보고서)
```

---

## 9. 리스크 및 완화책

| 리스크 | 영향 | 완화 |
|---|---|---|
| Roslyn 경고 폭발 (수백 건) | 헌법 #19 위반 베이스라인 | `.editorconfig`로 기존 경고는 `suggestion` 격하, 신규만 `error`. 별도 작지서로 점진적 해소. |
| CodeQL 빌드 시간 +15분 | 개발 속도 저하 | PR은 incremental, 풀스캔은 야간 배치. |
| GHAS 비용 $4,704/년 | 예산 결재 필요 | 베타 단계 트라이얼, 정식 출시 후 결제. 대안: self-hosted CodeQL CLI(무료) + 자체 결과 저장. |
| TruffleHog false positive | 알림 피로 | `--only-verified` 플래그 필수. detector 화이트리스트 관리. |
| 커스텀 QL 쿼리 학습 곡선 | W5 일정 지연 | AI수석이 쿼리 초안 작성, 보안 매니저 검증. |

---

## 10. 결론

- **권장 조합:** Roslyn Analyzers (1차·즉시·무료) + TruffleHog (시크릿·무료) + CodeQL (심층·유료 트라이얼)
- **도입 일정:** W3 Roslyn+TruffleHog → W4 CodeQL → W5 헌법 커스텀 쿼리. 6/2 완료.
- **CI 게이트:** PR(3종 자동) → 매니저 리뷰 → 머지 → 야간 풀스캔
- **Snyk Code 제외:** 헌법 #18 v3 데이터 주권 우려 + CodeQL과 기능 중복
- **헌법 #23 5중 검증 정합성:** ③ SAST 단계가 본 조합으로 채워지며, DAST(OWASP ZAP, W6~)와 함께 베타 출시 절대 게이트 통과 가능

> "보안에 영혼을 갈아넣은 풀스택" — 헌법 #25 ② 정확하게.
> 3종 SAST는 그 영혼을 코드 라인마다 새기는 첫 자물쇠다.

---

**문서 끝. 다음 결재 요청:**
1. 본 문서 사장님 결재 (W3 D1)
2. 작17_SAST_도입 작지서 발행 승인 (W3 D2)
3. GHAS 트라이얼 활성화 승인 (W4 D1)
4. GHAS 정식 결제 $392/월 사장님 결재 (베타 출시 후)
