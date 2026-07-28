# GitHub Actions YAML 6건 박제 (5중 검증 자동화)

> **작성일**: 2026-05-26 (W5 자동 가도)
> **정합**: `docs/헌법/5중검증_자동화_파이프라인.md` (7영역 매트릭스)
> **헌법 정합**: #19 (warnings 0) / #22 (데이터 최소주의) / #23 (AI 협업 5중 검증) / #31 (OS 보안 도구 호환성)
> **결재**: 사장님 "응 다음결재" (2026-05-26, W5 자동 가도)
> **실 배포**: 본 문서는 박제만, `.github/workflows/` 디렉터리 실파일 커밋은 W6 가도

---

## 0. 본 문서의 존재 이유

> 사장님 헌법 #23: *"AI 협업 코드는 5중 검증 통과 후 머지. 1개라도 실패 시 머지 금지."*

GitHub Actions가 사람의 손이 잊은 검증을 강제 박제. 6 YAML + branch protection rule + CODEOWNERS = 자동 게이트.

---

## 1. `.github/workflows/sast-codeql.yml` — CodeQL 정적 분석

```yaml
name: SAST - CodeQL
on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main, develop]
  schedule:
    - cron: '0 18 * * 0'   # 매주 일요일 03:00 KST

permissions:
  actions: read
  contents: read
  security-events: write

jobs:
  analyze:
    name: Analyze (${{ matrix.language }})
    runs-on: ubuntu-latest
    timeout-minutes: 360
    strategy:
      fail-fast: false
      matrix:
        language: ['csharp', 'javascript']
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 8
        if: matrix.language == 'csharp'
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Initialize CodeQL
        uses: github/codeql-action/init@v3
        with:
          languages: ${{ matrix.language }}
          queries: security-extended,security-and-quality

      - name: Autobuild
        uses: github/codeql-action/autobuild@v3

      - name: Perform CodeQL Analysis
        uses: github/codeql-action/analyze@v3
        with:
          category: "/language:${{matrix.language}}"
          upload: always

      - name: Fail on HIGH severity
        run: |
          # GitHub Code Scanning API로 HIGH 이상 1건이라도 있으면 exit 1
          gh api /repos/${{ github.repository }}/code-scanning/alerts \
            --jq '[.[] | select(.rule.security_severity_level == "high" or .rule.security_severity_level == "critical")] | length' \
            > high_count.txt
          if [ "$(cat high_count.txt)" -gt 0 ]; then
            echo "HIGH 이상 발견 — merge 차단"
            exit 1
          fi
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}

      - name: Auto-create issue on failure
        if: failure()
        run: |
          gh issue create \
            --title "CodeQL HIGH 알림 (PR #${{ github.event.pull_request.number }})" \
            --body "5중 검증 ③ SAST CodeQL HIGH 발견. 머지 차단." \
            --label "security,blocker"
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**실패 정책**: HIGH 이상 1건 = merge 차단 + Issue 자동 등록.

---

## 2. `.github/workflows/sast-roslyn.yml` — Roslyn 헌법 #15·#16·#19 검증

```yaml
name: SAST - Roslyn
on:
  pull_request:
    branches: [main, develop]
  push:
    branches: [main, develop]

