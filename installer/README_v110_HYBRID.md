# HitPan ERP Installer v1.1.0 — Hybrid (Inno + Velopack)

> 헌법 #27·#28·#29·#31 정합. 40시간 하부르타 합의 결과 (5/27~28).
> 기존 `README.md` (v1.0.7) 보존, 이 문서는 v1.1.0 신규 가이드.

## 디렉터리

```
installer/
├── HitPanSetup.iss             # 신규 Inno Setup 스크립트
├── scripts/
│   ├── AntivirusExceptions.ps1
│   ├── FirewallRules.ps1
│   ├── InstallCloudflared.ps1
│   ├── InstallWatchdog.ps1
│   └── SelfCheck.ps1
├── payload/                    # 빌드 시 채움 (gitignore)
├── docs/                       # 백신 매뉴얼 PDF
└── output/
```

## 빌드

```powershell
dotnet publish src\HitPan.API -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o installer\payload\HitPan.API
dotnet publish src\HitPan.Web -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o installer\payload\HitPan.Web
dotnet publish src\HitPan.Watchdog -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o installer\payload\HitPan.Watchdog
Copy-Item C:\path\cloudflared.exe installer\payload\
Copy-Item C:\path\mariadb-11.4.10-winx64.msi installer\payload\
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\HitPanSetup.iss
```

## 사용자 경험

1. EXE 더블클릭
2. 라이선스 키 16자 입력 (사용자 입력은 이게 전부)
3. 자동:
   - 백신 5종 예외 (자동 4종 + 매뉴얼 2종 안내)
   - 방화벽 4 규칙
   - MariaDB silent install
   - cloudflared 프로비저닝 + Service
   - 워치독 Service + Guardian 5분 주기
   - 5분 자가 점검 (/health 200)
4. 완료

## v1.0.7 → v1.1.0 변경점

- 워치독 통합 (WS-28-A~I 9단계)
- 백신 4종 자동 예외 → 본사 PC 5대 격리 0건 검증 의무
- 5분 자가 점검 PASS 게이트 추가
- 본사 메타 ping (헌법 #22 정합) 박제
- 라이선스 키 외 사용자 입력 0
