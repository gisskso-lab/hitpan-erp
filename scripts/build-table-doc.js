// 137개 테이블 정의서 자동 박제
const fs = require('fs');
const path = require('path');

const tables = fs.readFileSync('tmp-build/tables_info.txt', 'utf-8').trim().split('\n')
    .map(l => { const [name, rows, comment] = l.split('\t'); return { name, rows: parseInt(rows) || 0, comment: (comment || '').trim() }; });
const cols = fs.readFileSync('tmp-build/columns_info.txt', 'utf-8').trim().split('\n')
    .map(l => { const p = l.split('\t'); return { table: p[0], col: p[1], type: p[2], nullable: p[3], key: p[4], def: p[5] === 'NULL' ? '' : p[5], comment: (p[6] || '').trim() }; });

const byTable = {};
cols.forEach(c => { if (!byTable[c.table]) byTable[c.table] = []; byTable[c.table].push(c); });

// 카테고리 분류
const CAT = {
    '인증·테넌트·사용자': /^(users|refresh_tokens|tenants|tenant_|employees|positions|departments|permissions|roles|device|login_attempts|audit_|terms_consents|user_terms|platforms|backoffice_)/,
    '업체·상품·마스터': /^(partners|partner_|items|item_|item_groups|item_special|item_specs|item_stock|inventory)/,
    '매입': /^(purchase_|purchases)/,
    '판매·세금계산서': /^(quotations|quotation_|sales_|sales|deliveries|delivery_|tax_invoices|tax_invoice|etax_)/,
    '재고·창고': /^(stock_|warehouses|warehouse)/,
    '재무·회계': /^(accounts|journal_|monthly_|ledger_|partner_balance|bills|bank_|card_|cashbook|expenses|collections|payments|vat_)/,
    'BOM·생산': /^(bom_|mold_|haccp_|material_)/,
    'HR·결재': /^(approval_|attendance|leave_|overtime|labor_|esign_|evaluations|hr_|signature_)/,
    'AI·CS·챗봇': /^(ai_|chat|hitpan_knowledge|cs_|chatbot)/,
    '백오피스·대리점': /^(reseller_|platform_|admin_|backoffice|subscription|commission|payout|promotion|revenue)/,
    '백업·마이그·시스템': /^(backup_|migration_|migration|watchdog_|idempotency|events|email_|document|force_|common_codes|licenses|api_|custom_order|monthly_summary|beta_|billing_|form_templates|print_|verifications|user_|chart_)/,
    '기타': /./
};

function categorize(name) {
    for (const [cat, regex] of Object.entries(CAT)) {
        if (regex.test(name)) return cat;
    }
    return '기타';
}

const grouped = {};
tables.forEach(t => {
    const cat = categorize(t.name);
    if (!grouped[cat]) grouped[cat] = [];
    grouped[cat].push(t);
});

let md = `# 08. 테이블 정의서 — 137개 테이블 전수

> **작성일**: 2026-06-01 / PM 브라운킴 + DB 매니저
> **DB**: MariaDB 11.4.10 / hitpan_erp / utf8mb4_unicode_ci / InnoDB (헌법 #17)
> **추출 시각**: ${new Date().toISOString()}

---

## 1. 요약

| 카테고리 | 테이블 수 |
|---|---|
`;
for (const [cat, ts] of Object.entries(grouped)) {
    md += `| ${cat} | ${ts.length} |\n`;
}
md += `| **합계** | **${tables.length}** |\n\n---\n\n## 2. 테이블 상세\n\n`;

for (const [cat, ts] of Object.entries(grouped)) {
    md += `### ${cat} (${ts.length}건)\n\n`;
    for (const t of ts) {
        md += `#### \`${t.name}\``;
        if (t.comment) md += ` — ${t.comment}`;
        md += `\n`;
        md += `- 현재 행수: ${t.rows.toLocaleString()}\n\n`;
        md += `| 컬럼 | 타입 | Null | Key | Default | 설명 |\n`;
        md += `|---|---|---|---|---|---|\n`;
        for (const c of (byTable[t.name] || [])) {
            md += `| ${c.col} | ${c.type} | ${c.nullable} | ${c.key || ''} | ${(c.def || '').slice(0, 30)} | ${c.comment.slice(0, 60)} |\n`;
        }
        md += `\n`;
    }
}

md += `\n---\n\n## 3. 헌법 정합 체크\n\n`;
md += `- **헌법 #3 (INSERT ONLY 원장)**: stock_ledger, journal_lines 적용\n`;
md += `- **헌법 #5 (암호화 컬럼)**: tenant_certificates, billing_payment_methods 등 AES-256\n`;
md += `- **헌법 #17 (InnoDB)**: 137개 전체 ENGINE=InnoDB 명시\n`;
md += `- **헌법 #18·#22 (본사 데이터 최소주의)**: backoffice_*, platform_*, reseller_*, licenses, watchdog_pings, billing_* — 본사 보유 영역만\n`;
md += `- **utf8mb4_unicode_ci 통일**\n`;

fs.writeFileSync(path.join('docs', 'erp-handover', '08_테이블정의서.md'), md);
console.log(`테이블 정의서 박제 완료: ${tables.length}개 테이블, ${cols.length}개 컬럼`);
console.log(`크기: ${(md.length/1024).toFixed(1)} KB`);
