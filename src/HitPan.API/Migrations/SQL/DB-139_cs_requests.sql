-- =============================================================
-- DB-139: cs_requests 신설 — 고객이 쓴 CS 쪽지 본체 (작14 묶음 B-2)
-- =============================================================
-- 근거: 작업지시서 docs/운영기록/20261008작14_백오피스_메뉴재구성_CS쪽지_AI자동화_작업지시서.md §3 B-2
--       설계     docs/설계/백오피스/20261007_개발매니저피드백_CS_ERP연동_4인.md §3-1 (칸 정의 그대로)
--       결-5(사장님 10/8): 자유 본문 ㉯ 1차 포함 ⇒ body 칸을 만든다 (금지필드 게이트 같은 묶음)
--       결-10: 메뉴 = 사이드바 하단 「CS 요청」 단독
--
-- 무엇에 쓰나
--   자식계정(직원)이 ERP 화면에서 쓴 쪽지의 로컬 원본. 전송 흐름(교정③ 부모계정 파이프라인):
--   로컬 INSERT → cs_outbox(DB-140) 큐 → 송신 모듈이 부모계정 신원으로 백오피스 전송.
--   🔴 created_by(자식계정)는 **로컬 전용 칸** — 송신 payload 화이트리스트 밖(#22 최소주의).
--   🔴 body 는 varchar(2000) — chat_messages.body 와 같은 길이 기준(집 안에 기준 하나).
--       TEXT 금지: 길면 금지필드 검사가 느려지고 행 크기 관리가 어렵다(설계 §3-1).
--   상태: 접수/전송중/전송완료/거부/답변도착/종결 + 「고객 답 대기」(결-1 — 백오피스 축이나 어휘 예약).
--
-- 🚫 건드리지 않는 것: 다른 표 전부 · 기존 행 수정 0(#1) · 삭제 0
-- 대상 DB: hitpan_erp (고객 PC 로컬) · 출하 DDL installer/hitpan_db_clean.sql 같은 커밋 편입(#36)
-- #13: 신설 표 — 인접 chat_messages.body varchar(2000) 실측(2026-10-07 설계 §3-1 각주).
-- #17: ENGINE=InnoDB 명시. 멱등: CREATE TABLE IF NOT EXISTS.
-- =============================================================

CREATE TABLE IF NOT EXISTS cs_requests (
    cs_request_id  varchar(36)   NOT NULL COMMENT 'ERP 가 발급(UUID) — 멱등키의 몸통',
    tenant_id      varchar(36)   NOT NULL COMMENT 'JWT 유래만 (#2)',
    created_by     varchar(36)   NOT NULL COMMENT '쓴 사원(자식계정) — 로컬 전용 · 송신 payload 밖(#22)',
    category       varchar(20)   NOT NULL COMMENT '유형(7모양 콤보 고정 목록 — 화이트리스트 밖 거부)',
    sub_tag        varchar(30)   NOT NULL COMMENT '세부태그 — 사전정의 목록만',
    body           varchar(2000) NULL     COMMENT '자유 본문(결-5 ㉯) — 저장 전 금지필드 검사(문①)',
    screen_code    varchar(40)   NULL     COMMENT '어느 화면에서 났나(자동)',
    erp_version    varchar(20)   NOT NULL COMMENT '어느 판인가(자동)',
    status         varchar(10)   NOT NULL DEFAULT '접수' COMMENT '접수/전송중/전송완료/거부/답변도착/종결',
    created_at     datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (cs_request_id),
    KEY idx_cs_req_tenant (tenant_id, created_at),
    KEY idx_cs_req_status (tenant_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='CS 쪽지 로컬 원본 — 자식계정 작성 · 부모계정 파이프로 송신(작14 B-2)';
