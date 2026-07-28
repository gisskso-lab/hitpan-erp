# Velopack 자동 업데이트 Phase 2 통합 계획

> 작성일 : 2026-05-29 (선제 박제, 정식 적용 시점 정식 출시 후)
> 단계  : Phase 1 = 워치독 WS-28-D 자체 재설치만 / **Phase 2 = Velopack delta 자동 갱신**
> 헌법 #29 정합 : Phase 2 발진 = 사장님 결재 후만

---

## 0. 도입 배경

### Phase 1 (5/29 박제 완료)
- WS-28-D: cloudflared Service 자동 재설치
- 워치독 EXE 자체 갱신 = 불가 (현재는 Installer 재실행만)
- 신규 빌드 배포 = Installer 재배포 필요

### Phase 2 도입 효과
- 워치독 EXE delta 자동 갱신 (수십 KB 다운로드)
- 사고 발생 시 EXE 자동 패치 → 무인 봉합
- 본사 결재 1회로 10,000 고객사 동시 패치 가능
- 헌법 #28 EXE 자동 갱신 명문화 정합

---

## 1. 도입 시점 조건

| 조건 | 충족 여부 |
|---|---|
| 베타 4주차 종료 (7/12) | ⏳ 미충족 |
| 본사 사고 0건 박제 | ⏳ 미충족 |
| 본사 서명 EXE 정식 적용 | ⏳ 미충족 |
| 사장님 결재 | ⏳ 미충족 |
| 본사 업데이트 서버 박제 | ⏳ 미충족 |

→ 정식 출시 후 1개월 평가 → 사장님 결재 → Phase 2 발진.

---

## 2. 통합 시퀀스 (정식 적용 시)

### Step 1. NuGet 패키지 추가
```xml
<PackageReference Include="Velopack" Version="0.0.x" />
```

### Step 2. Program.cs 진입점 직후
```csharp
using Velopack;
VelopackApp.Build().Run();   // ← 자동 갱신 핸들러 등록
```

### Step 3. 본사 빌드 파이프라인
```bash
# 본사 서버 (사장님 결재 후)
dotnet publish src/HitPan.Watchdog -c Release -r win-x64 -p:PublishSingleFile=true
vpk pack -i HitPan.Watchdog -v 1.1.0 -o ./releases
# 결과: ./releases/HitPanWatchdog-1.1.0-full.nupkg + delta
```

### Step 4. 본사 Cloudflare R2 (또는 S3) 업로드
- `https://updates.hitpan.kr/HitPanWatchdog/{version}/`
- TLS 1.3 + 본사 서명 hash 검증

### Step 5. 워치독 자동 체크 (5분 주기)
```csharp
public class WS28J_AutoUpdate : BackgroundService
{
    private readonly UpdateManager _mgr = new("https://updates.hitpan.kr/HitPanWatchdog");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var info = await _mgr.CheckForUpdatesAsync();
            if (info != null && IsSafeToUpdate())
            {
                await _mgr.DownloadUpdatesAsync(info);
                _mgr.ApplyUpdatesAndRestart(info);   // 자동 재시작
            }
            await Task.Delay(TimeSpan.FromMinutes(60), ct);
        }
    }

    private bool IsSafeToUpdate() =>
        DateTime.Now.Hour >= 2 && DateTime.Now.Hour <= 5; // 새벽 2~5시만
}
```

---

## 3. 보안 검증

| 항목 | 박제 |
|---|---|
| 본사 서명 EXE만 적용 | Authenticode 또는 Velopack 자체 서명 |
| sha256 다운로드 검증 | Velopack 내장 |
| 사용자 PC 무단 변경 차단 | EXE 위치 ACL + 워치독 자체 검증 |
| 롤백 가능 | 직전 EXE 보관 + 실패 시 자동 복귀 |
| 본사 데이터 0 (헌법 #22) | 업데이트 패키지만, 고객 데이터 0 |

---

## 4. Phase 2 발진 후 워치독 9 → 10단계 확장

| 단계 | 기존 / 신규 |
|---|---|
| WS-28-A ~ I | Phase 1 박제 (기존) |
| **WS-28-J** (신규) | Velopack 자동 업데이트 |

---

## 5. 비용

| 항목 | 월 |
|---|---|
| Cloudflare R2 storage (10,000 PC × delta 50KB × 월 4 빌드) | < 1만원 |
| 본사 빌드 파이프라인 | 기존 GitHub Actions로 충분 |
| **합계** | **1만원 미만** |

10,000 고객사 운영 가능.

---

## 6. 5중 검증 통과 의무 (헌법 #23)

| 단계 | 검증 |
|---|---|
| ① 작업지시서 | Phase 2 발진 사장님 결재서 박제 |
| ② 어벤져스 리뷰 | 백엔드 + 보안1 + 보안2 + 설계 + DB |
| ③ SAST | CodeQL + TruffleHog + Roslyn |
| ④ DAST | 본사 PC 5대 × 7일 자동 갱신 시뮬레이션 |
| ⑤ 데이터 최소주의 | 업데이트 메타 통신 = sha256 + 버전만 |

---

## 7. 발진 결재 양식 (Phase 2)

```
[Phase 2 — Velopack 자동 업데이트 정식 발진]

승인 시각 : __________
사전 조건 :
  [ ] 정식 출시 7/14 + 1개월 무사고
  [ ] 본사 빌드 서명 박제
  [ ] 본사 R2 storage 박제
  [ ] 5중 검증 5/5 PASS

[ ] 승인
[ ] 반려 (사유: __________)
```

---

## 8. 결론

Phase 2는 정식 출시 후 1개월 평가 + 사장님 결재 후 발진. 현재(5/29)는 stub 박제만.

`src/HitPan.Watchdog/AutoUpdate/VelopackUpdaterStub.cs` — Phase 2 통합 시 정식 클래스로 교체.

---

**문서 끝.**