jobs:
  roslyn-analyzer:
    runs-on: ubuntu-latest
    timeout-minutes: 30
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build (warnings as errors) — 헌법 #19
        run: |
          dotnet build \
            --configuration Release \
            --no-restore \
            /warnaserror \
            /p:TreatWarningsAsErrors=true \
            /p:EnforceCodeStyleInBuild=true \
            /p:GenerateDocumentationFile=true

      - name: 헌법 #15 빈 catch 검증 (Roslyn 커스텀 룰)
        run: |
          # CA1031: Do not catch general exception types
          # S2737: catch 블록은 비어있으면 안 됨 (SonarAnalyzer)
          # 빈 catch 패턴 정규식 grep 보강
          if grep -rPzo 'catch\s*\([^)]*\)\s*\{\s*\}' --include='*.cs' src/; then
            echo "헌법 #15 위반 — 빈 catch 블록 발견"
            exit 1
          fi

      - name: 헌법 #16 MySqlConnection + Task.WhenAll 금지 검증
        run: |
          # MySqlConnection이 등장하는 파일에서 Task.WhenAll 동시 등장 시 차단
          for f in $(grep -rl 'MySqlConnection' --include='*.cs' src/); do
            if grep -q 'Task\.WhenAll' "$f"; then
              echo "헌법 #16 위반: $f — MySqlConnection + Task.WhenAll 조합 금지"
              exit 1
            fi
          done

      - name: 헌법 #4 decimal 검증 (금액 컬럼 float/double 차단)
        run: |
          # amount·price·rate·cost 변수에 float/double 사용 시 차단
          if grep -rPzo '(float|double)\s+(amount|price|rate|cost|fee)' --include='*.cs' src/; then
            echo "헌법 #4 위반 — 금액 컬럼은 decimal만 허용"
            exit 1
          fi
```

**실패 정책**: 1건이라도 실패 = merge 차단. 헌법 #15·#16·#19·#4 일괄 검증.

---

## 3. `.github/workflows/sast-trufflehog.yml` — 비밀번호·시크릿 누출

```yaml
name: SAST - TruffleHog
on:
  pull_request:
    branches: [main, develop]
  push:
    branches: [main, develop]

jobs:
  trufflehog:
    runs-on: ubuntu-latest
    timeout-minutes: 15
    steps:
      - name: Checkout (fetch-depth 0 — 전체 히스토리)
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: TruffleHog OSS
        uses: trufflesecurity/trufflehog@main
        with:
          path: ./
          base: ${{ github.event.pull_request.base.sha || github.event.before }}
          head: ${{ github.sha }}
          extra_args: --debug --only-verified --fail

      - name: 추가 정규식 검증 (Cloudflare API 토큰·MariaDB 비번)
        run: |
          # Cloudflare API 토큰 패턴 (40자 16진)
          if grep -rPzo '[a-zA-Z0-9_-]{40}' --include='*.cs' --include='*.json' src/ \
            | grep -i 'cloudflare\|cf_api'; then
            echo "Cloudflare API 토큰 누출 의심 — 머지 차단"
            exit 1
          fi
          # MariaDB 비번 하드코딩 패턴
          if grep -rPzo 'password\s*=\s*"[^"]+"' --include='*.cs' --include='*.json' src/ \
            | grep -v 'appsettings\|placeholder\|test'; then
            echo "비밀번호 하드코딩 의심 — 머지 차단"
            exit 1
          fi

      - name: Auto-create issue on detection
        if: failure()
        run: |
          gh issue create \
            --title "🚨 시크릿 누출 의심 (PR #${{ github.event.pull_request.number }})" \
            --body "TruffleHog 5중검증 ③ 검출. 즉시 git history rewrite + 시크릿 회전 필요." \
            --label "security,critical,blocker"
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**실패 정책**: verified secret 1건 = merge 차단 + critical 이슈 + 시크릿 회전.

---

## 4. `.github/workflows/sast-snyk.yml` — NuGet 취약성

