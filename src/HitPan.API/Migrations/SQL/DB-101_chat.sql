-- ═══════════════════════════════════════════════════════════════
-- DB-101 : 사내 메신저 (그룹웨어 단계9)
-- 작성: 2026-08-13 · 근거 20260813작1 작업지시서
-- ═══════════════════════════════════════════════════════════════
--
-- 🔴 사장님 지시 (2026-08-13, 원문)
--
--   "단체대화, 부서별 대화, 1:1대화 모두 가능해야함"
--   "메신저에서 각 그룹웨어 문서들 연결 가능해야 함"
--   "메신저 전송 후 상대방이 메신저 읽을시, "읽음" 처리 되야 함"
--   "단체·부서방은 몇 명이 읽었는지 = 00"
--
--   ⇒ 방 3종 · 문서 링크 · 읽음.
--
-- 🔴 범위를 사장님이 자르셨다 — 되돌리지 말 것
--
--   "그룹웨어 문서 전송은 그룹웨어에 이미 있는 기능들 연결 정도만 해도 됨"
--   "메신저 채팅창에서 연차신청서 생성해서 만들고 결재까지 기능을 넣을 필요까진 없음.
--    연결까지만 해도 충분함"
--   "있는 기능 연결해서 사용하는게 훨씬 효율적임"
--
--   ⇒ 메신저는 **길만 놓는다.** 문서 생성·결재 상신·승인을 메신저가 하지 않는다.
--     왜 이게 맞나: 연차신청 화면은 이미 잔여연차 확인·결재선·이중차감 방지를 한다
--     (6/23 봉합). 메신저에 또 만들면 규칙이 두 벌이 되고, 두 벌은 반드시 갈라진다.
--
-- 🔴 결재 결과는 신청자에게 "메시지"로 돌아온다 (알림 아님)
--
--   "그룹웨어 문서 승인 혹은 반려시, 최초 발신인(신청자)에게 메시지 보내야됨"
--   "반려되었습니다. 혹은 승인되었습니다."
--   "결재봇, 메시지봇 공유해도 될듯. 인스타나 페이스북 처럼.
--    다만 메시지인지, 결재안내인지는 안내"
--
--   ⇒ 한 곳에 모이고, msg_kind 로 [결재]/[메시지] 를 가른다.
--     알림(종)과 메시지를 둘 다 보내지 않는다 — 같은 소식이 두 번 오면 안 된다.
--
-- 🔴 파일 전송 — 최소한으로
--
--   "파일전송 기능도 추가해. 엑셀, 캡쳐파일, 디자인파일 등"
--   "통신이 안정적이지 않다면 안되는걸로 생각하자" / "20메가"
--   "히트판 ERP 데이터양이 많으면 과부화가 올 수 있어. 파일전송은 최소한으로"
--
--   ⇒ 20MB · 디스크 폴더 보관(DB엔 경로만) · 조각 업로드 안 만든다.
--     100MB 를 사장님이 **알고 접으셨다.** 조각 업로드로 억지로 되게 만들면
--     되는 것처럼 보이다가 고객 사무실 인터넷이 느린 날 90% 에서 터진다.
--     20MB 는 한 번에 가거나 안 가거나 둘 중 하나다 — 반쯤 간 상태가 없다.
--
--   🔴 파일이 ERP 를 넘어뜨리지 못하게 3중 한도:
--     파일 누적 → 백업 느려짐 → 업데이트 전 백업 실패 → 업데이트 차단
--     메신저 파일 때문에 ERP 업데이트가 멈추는 것이 최악이다.
--
-- 헌법: #1(추가만) #2(tenant_id JWT) #17(InnoDB) #36(clean DDL 동반)
-- ═══════════════════════════════════════════════════════════════

