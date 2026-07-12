namespace HitPan.Domain.Enums;

public enum SubscriptionStatus
{
    Active = 1,
    Paused = 2,
    Cancelled = 3,
    // W3-3 (작업지시서 20260707작2): 출하 DDL subscriptions.status DEFAULT 'trial' 과 정합 —
    //   enum 에 멤버가 없어 trial 행을 읽는 순간 materialize 폭발하는 휴면 지뢰였다. 가산만(기존 값 무변경).
    Trial = 4
}
