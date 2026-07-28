# 작업지시서 20260722작3 — CI api\wwwroot 누락 봉합 (터널 404 진범)

> 발행: PM 브라운킴 / 2026-07-22
> SOP: 작지서 → CTO 결재 → 사장님 승인 → 봉합 → CI
> 대상 파일: `.github/workflows/build-installer.yml` (1개), 버전 1.2.37→1.2.38

---

## §1. 진범 (2026-07-22 Sandbox 실측 확정)

### 증상
- `localhost:5234` (web-server) = 로그인 정상 ✅
- `test1.hitpan.kr` (터널 → API:5257) = **404** ❌
- `curl 127.0.0.1:5257/health` = 200 (API 살아있음) / `curl 127.0.0.1:5257/` = **404**

### 실측 규명
```
Test-Path "C:\Program Files\HitPan\api\wwwroot\index.html"  = False  ← api엔 Blazor 없음
Test-Path "C:\Program Files\HitPan\web\wwwroot\index.html"  = True   ← web엔 있음(389개)
unzip -l hitpan-1.2.37.zip:  api/wwwroot = 0 files / web/wwwroot = 389 files
```

### 봉합 검증 (Sandbox 즉석)
```powershell
Copy-Item "...\web\wwwroot" "...\api\wwwroot" -Recurse -Force
Restart API → curl 127.0.0.1:5257/ = 200 ✅
```
→ **api\wwwroot를 채우니 루트 200. 진범 = api에 Blazor 정적자산 부재.**

### 코드 경로
- `HitPan.API/Program.cs:58-60`: `exeDir\wwwroot` 존재 시 API가 Blazor를 서빙(터널 접속용).
- `build-installer.yml:76-84`: `dotnet publish HitPan.API.csproj` **단독** publish → API는 Web을 참조 안 하므로 Blazor WASM 정적자산이 api 산출물에 안 들어감.
- `build-installer.yml:87-93`: Web publish는 `payload/web/wwwroot`에만 들어가고 api엔 복사 안 함.
- **∴ 지금까지 CI로 구운 모든 버전(1.2.35/36/37)은 터널 접속 시 404. 로컬(5234)만 됐음.**

이건 "복구설치 진범"이 아니었다(어제밤 §5-3 오판 정정). **최초설치든 복구설치든 CI EXE는 api\wwwroot가 비어 터널 404.** 어제 "최초설치는 됐다"는 로컬 5234 확인이었고, 터널은 그때도 404였을 것(브라우저 확인 안 했을 뿐).

---

## §2. 봉합 내용 (파일 1개)

`.github/workflows/build-installer.yml` — Web publish 스텝(L87-93) 직후 복사 스텝 1개 추가.

```yaml
      # ★ 봉합 (2026-07-22, 작3 — Sandbox 실측 진범): api\wwwroot 부재 → 터널 404.
      #   API 는 exeDir\wwwroot 가 있으면 Blazor 를 서빙한다(Program.cs:58-60, 터널 접속 경로).
      #   그런데 HitPan.API.csproj 단독 publish 는 Web 을 참조하지 않아 Blazor 정적자산이
      #   api 산출물에 안 들어간다. Web publish 는 payload/web/wwwroot 에만 놓여
      #   localhost:5234(web-server)만 되고 test1.hitpan.kr(터널→API:5257)는 404 였다.
      #   → Blazor WASM 산출물(web/wwwroot)을 api/wwwroot 로 복사해 API 도 서빙 가능하게 한다.
      - name: Copy Blazor static assets into api/wwwroot (tunnel serving)
        shell: pwsh
        run: |
          $src = 'installer/payload/web/wwwroot'
          $dst = 'installer/payload/api/wwwroot'
          if (-not (Test-Path $src)) { throw "web/wwwroot 없음 — Web publish 실패" }
          if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
          Copy-Item $src $dst -Recurse -Force
          $n = (Get-ChildItem $dst -Recurse -File).Count
          Write-Host "api/wwwroot 복사 완료: $n files"
          if ($n -lt 100) { throw "api/wwwroot 파일 수 비정상($n) — Blazor 산출물 확인" }
```

