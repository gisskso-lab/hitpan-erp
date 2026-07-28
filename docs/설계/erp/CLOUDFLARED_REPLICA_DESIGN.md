# Cloudflare Tunnel Replica 다중 이중화 설계 (Phase 1)

> 40h 하부르타 라운드 2 합의 결과 박제. 헌법 #27 SLA 99.99% 달성 핵심.
> **본 문서는 설계만**. 실제 Cloudflare 대시보드 조작은 헌법 #29 정합 사장님 결재 후만.

---

## 0. 배경

### 사고 기록
- **2022-06-21 Cloudflare 19개 데이터센터 다운** (~1.5h) — 50% 요청 영향
- **2017-02-28 AWS US-EAST-1 다운** (~4h) — multi-region 미적용 서비스 전멸
- **2026-05-15 본 시스템 사고** — Windows Update + TunnelSecret 회전 → 6h 다운

### 핵심 합의 (라운드 2)
| 시안 | 평가 |
|---|---|
| 3중 (CF + Tailscale + Headscale) | 운영 부담 폭발 — 기각 |
| CF 단일 + Tailscale | 본사 → 고객 양방향 = 헌법 #22 위반 — 기각 |
| **CF 단일 + replica + 워치독** | ✅ Phase 1 채택 |
| Headscale 비상 대기 | ✅ Phase 1 보유, Phase 2 활성화 |

---

## 1. Cloudflare Tunnel Replica 구조

### Cloudflare 공식 문서 (2024)
- 동일 tunnel ID에 최대 **25 replica** 동시 등록 가능
- 각 replica = **4 connection** × **2 데이터센터** = 자동 부하 분산
- replica 1개 다운 시 다른 replica가 즉시 인계

### 본 시스템 채택
| Phase | replica 수 | 환경 |
|---|---|---|
| 베타 1주차 | **2** | 본사 1대 + 사용자 PC 1대 |
| 베타 4주차 | **2** | 안정성 검증 후 유지 |
| 정식 출시 | **2** | Phase 1 최종 |
| Phase 2 (조건부) | 3+ Headscale | 베타 실측 SLA 99.99% 미달 시만 |

### replica 배치 도식
```
                    [Cloudflare 글로벌 네트워크]
                      ↑                ↑
                  4 conn × 2 DC    4 conn × 2 DC
                      ↑                ↑
                  cloudflared       cloudflared
                  (사용자 PC)      (사용자 PC, 본사 PC 미사용 — 헌법 #22)
                      ↓                ↓
               HitPan.Web (5234)  HitPan.Web (5234)
               HitPan.API (5257)  HitPan.API (5257)
```

**중요**: replica 2개 모두 **고객 PC 안에서만 가능**. 본사 PC에 replica 두면 헌법 #22 위반 (본사가 고객 트래픽 본다).

→ Phase 1 현실: replica 1개만 운영. 워치독으로 보강.
→ Phase 2 검토: 고객이 2대 PC 운영 시 replica 분산 (대규모 도소매).

---

## 2. 워치독 통합 (WS-28-D 확장)

기존 WS-28-D는 cloudflared Service 1개 재설치. replica 도입 시 확장:

### WS-28-D-1 — Primary cloudflared 감시 (현 박제 완료)
- `Get-Service cloudflared` Status 검증
- 실패 시 자동 재설치

### WS-28-D-2 — Secondary cloudflared (Phase 2 신설)
- Service 이름: `cloudflared-secondary`
- 동일 tunnel ID, 동일 cred 파일, 동일 config.yml
- Primary 다운 시 Secondary가 4 connection 인계 (Cloudflare 자동)
- 워치독은 둘 다 1분 주기 감시

### 의사코드 (Phase 2 검토)
```csharp
public class WS28D_ReplicaSurveillance
{
    private static readonly string[] Replicas = { "cloudflared", "cloudflared-secondary" };

    public async Task<int> AliveReplicaCountAsync()
    {
        int alive = 0;
        foreach (var name in Replicas)
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status == ServiceControllerStatus.Running) alive++;
            }
            catch { /* 미설치 = 0 */ }
        }
        return alive;
    }
}
```

---

## 3. 본사 Headscale 비상 대기

### 목적
- Cloudflare 정책 변경 (무료 터널 폐지·요금제 변경 등) 대비
- Cloudflare 전체 장애 (2022-06-21급 사고) 대비

### 운영 원칙
- **평시**: 본사 클라우드 서버 1대에 Headscale Docker 컨테이너 비활성 상태
- **비상**: 본사 어드민이 활성화 → 워치독에 EXE 자동 업데이트 push → Tailscale WireGuard로 전환
- **헌법 #22 정합**: 본사 → 고객 단방향 차단 ACL (Tailscale ACL `accept: false`)
- **헌법 #30 정합**: 본사 Headscale 자체도 docker-compose 자가 회복

### 비상 활성화 시퀀스
1. 본사 어드민 결재 (사장님 1회 클릭)
2. Headscale Docker 컨테이너 기동
3. 베타 고객사에게 워치독 EXE 자동 업데이트 push (Velopack)
4. 워치독이 Tailscale 클라이언트 자동 설치 + 키 등록
5. cloudflared 중단, Tailscale 전환
6. 본사 ACL 검증 (단방향 확인)

**소요**: 30분 ~ 1시간 (긴급 패치).

---

## 4. Phase 1 → Phase 2 전환 조건

### Phase 2 발진 조건 (베타 실측 후 판단)
- 베타 4주차 종료 시 (2026-07-12)
- 본사 책임 영역 SLA 측정값 **< 99.99%**
- 사고 원인 = Cloudflare 단일 의존
- 사장님 결재 + 본부장 + 설계팀장 합의

### Phase 2 발진 시 작업
1. Headscale 비상 대기 → 정식 운영
2. 워치독 EXE 자동 업데이트 (Velopack delta)
3. 베타 고객사 매뉴얼 안내
4. 약관 v2.1 (Tailscale 도입 명시)

---

## 5. 비용 분석

| 항목 | Phase 1 (월) | Phase 2 추가 (월) |
|---|---|---|
| Cloudflare Tunnel | **무료** | — |
| Cloudflare DNS | **무료** | — |
| 본사 클라우드 (Headscale) | 5~10만원 (대기) | 5~10만원 (활성) |
| Tailscale | — | 사용 안 함 (Headscale 자체) |
| **합계** | **5~10만원** | **5~10만원 추가 (필요 시만)** |

10,000 고객사 운영 가능. 사업성 검증.

---

## 6. 검증 게이트 (베타 4주차 종료 시)

- [ ] SLA 99.99% 측정값 박제 (매월 1일 공시)
- [ ] Cloudflare 부분 장애 시뮬 PASS (특정 데이터센터 차단)
- [ ] cloudflared 자동 회복 (WS-28-C/D) 30회 PASS
- [ ] Headscale 비상 대기 환경 박제 (사장님 결재 후)
- [ ] 사장님 결재: Phase 2 발진 여부

---

## 7. 사장님 결재 필요 항목

| # | 시점 | 결재 |
|---|---|---|
| 1 | 즉시 | replica 2 → 1로 축소 (헌법 #22) |
| 2 | 베타 발진 (6/15) 전 | Cloudflare 대시보드 접근 권한 (PM 조작 0) |
| 3 | Phase 2 (필요 시) | Headscale 비상 활성화 |

---

**문서 끝.**
