namespace HitPan.Application.Services;

/// <summary>
/// 복원 때 표 하나가 어느 쪽인가. <b>기본값(0) = 분류 안 됨</b> — 분류 안 된 표가 하나라도 있으면 복원은 멈춘다.
/// </summary>
public enum RestoreTableClass
{
    /// <summary>두 목록 어디에도 없음(또는 둘 다에 있음). 복원 멈춤 · 게이트 G13 빨강.</summary>
    Unclassified = 0,

    /// <summary>「지금 값」 — 복원이 손대지 않는다(계정 · 회사정보 · 로그 · 구조 · 설정 · 수신 캐시 · 1회용 표).</summary>
    KeepCurrent = 1,

    /// <summary>「백업 값」 — 복원이 백업 시점 값으로 바꾼다(업무 자료 · 장부 포함 = #3 예외 ①).</summary>
    FromBackup = 2,
}

/// <summary>
/// 🔴 W-G1-52 갈래 A-1 — <b>복원 표 분류 상수</b>(설계 <c>docs/설계/erp/W-G1-52_최소복구_설계_20261127.md</c> §5 · §15-4 · 작업지시서 K3 · K12).
/// </summary>
/// <remarks>
/// <para><b>두 목록 · 기본값 0</b>: 「지금 값」 · 「백업 값」 둘 다 이름을 적는다. 「나머지 전부 = 백업 값」 같은 기본값은 없다.
/// 어느 목록에도 없는 표 = <see cref="RestoreTableClass.Unclassified"/> → 복원 걸음 ① 에서 멈춤(A-2 이후) · CI 게이트 G13 빨강.
/// 새 표를 만드는 사람은 <b>반드시 이 파일에 한 줄</b>을 더한다 — 안 더하면 게이트가 빨개진다(그게 이 상수의 존재 이유).</para>
/// <para><b>초기화 보존표(<c>DataResetService.PreservedTables</c>)와 따로 둔다</b> — 출발점은 같지만(6/23 분류) 초기화는 손 0(설계 §5).
/// 다른 점: ③-예외 로그 3(<c>stock_adjust_logs</c> · <c>haccp_logs</c> · <c>mold_production_log</c>)은 장부와 함께 「백업 값」(K3) ·
/// <c>idempotency_keys</c> 도 「백업 값」(K12 · R8 ① — 지금 값이면 되돌려진 요청을 「이미 처리함」 으로 답해 같은 키 거래가 조용히 사라짐).</para>
/// <para>이름은 소문자 · 비교는 대소문자 무시(Windows MariaDB <c>lower_case_table_names=1</c>).
/// 배열로 적는 까닭 = 같은 이름 두 번(한 목록 안 · 두 목록 사이)을 시험이 셀 수 있게(HashSet 초기화는 중복을 조용히 삼킨다).</para>
/// <para>이 묶음(A-1)은 <b>상수 · 판정 함수만</b>이다 — 부르는 제품 코드 0. 복원 걸음 ①~⑤ 몸은 A-2 이후.</para>
/// </remarks>
public static class RestoreTableClassification
{
    /// <summary>같은 DB 안 임시 표 접두(설계 §15-2 · K1). 이 접두로 시작하는 실제 표는 분류하지 않는다(G13 ⓑ).</summary>
    public const string StagingPrefix = "rs_new__";

    /// <summary>분류 표 이름 최대 길이 = 64(MariaDB 이름 한도) − 접두 8(G13 ⓖ · R13).</summary>
    public const int MaxClassifiedNameLength = 56;