### 왜 "복사"인가 (대안 검토)
- **대안 A (채택): web/wwwroot → api/wwwroot 복사.** Sandbox에서 실측 검증된 방법. web publish는 그대로 두어 5234 경로 무손상. 최소 변경(헌법 #1 "추가만").
- 대안 B (반려): API가 Web을 ProjectReference. 빌드 그래프·DI·정적자산 병합이 얽혀 표면 큼. TreatWarningsAsErrors·단일파일 publish와 충돌 위험.
- 대안 C (반려): `PublishSingleFile` 안에 정적자산 임베드. Blazor WASM은 `_framework/*.wasm`을 파일시스템에서 서빙해야 해 임베드 부적합.

---

### 숨은 완결 효과 (CTO 결재 지적)
`.iss`의 FixupBlazor(api\wwwroot\appsettings.json ApiBaseUrl 정정)는 **파일이 없으면 no-op**이다. 지금까지 api\wwwroot가 비어서 그 정정이 항상 무동작 → 터널 경로 ApiBaseUrl이 안 박혔다. **이 복사 봉합으로 파일이 생겨야 FixupBlazor가 실동작해 `test1.hitpan.kr` ApiBaseUrl이 동일출처로 정정된다(CORS 안전망 완성).** 즉 이 한 스텝이 404 봉합 + ApiBaseUrl 정정을 동시에 살린다.

## §3. 게이트·검증

0. **★ 버전 범프 (REQUIRED — CTO blocking 보완)**: 봉합 diff에 **두 파일 1.2.37→1.2.38** 반드시 포함.
   - `Directory.Build.props:33` `<HitPanVersion ...>1.2.37</HitPanVersion>` → `1.2.38`
   - `installer/HitPan-Universal.iss:32` (AppVersion 폴백) `1.2.37` → `1.2.38`
   - 이 한 줄이 빠지면 "동일 버전 → 감지 0"이라 자동업데이트 실측(게이트5) 자체가 불가.
1. **버전 1.2.38** (Directory.Build.props + .iss). 채널 = Emergency(즉시 적용, 실측용).
2. CI 초록불 (빌드 0/0, TreatWarningsAsErrors 정합).
3. **CI 산출 zip 검증**: `api/wwwroot`에 index.html 포함(파일 수 ≥ 100).
4. **Sandbox 종단 실측**: 1.2.38 설치(백지) → 브라우저 `test1.hitpan.kr` 로그인 화면 뜸 = 봉합 성공.
5. **자동업데이트 실측**: 1.2.37 ERP가 1.2.38 감지 → 팝업(MainLayout·유지 확인) → Y → 실제 교체 → 재기동 후 1.2.38.

※ 4·5는 사장님 NCP 게시(#29) 후 사장님 Sandbox 실측.

---

## §4. 영향·리스크

- **영향 범위**: CI 워크플로우 1개 스텝 추가. 코드(C#) 무변경. web/5234 경로 무손상.
- **리스크**: 낮음. 복사 실패 시 throw로 CI 즉시 실패(침묵 통과 없음). 파일 수 게이트로 빈 복사 차단.
- **연쇄**: 이 봉합 후에야 팝업 유지 실측(작1)·게이트 A'안 자동전달(작2 후속)이 브라우저에서 검증 가능.

---

## §5. 미결(이 작지서 범위 밖)

- 복구설치 시 API 5000 포트로 뜬 건(어제 실측) — api\wwwroot와 별개 가능성. 1.2.38 백지설치 실측으로 재현되는지 먼저 확인 후 별도 작지서(20260721작1 hang과 함께).
- 워치독 자기교체(W4-3, B안) — 별도.
- **stale api\wwwroot (CTO 결재 지적, 기록만 — 이번 봉합 불필요)**: `.iss:95` [Files]는 `ignoreversion recursesubdirs`로 덮어쓰기·추가만 하고 소스에서 사라진 파일은 안 지운다. `{app}\api\wwwroot` 사전 DelTree 없음. 이번 릴리스엔 무해(api\wwwroot가 처음 생기는 것). 단 **향후 Blazor 자산 파일명이 바뀌는 릴리스** 전엔 `{app}\api\wwwroot`(및 web) 사전 정리를 별도 작지서로 다뤄야 함. web\wwwroot도 동일 플래그로 출하 중이라 이 봉합이 새로 만든 회귀는 아님.

---

*PM 브라운킴 / 20260722작3 / CTO 결재 요청*