-- ───────────────────────────────────────────────────────────────
-- 1. chat_rooms — 방
-- ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `chat_rooms` (
  `room_id`     varchar(36)  NOT NULL COMMENT '방 PK',
  `tenant_id`   varchar(36)  NOT NULL COMMENT '테넌트 (헌법 #2 — JWT 에서만)',
  `room_type`   varchar(10)  NOT NULL COMMENT 'direct(1:1) · dept(부서) · group(단체)',
  `room_name`   varchar(100) DEFAULT NULL COMMENT '단체방만 입력. 1:1 은 NULL(상대 이름을 그때 붙인다)',
  `dept_id`     varchar(36)  DEFAULT NULL COMMENT '부서방만. 부서는 고객사가 설정할 일이라 0건이어도 정상',
  `direct_key`  varchar(80)  DEFAULT NULL COMMENT '🔴 1:1 중복 방지 — 사원ID 2개를 정렬해 결합. A→B 와 B→A 가 같은 방이어야 한다',
  `created_by`  varchar(36)  NOT NULL COMMENT '만든 사원',
  `created_at`  datetime(6)  NOT NULL DEFAULT current_timestamp(6),
  `updated_at`  datetime(6)  NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`room_id`),
  UNIQUE KEY `uq_chat_direct` (`tenant_id`,`direct_key`),
  KEY `idx_chat_rooms_tenant_type` (`tenant_id`,`room_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='사내 메신저 방 — 1:1·부서·단체 3종';

-- ───────────────────────────────────────────────────────────────
-- 2. chat_room_members — 참여자
--    🔴 계정 있는 사원만 (사장님 2026-08-12:
--       "메신저는 계정이 있는 사원에게만 권한을. 물리적으로 그렇게 될 수밖에 없으니")
--       ⇒ employees.user_id IS NOT NULL 인 사람만 넣는다. 서비스에서 거른다.
-- ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `chat_room_members` (
  `member_id`     varchar(36) NOT NULL COMMENT 'PK',
  `tenant_id`     varchar(36) NOT NULL,
  `room_id`       varchar(36) NOT NULL,
  `employee_id`   varchar(36) NOT NULL COMMENT '🔴 사원 ID (user_id 아님 — 알림·결재와 같은 축)',
  `last_read_at`  datetime(6) DEFAULT NULL COMMENT '🔴 읽음의 유일한 근거. 여기까지 읽었다',
  `joined_at`     datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `left_at`       datetime(6) DEFAULT NULL COMMENT 'NULL = 참여 중. 나가도 줄을 지우지 않는다(나가기 전 대화는 계속 보인다)',
  PRIMARY KEY (`member_id`),
  UNIQUE KEY `uq_chat_member` (`room_id`,`employee_id`),
  KEY `idx_chat_member_my` (`tenant_id`,`employee_id`,`left_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='메신저 방 참여자 — last_read_at 하나로 읽음을 판정한다';

-- ───────────────────────────────────────────────────────────────
-- 3. chat_messages — 메시지
--    🔴 문서 연결은 참조만. 본문을 복사하지 않는다.
--       원본이 바뀌면 사본은 틀린 내용이 되고,
--       급여·경비 금액이 대화방에 복사본으로 남으면 권한을 우회한다.
-- ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `chat_messages` (
  `message_id`  varchar(36)   NOT NULL COMMENT 'PK',
  `tenant_id`   varchar(36)   NOT NULL,
  `room_id`     varchar(36)   NOT NULL,
  `sender_id`   varchar(36)   NOT NULL COMMENT '보낸 사원. 🔴 결재 안내도 결재한 사람 ID(봇 이름으로 가리지 않는다)',
  `msg_kind`    varchar(10)   NOT NULL DEFAULT 'text' COMMENT '🔴 딱지의 근거 — text(메시지) · approval(결재) · file(파일)',
  `body`        varchar(2000) NOT NULL COMMENT '본문',
  `ref_type`    varchar(20)   DEFAULT NULL COMMENT '연결 문서 종류 — approval·leave·expense·payroll·contract·report',
  `ref_id`      varchar(36)   DEFAULT NULL COMMENT '원본 문서 ID. 누르면 그 화면으로 이동',
  `ref_title`   varchar(200)  DEFAULT NULL COMMENT '🔴 미리보기용 제목만. 내용이 아니다',
  `sent_at`     datetime(6)   NOT NULL DEFAULT current_timestamp(6),
  `deleted_at`  datetime(6)   DEFAULT NULL COMMENT '🔴 숨김만. 원문은 남는다 — 주고받은 기록을 한쪽이 없앨 수 없다(헌법 #3 정신)',
  PRIMARY KEY (`message_id`),
  KEY `idx_chat_msg_room` (`tenant_id`,`room_id`,`sent_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='메신저 메시지 — 문서는 참조만(제목만 저장), 삭제는 숨김만';

-- ───────────────────────────────────────────────────────────────
-- 4. chat_files — 보낸 파일
--    🔴 파일은 디스크에, DB 에는 경로만.
--       DB 안에 통째로 넣으면 DB 가 수십 GB 로 불어 백업·복구·업데이트가 모두 느려진다.
-- ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `chat_files` (
  `file_id`        varchar(36)  NOT NULL COMMENT 'PK. 🔴 저장 파일명도 이것 — 원래 이름을 쓰면 ..\\..\\ 경로 조작이 들어온다',
  `tenant_id`      varchar(36)  NOT NULL,
  `room_id`        varchar(36)  NOT NULL,
  `message_id`     varchar(36)  NOT NULL,
  `original_name`  varchar(255) NOT NULL COMMENT '🔴 보여주기용만. 저장 경로에 쓰지 않는다',
  `stored_path`    varchar(500) NOT NULL COMMENT 'chat-files/{tenant_id}/{yyyyMM}/{file_id}.{ext}',
  `content_type`   varchar(100) NOT NULL DEFAULT 'application/octet-stream',
  `file_size`      bigint(20)   NOT NULL COMMENT '🔴 20MB 한도 — 사장님 "20메가"',
  `uploaded_by`    varchar(36)  NOT NULL COMMENT '올린 사원',
  `uploaded_at`    datetime(6)  NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`file_id`),
  KEY `idx_chat_files_room` (`tenant_id`,`room_id`),
  KEY `idx_chat_files_msg` (`message_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='메신저 파일 — 실제 파일은 디스크, 여기엔 경로만';

-- ───────────────────────────────────────────────────────────────
-- 5. 파일 한도 설정값
--    🔴 코드 상수로 두지 않는다 ([[feedback_semi_auto_principle]]):
--       "기준값은 코드상수 금지 → 설정테이블. 하드코딩→상수는 같은 실수"
--    ⇒ 고객사마다 디스크 사정이 다르므로 설정에서 바꿀 수 있어야 한다.
-- ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `chat_file_settings` (
  `tenant_id`          varchar(36) NOT NULL COMMENT 'PK — 테넌트당 한 줄',
  `max_file_mb`        int(11)     NOT NULL DEFAULT 20   COMMENT '파일 한 개 최대 (사장님 확정 20MB)',
  `max_room_mb`        int(11)     NOT NULL DEFAULT 500  COMMENT '방 하나 누적 최대',
  `max_tenant_mb`      int(11)     NOT NULL DEFAULT 5120 COMMENT '회사 전체 누적 최대 (5GB)',
  `created_at`         datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at`         datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),
  PRIMARY KEY (`tenant_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='메신저 파일 한도 — 파일이 ERP 를 넘어뜨리지 못하게. 업무 데이터는 이 한도와 무관';
