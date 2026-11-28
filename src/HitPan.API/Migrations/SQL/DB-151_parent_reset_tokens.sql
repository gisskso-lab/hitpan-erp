-- ════════════════════════════════════════════════════════════════════
-- DB-151 · parent_reset_tokens — 대표(부모) 비밀번호 되찾기 1회용 표
--   W-G1-52 갈래 C · 2026-11-28 · 번호는 머지 때 PM 이 정리(갈래 A 와 따로 받음 · 상위 지시서 §4)
-- ════════════════════════════════════════════════════════════════════
--
-- 근거
--   덧 지시서  docs/운영기록/20261128작_W-G1-52_갈래AC_착수조건덧_작업지시서.md §2 (CTO 전결 §297)
--   상위       docs/운영기록/20261127작_W-G1-52_최소복구_작업지시서.md §4 C 행 · §7-1 K7
--   설계       docs/설계/erp/W-G1-52_최소복구_설계_20261127.md §8-4 2 · §12 G10
--
-- 🔴 무엇
--   오프라인 명령 `HitPan.API.exe parent-reset`(윈도우 관리자 승인 뒤)이 1행을 넣고,
--   재설정 API 가 조건 갱신(used_at IS NULL AND expires_at > 지금 · 줄 수 1)으로 한 번만 소비한다(K7 · G10 동시 2요청).
--   표 값은 저장하지 않는다 — SHA-256 지문(소문자 16진 64자)만(#40 · 비번 평문 · 표 값을 파일 · 로그에 안 남김).
--   🔴 복원 분류 = 「지금 값」(설계 §8-4 5 · 복원이 이 표를 되살리지 않음 — 갈래 A 분류 목록에 넣을 것).
--   본사 전송 0(#18 · #22).
--
-- 🔴 #13 DESCRIBE (정적 — 이 PC DB 접속 0)
--   새 표라 기존 칸 없음. 짝 칸 = installer/hitpan_db_clean.sql `users`(user_id varchar(36) · tenant_id varchar(36) ·
--   utf8mb4_unicode_ci) — 같은 꼴 · 같은 정렬(조인 1267 길 0). 외래키 0(users 쪽 FK 관례 0 · 출하 DDL :4380 머리 주석).
-- 🔴 #17 ENGINE=InnoDB · #36 출하 DDL 같은 모양(G-W13-2 모양 대조) · 다시 돌아도 같음(IF NOT EXISTS · G-W13-1 신규설치본 위 실행).
CREATE TABLE IF NOT EXISTS `parent_reset_tokens` (
  `token_id` varchar(36) NOT NULL,
  `tenant_id` varchar(36) NOT NULL,
  `user_id` varchar(36) NOT NULL COMMENT '대표(is_parent=1) user_id — 만들 때 이 DB 의 대표 행이 정확히 1개일 때만',
  `token_hash` char(64) NOT NULL COMMENT '1회용 표의 SHA-256 소문자 16진 — 표 값은 저장 안 함 (DB-151)',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `expires_at` datetime(6) NOT NULL COMMENT '만든 때 + 10분 (UTC)',
  `used_at` datetime(6) DEFAULT NULL COMMENT '쓴 때 (UTC) — NULL 일 때만 한 번 소비',
  `notified_at` datetime(6) DEFAULT NULL COMMENT '대표 다음 로그인 때 「다시 정해졌습니다」 를 알린 때 (UTC) — NULL = 아직 안 알림 (DB-151 · 설계 R16 ⑤)',
  PRIMARY KEY (`token_id`),
  UNIQUE KEY `uq_parent_reset_token_hash` (`token_hash`),
  KEY `idx_parent_reset_user` (`user_id`),
  KEY `idx_parent_reset_expires` (`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
