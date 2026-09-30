using System.Diagnostics.CodeAnalysis;

// 2026-09-30 작1 갈래 U (설계 §13-2 링크 컴파일):
//   API csproj 가 워치독 원본 DbConfReader.cs 를 링크 컴파일하면, API 에만 붙어 있는 히트판 분석기(HP0015 · 헌법 #15)가
//   그 파일의 Parse 안 '주석만 있는 catch' 를 잡는다. 그 catch 는 워치독 운영 코드 그대로이고(워치독 프로젝트엔 분석기가 없다),
//   이 트랙은 워치독 파일 수정 0(G-43)이다. 그래서 고치지 않고 '그 메서드 하나'에만 범위를 좁혀 억제한다.
//   · 억제 범위 = DbConfReader.Parse 한 메서드. 수동 모듈 자기 코드의 catch 는 전부 로그를 남긴다(억제 대상 아님).
//   · 동작 의미: db.conf 를 못 읽으면 빈 사전 → 서명 공개키는 내장 상수로 폴백(워치독과 같은 판정 · fail-closed 유지).
//   · 나중에 링크 파일을 공용 라이브러리로 옮길 때(설계 §13-7) 원본에 로그 한 줄을 넣고 이 억제를 지운다.
[assembly: SuppressMessage(
    "HitPanConstitution",
    "HP0015",
    Scope = "member",
    Target = "~M:HitPan.Watchdog.DbConfReader.Parse(System.String)~System.Collections.Generic.Dictionary{System.String,System.String}",
    Justification = "워치독 원본 링크 파일 — 수정 0(G-43). 설계 §13-2·§13-7.")]