    /// <summary>「지금 값」 — 복원이 손대지 않는 표.</summary>
    public static readonly string[] KeepCurrentNames =
    [
        // ① 계정 — 대표 로그인 칸 전부 · 퇴사 · 비번 · 권한 · 폐기 토큰 · 약관 동의(설계 §5 ① · ⓐ · S-5)
        "users", "user_permissions", "user_terms_consent", "refresh_tokens", "user_sessions",

        // ② 회사정보 · 수신 캐시(#37 — local_subscription · tenants 손 0) · 분실 기기 차단 유지(설계 §5 ②)
        "tenants", "local_company", "local_subscription", "subscriptions",
        "tenant_settings", "tenant_certificates", "tenant_etax_settings", "tenant_devices",

        // ③ 로그 — 복원으로 지워지는 기간의 기록도 잃지 않음(ⓒ · 설계 §5 ③)
        "audit_logs", "audit_trail", "login_attempts", "force_edit_logs",
        "device_login_logs", "security_alerts", "ai_usage_logs",

        // ④ 구조 · 설정 — backup_history · restore_history 가 남아 S-7 해소(설계 §5 ④ · idempotency_keys 는 「백업 값」 으로 옮김 K12)
        "__efmigrationshistory",
        "backup_settings", "backup_history", "restore_history",
        "email_settings",
        "billing_settings", "billing_subscriptions", "billing_invoices",
        "billing_payment_methods", "billing_payment_attempts",
        "common_codes", "form_templates",
        "approval_settings", "workflow_settings",
        "sync_tokens",

        // ⑤ 지금 값 추가 21(설계 §15-4)
        //   마이그 기록 · 업데이트 사슬(#43 · K5 「같은 판」 판정 근거)
        "schema_migrations",
        "local_update_consents", "local_update_status", "local_update_apply_status", "local_update_attempts",
        //   기기 · 동의 — 1회용 기기 등록 표 · 폐기된 정책 표(읽지 않음) · 외부 AI 내보내기 동의(INSERT ONLY)
        "device_register_tokens", "device_slot_policy_settings", "ai_export_consents",
        //   본사와 오가는 표(#35 · #37) — CS 쪽지 5 · Pull 복사본 2
        "cs_requests", "cs_outbox", "cs_replies", "cs_rating_outbox", "cs_forbidden_rejects",
        "tenant_devices_snapshot", "tenant_employees_snapshot",
        //   설정 · 제품 자산(print_common_settings · labor_policy_settings · hitpan_knowledge = 설계 「확인 필요」 → 추천대로 지금 값)
        "fax_settings", "geocoding_settings", "chat_file_settings",
        "print_common_settings", "labor_policy_settings", "hitpan_knowledge",

        // ⑥ 옛 마이그만 만든 표 — 출하 DDL(installer/hitpan_db_clean.sql)엔 0 · 옛 고객 DB 에 남아 있을 수 있음
        //   (DB-02 · DB-09 · DB-38 · DB-70 · DB-71 · DB-80 · 본사 · 대리점 · 워치독 옛 자리 #18 · #22) → 복원이 손대지 않음.
        //   목록에 없으면 옛 고객 PC 에서 걸음 ① 이 늘 멈춘다 → 지금 값으로 둔다(PM 판단 칸 · 개발명세서 §5).
        "platforms", "resellers", "reseller_applications", "reseller_commission_policies",
        "reseller_promotions", "reseller_revenues", "reseller_payouts",
        // 고침 1 Y6 ([3-V] P3-5) — 옛 줄: "licenses", "watchdog_pings", "watchdog_emergencies",
        //   licenses 는 「옛 자리」 가 아니다 — WatchdogBearerMiddleware 가 지금도 토큰 해시를 읽는 살아 있는 인증 표.
        //   백업 값으로 옮기면 폐기한 토큰이 되살아남 → 지금 값 고정(G13-S9 가 지킴 · 나중 정리 때 옮기지 말 것).
        "licenses",
        "watchdog_pings", "watchdog_emergencies",
        "tenants_rebuilt",   // DB-09 가 만들고 곧바로 tenants 로 이름 바꿈 — 도중에 죽었을 때만 남는 자리
        "tenants_old",       // 고침 1 Y2 — DB-09 :32 RENAME 으로 생기고 :34 에서 지움 — 그 사이 죽었을 때만 남는 자리(tenants_rebuilt 와 같은 쪽 · [4] P2-②)
    ];