```yaml
name: SAST - Snyk
on:
  pull_request:
    branches: [main, develop]
  push:
    branches: [main, develop]
  schedule:
    - cron: '0 21 * * *'   # 매일 06:00 KST nightly

jobs:
  snyk:
    runs-on: ubuntu-latest
    timeout-minutes: 30
    permissions:
      contents: read
      security-events: write
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore

      - name: Snyk Test (HIGH 차단)
        uses: snyk/actions/dotnet@master
        env:
          SNYK_TOKEN: ${{ secrets.SNYK_TOKEN }}
        with:
          args: >-
            --severity-threshold=high
            --fail-on=upgradable
            --sarif-file-output=snyk.sarif
            --all-projects

      - name: Upload SARIF
        if: always()
        uses: github/codeql-action/upload-sarif@v3
        with:
          sarif_file: snyk.sarif

      - name: Snyk Monitor (nightly만)
        if: github.event_name == 'schedule'
        run: snyk monitor --all-projects
        env:
          SNYK_TOKEN: ${{ secrets.SNYK_TOKEN }}

      - name: Auto-create issue on HIGH
        if: failure()
        run: |
          gh issue create \
            --title "Snyk HIGH 취약성 (PR #${{ github.event.pull_request.number }})" \
            --body "NuGet 패키지 HIGH 이상 취약성 검출. dotnet add package 또는 dotnet remove package 필요." \
            --label "security,blocker"
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**실패 정책**: HIGH 1건 = merge 차단 + 이슈 자동 등록. nightly는 monitor 모드로 트렌드 추적.

---

## 5. `.github/workflows/data-minimalism.yml` — 헌법 #22 금기 컬럼 grep

```yaml
name: Data Minimalism (헌법 #22)
on:
  pull_request:
    branches: [main, develop]
  push:
    branches: [main, develop]

jobs:
  data-minimalism-scan:
    runs-on: ubuntu-latest
    timeout-minutes: 10
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: 본사 영역 금기 컬럼 검증
        run: |
          set -e
          BACKOFFICE_DIRS="src/HitPan.Backoffice src/HitPan.Landing"

          # 금기 컬럼 패턴 (본사 코드에 등장하면 차단)
          FORBIDDEN_PATTERNS=(
            'sales_amount'
            'purchase_amount'
            'invoice_amount'
            'invoice_supply'
            'invoice_tax_amount'
            'customer_name'
            'vendor_name'
            'employee_name'
            'employee_ssn'
            'employee_resident'
            'ledger_balance'
            'journal_amount'
            'stock_quantity'
            'bom_quantity'
            '매출_'
            '매입_'
            '거래처_'
            '직원_'
            '세금계산서_'
            '원장_'
            '재고_'
          )

          FAIL=0
          for pat in "${FORBIDDEN_PATTERNS[@]}"; do
            for dir in $BACKOFFICE_DIRS; do
              if [ -d "$dir" ] && grep -rIn "$pat" "$dir" --include='*.cs' --include='*.sql' --include='*.json' 2>/dev/null; then
                echo "❌ 헌법 #22 위반: $dir 영역에 금기 컬럼 '$pat' 발견"
                FAIL=1
              fi
            done
          done

          if [ "$FAIL" -eq 1 ]; then
            echo ""
            echo "🚨 본사 코드는 업무 데이터 컬럼을 보유할 수 없습니다 (헌법 #22 데이터 최소주의)"
            echo "본사가 안 가지면 본사가 털릴 일 없다 — 사장님 헌법 2026-05-12"
            exit 1
          fi
          echo "✅ 본사 영역 금기 컬럼 0건 — 헌법 #22 정합"

      - name: ERP→본사 전송 코드 검증 (헌법 #18 v3)
        run: |
          # ERP 코드(src/HitPan.API)에서 본사로 업무 데이터 POST하는 코드 차단
          if grep -rIPzo 'backoffice.*Post.*\b(sales|purchase|invoice|ledger|stock|employee)\b' \
            src/HitPan.API --include='*.cs' 2>/dev/null; then
            echo "❌ 헌법 #18 위반: ERP가 본사로 업무 데이터 전송"
            exit 1
          fi
          echo "✅ ERP→본사 업무 데이터 전송 0건 — 헌법 #18 v3 정합"

      - name: Auto-create issue on violation
        if: failure()
        run: |
          gh issue create \
            --title "🚨 헌법 #22 데이터 최소주의 위반 (PR #${{ github.event.pull_request.number }})" \
            --body "본사 영역에 업무 데이터 컬럼 발견. 즉시 제거 필요." \
            --label "constitution,blocker"
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**실패 정책**: 본사 영역에 업무 데이터 컬럼 1건이라도 발견 시 즉시 merge 차단.

---

## 6. `.github/workflows/dast-zap.yml` — OWASP ZAP nightly

```yaml
name: DAST - OWASP ZAP
on:
  schedule:
    - cron: '0 15 * * *'   # 매일 00:00 KST nightly
  workflow_dispatch:       # 베타 출시 D-7부터 수동 실행
    inputs:
      target_env:
        description: 'Target environment'
        required: true
        default: 'demo'
        type: choice
        options: [demo, beta, prod]

jobs:
  zap-baseline:
    runs-on: ubuntu-latest
    timeout-minutes: 60
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Resolve target URL
        id: target
        run: |
          case "${{ github.event.inputs.target_env || 'demo' }}" in
            demo) echo "url=https://demo.hitpan.kr" >> $GITHUB_OUTPUT ;;
            beta) echo "url=https://beta.hitpan.app" >> $GITHUB_OUTPUT ;;
            prod) echo "url=https://www.hitpan.app" >> $GITHUB_OUTPUT ;;
          esac

      - name: ZAP Baseline Scan
        uses: zaproxy/action-baseline@v0.12.0
        with:
          target: ${{ steps.target.outputs.url }}
          rules_file_name: '.zap/rules.tsv'
          cmd_options: '-a -j -m 10 -T 60'
          fail_action: true     # HIGH 1건 = 베타 차단
          allow_issue_writing: true

      - name: ZAP Full Scan (베타 직전·prod)
        if: github.event.inputs.target_env != 'demo'
        uses: zaproxy/action-full-scan@v0.10.0
        with:
          target: ${{ steps.target.outputs.url }}
          rules_file_name: '.zap/rules.tsv'
          cmd_options: '-a -j -m 10 -T 240'
          fail_action: true

      - name: Upload ZAP Report
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: zap-report-${{ github.event.inputs.target_env || 'demo' }}
          path: |
            report_html.html
            report_md.md
            report_json.json

      - name: Auto-block release on HIGH
        if: failure()
        run: |
          gh issue create \
            --title "🚨 DAST ZAP HIGH (${{ steps.target.outputs.url }})" \
            --body "OWASP ZAP HIGH 검출. 베타 출시 차단. 5중 검증 ④ 실패." \
            --label "security,beta-blocker"
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**실패 정책**: HIGH 1건 = 베타 출시 차단. demo는 알림만, beta·prod는 release 브랜치 머지 차단.

---

## 7. Branch Protection Rule (수동 설정 영역)

GitHub Repo Settings → Branches → `main`·`develop` 브랜치 보호:

### 7.1 main / develop 공통
- ✅ Require a pull request before merging
- ✅ Require approvals: 최소 1명 (CODEOWNERS)
- ✅ Dismiss stale pull request approvals when new commits are pushed
- ✅ Require status checks to pass before merging:
  - `SAST - CodeQL / Analyze (csharp)`
  - `SAST - CodeQL / Analyze (javascript)`
  - `SAST - Roslyn / roslyn-analyzer`
  - `SAST - TruffleHog / trufflehog`
  - `SAST - Snyk / snyk`
  - `Data Minimalism (헌법 #22) / data-minimalism-scan`
- ✅ Require branches to be up to date before merging
- ✅ Require conversation resolution before merging
- ❌ Allow force pushes — 금지
- ❌ Allow deletions — 금지

### 7.2 release/beta-* (베타 출시 게이트)
- 위 7.1 모두 +
- ✅ `DAST - OWASP ZAP / zap-baseline` 24시간 이내 PASS 필수
- ✅ `AV Compatibility / av-test` 1주일 이내 PASS 필수 (W6 추가 가도)
- ✅ `Comms Integrity / scenario-17` 24시간 이내 PASS 필수 (W6 추가 가도)
- ✅ Required reviewers: PM + 보안 매니저 1·2 모두 승인 (헌법 #29)

---

## 8. CODEOWNERS 박제 (`.github/CODEOWNERS`)

```
# 헌법·거버넌스 영역
docs/governance/                   @pm @ai-chief
docs/design/                       @pm @ai-chief @backend-manager

# 보안 영역
src/**/Auth*                       @security-manager-1 @security-manager-2
src/**/Tenant*                     @security-manager-1
src/**/Jwt*                        @security-manager-1 @security-manager-2
.github/workflows/sast-*           @security-manager-1 @security-manager-2
.github/workflows/dast-*           @security-manager-1 @security-manager-2

# DB 영역
src/**/Repositories/               @db-manager
**/*.sql                           @db-manager
**/Migrations/                     @db-manager

# 백엔드 영역
src/HitPan.API/                    @backend-manager
src/HitPan.Application/            @backend-manager
src/HitPan.Backoffice/             @backend-manager @pm
src/HitPan.Landing/                @backend-manager @pm

# 프론트 영역
src/HitPan.Web/                    @frontend-manager @web-designer
*.razor                            @frontend-manager
*.css                              @web-designer

# 통신 무결성 영역 (헌법 #27, #28)
src/**/Watchdog*                   @security-manager-2 @backend-manager
src/**/Cloudflare*                 @security-manager-2

# 최상위 — PM 최종
/CLAUDE.md                         @pm
/README.md                         @pm
```

---

## 9. PULL_REQUEST_TEMPLATE.md 박제 (`.github/PULL_REQUEST_TEMPLATE.md`)

```markdown
## 작업지시서
- 번호:
- 영역:
- 결재자: 사장님 / PM

## 변경 요약


## 헌법 절대원칙 25개 자가 점검 (필수)
- [ ] #2 tenant_id는 JWT 클레임에서만 (파라미터 0건)
- [ ] #4 금액 컬럼은 decimal (float/double 0건)
- [ ] #15 빈 catch 0건 (전 catch에 `_logger.LogWarning`)
- [ ] #16 MySqlConnection + Task.WhenAll 조합 0건
- [ ] #18 v3 ERP→본사 업무 데이터 전송 0건
- [ ] #19 errors 0 + warnings 0
- [ ] #22 본사 영역 업무 데이터 컬럼 0건
- [ ] #23 5중 검증 7영역 PASS

## 5중 검증 결과
- ① 작업지시서 보안 요구사항: ___
- ② 어벤져스 매니저 리뷰: ___
- ③ SAST (CodeQL·Roslyn·TruffleHog·Snyk): ___
- ④ DAST (ZAP): ___ (해당 시)
- ⑤ 데이터 최소주의 스캔: ___
- ⑥ 백신 호환성: ___ (베타 직전)
- ⑦ 통신 무결성: ___ (베타 직전)

## 테스트 결과


## 리뷰어 (CODEOWNERS 자동)
```

---

## 10. 박제 결과 요약

| # | 영역 | YAML 파일 | 트리거 | 실패 시 |
|---|---|---|---|---|
| 1 | SAST CodeQL | sast-codeql.yml | PR + push + weekly | merge 차단 |
| 2 | SAST Roslyn (#15·#16·#19·#4) | sast-roslyn.yml | PR + push | merge 차단 |
| 3 | SAST TruffleHog | sast-trufflehog.yml | PR + push | merge 차단 + critical 이슈 |
| 4 | SAST Snyk | sast-snyk.yml | PR + push + nightly | merge 차단 |
| 5 | Data Minimalism (#22) | data-minimalism.yml | PR + push | merge 차단 |
| 6 | DAST OWASP ZAP | dast-zap.yml | nightly + manual | 베타 출시 차단 |
| +α | 백신 호환성 (#31) | av-compatibility.yml | W6 가도 | 베타 출시 차단 |
| +α | 통신 무결성 (#27) | comms-integrity.yml | W6 가도 | 베타 출시 차단 |

---

## 11. W6 실 배포 가도 예고

| 일자 | 산출물 |
|---|---|
| W6 D1 | 6 YAML 파일 `.github/workflows/` 실 커밋 |
| W6 D2 | CODEOWNERS + PR 템플릿 실 커밋 |
| W6 D3 | Branch protection rule 수동 설정 + 사장님 결재 |
| W6 D4 | Snyk·CodeQL token secrets 등록 (사장님 직접) |
| W6 D5 | dry-run 6 YAML 실행 + 위양성 튜닝 |
| W6 D6 | 백신 호환성·통신 무결성 YAML 추가 박제 |

---

**박제자**: PM 브라운킴 + 보안 매니저 1·2
**검증**: AI수석 + DB매니저 + 백엔드 매니저
**상태**: W5 자동 가도 박제 완료, 실 배포는 W6 가도