    /// <summary>「백업 값」 — 복원이 백업 시점 값으로 바꾸는 표(업무 자료 · 장부 포함).</summary>
    public static readonly string[] FromBackupNames =
    [
        // 회계 · 장부(#3 예외 ① — 교체 걸음 한 곳에서만 · 설계 §2)
        "accounts", "journal_entries", "journal_lines", "ledger_balance_snapshot",
        "monthly_closing", "monthly_summary", "monthly_summary_sources",
        "cashbook", "bank_transactions", "bills", "card_payments", "card_payment_lines",
        "collections", "payments", "expenses",

        // 거래처
        "partners", "partner_contacts", "partner_balance", "partner_legacy_balances", "partner_special_prices",

        // 품목 · 재고 · BOM · 생산
        "items", "item_groups", "item_specs", "item_special_prices", "item_stock",
        "inventory_lots", "warehouses", "stock_ledger", "stock_alerts",
        "material_price_history", "bom_headers", "bom_items", "bom_cost_cache",
        "work_in_process", "mold_assets", "custom_order_specs",
        //   ③-예외 업무성 로그 3 — 장부 · 재고와 함께(K3 · HACCP 보관 의무 = 법무팀장 확인 열림 · 막는 조건 아님)
        "stock_adjust_logs", "haccp_logs", "mold_production_log",

        // 매입
        "purchase_orders", "purchase_order_items", "purchase_receipts", "purchase_receipt_items",
        "purchase_returns", "purchase_return_items",

        // 판매 · 계산서
        "quotations", "quotation_items", "sales_orders", "sales_order_items",
        "sales_deliveries", "sales_delivery_items", "sales_returns", "sales_return_items",
        "tax_invoices", "tax_invoice_items",
        "etax_send_history",   // 외래키 → tax_invoices 라 백업 값 강제(설계 §15-4 · 법무팀장 확인 = R9 와 함께)
        "delivery_tracking", "service_tickets", "status_history", "document_conversions",
        "email_send_history", "email_attachment_history", "fax_send_history",

        // 인사 · 급여 · 근태
        "employees", "departments", "positions", "attendance", "overtime",
        "leave_requests", "annual_leave_grants", "employee_leave_of_absence",
        "labor_contracts", "evaluations", "hr_reports", "hr_expense_requests",
        "payroll_slips", "payroll_slip_lines", "severance_payments", "resignation_letters",

        // 그룹웨어 — 결재 · 메신저 · 일정 · 전자서명 · AI 대화
        "approval_documents", "approval_doc_lines", "approval_history", "approval_lines", "approval_line_steps",
        "chat_rooms", "chat_room_members", "chat_messages", "chat_files",
        "events", "schedules", "esign_records", "ai_conversations",

        // 자료이관 — 업무 자료와 짝(설계 「확인 필요」 → 추천대로 백업 값)
        "migration_jobs", "migration_checkpoints", "migration_errors",
        "legacy_unposted_documents", "legacy_unposted_document_lines",

        // 요청 멱등 키 — 되돌려진 거래와 짝(K12 · R8 ①)
        "idempotency_keys",
    ];

    /// <summary>
    /// 트리거가 걸린 것으로 아는 표(G13 ⓔ). 여기 없는 표에 트리거가 생기면 빨강 → 교체 걸음(지우고 붓기) 설계를 다시 본다.
    /// 출하 DDL: <c>partner_special_prices</c>(2) · <c>tax_invoices</c>(1) / 옛 마이그: <c>tenant_devices</c>(DB-89) · 본사 옛 표 6(DB-02).
    /// </summary>
    public static readonly string[] KnownTriggerTableNames =
    [
        "partner_special_prices", "tax_invoices",
        "tenant_devices",
        "platforms", "resellers", "reseller_commission_policies",
        "reseller_promotions", "reseller_revenues", "reseller_payouts",
    ];

    private static readonly HashSet<string> KeepSet = new(KeepCurrentNames, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BackupSet = new(FromBackupNames, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TriggerSet = new(KnownTriggerTableNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>「지금 값」 이름 집합(대소문자 무시).</summary>
    public static IReadOnlySet<string> KeepCurrent => KeepSet;

    /// <summary>「백업 값」 이름 집합(대소문자 무시).</summary>
    public static IReadOnlySet<string> FromBackup => BackupSet;

    /// <summary>트리거가 걸린 것으로 아는 표 집합(대소문자 무시).</summary>
    public static IReadOnlySet<string> KnownTriggerTables => TriggerSet;

    /// <summary>
    /// 표 하나를 판정한다. 두 목록 중 <b>정확히 하나</b>에 있을 때만 그 분류 · 아니면 <see cref="RestoreTableClass.Unclassified"/>(멈춤 쪽).
    /// </summary>
    public static RestoreTableClass Classify(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return RestoreTableClass.Unclassified;
        var keep = KeepSet.Contains(tableName);
        var backup = BackupSet.Contains(tableName);
        if (keep == backup) return RestoreTableClass.Unclassified;   // 둘 다 없음 · 둘 다 있음 = 멈춤
        return keep ? RestoreTableClass.KeepCurrent : RestoreTableClass.FromBackup;
    }

    /// <summary>
    /// 실제 표 목록 중 분류 안 된 것(이름순). 비어 있어야 복원이 걸음 ① 을 넘는다(A-2 이후 부름 · 지금은 게이트 G13 이 부름).
    /// 임시 접두(<see cref="StagingPrefix"/>) 표도 분류 안 됨으로 돌려준다 — 남은 임시 표가 있으면 멈춤 쪽.
    /// </summary>
    public static IReadOnlyList<string> FindUnclassified(IEnumerable<string> actualTableNames)
    {
        ArgumentNullException.ThrowIfNull(actualTableNames);
        return actualTableNames
            .Where(t => Classify(t) == RestoreTableClass.Unclassified)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    }
}
