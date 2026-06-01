// 09. 프로그램 목록 자동 박제
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

function listFiles(dir, ext) {
    const out = [];
    function walk(d) {
        for (const f of fs.readdirSync(d)) {
            const p = path.join(d, f);
            const st = fs.statSync(p);
            if (st.isDirectory()) walk(p);
            else if (f.endsWith(ext)) {
                const rel = path.relative('src', p).replace(/\\/g, '/');
                const lines = fs.readFileSync(p, 'utf-8').split('\n').length;
                out.push({ path: rel, lines });
            }
        }
    }
    walk(dir);
    return out;
}

const ctls = listFiles('src/HitPan.API/Controllers', '.cs');
const svcs = listFiles('src/HitPan.Application/Services', '.cs');
const dtos = listFiles('src/HitPan.Application/DTOs', '.cs');
const intf = listFiles('src/HitPan.Application/Interfaces', '.cs');
const razors = listFiles('src/HitPan.Web/Pages', '.razor').filter(f => !f.path.includes('.bak'));
const comps = listFiles('src/HitPan.Web/Components', '.razor');
const wsvcs = listFiles('src/HitPan.Web/Services', '.cs');
const sqls = listFiles('src/HitPan.API/Migrations/SQL', '.sql');

let md = `# 09. 프로그램 목록 — 전체 소스 파일 인벤토리

> **작성일**: 2026-06-01 / PM 브라운킴
> **자동 추출**: ${new Date().toISOString()}

---

## 1. 통계 요약

| 카테고리 | 파일 수 | 총 라인 |
|---|---|---|
| API Controller | ${ctls.length} | ${ctls.reduce((s,f)=>s+f.lines,0).toLocaleString()} |
| Application Service | ${svcs.length} | ${svcs.reduce((s,f)=>s+f.lines,0).toLocaleString()} |
| DTO | ${dtos.length} | ${dtos.reduce((s,f)=>s+f.lines,0).toLocaleString()} |
| Interface | ${intf.length} | ${intf.reduce((s,f)=>s+f.lines,0).toLocaleString()} |
| Razor Page | ${razors.length} | ${razors.reduce((s,f)=>s+f.lines,0).toLocaleString()} |
| Razor Component | ${comps.length} | ${comps.reduce((s,f)=>s+f.lines,0).toLocaleString()} |
| Web Service | ${wsvcs.length} | ${wsvcs.reduce((s,f)=>s+f.lines,0).toLocaleString()} |
| SQL Migration | ${sqls.length} | ${sqls.reduce((s,f)=>s+f.lines,0).toLocaleString()} |
| **합계** | **${ctls.length+svcs.length+dtos.length+intf.length+razors.length+comps.length+wsvcs.length+sqls.length}** | |

---

`;

function section(title, list) {
    md += `## ${title} (${list.length}건)\n\n`;
    md += `| # | 경로 | 라인 |\n|---|---|---|\n`;
    list.sort((a,b) => a.path.localeCompare(b.path));
    list.forEach((f, i) => md += `| ${i+1} | \`${f.path}\` | ${f.lines} |\n`);
    md += `\n`;
}

section('2. API Controller', ctls);
section('3. Application Service', svcs);
section('4. Interface', intf);
section('5. DTO', dtos);
section('6. Web Razor Page', razors);
section('7. Web Razor Component', comps);
section('8. Web Service', wsvcs);
section('9. SQL Migration', sqls);

fs.writeFileSync(path.join('docs', 'erp-handover', '09_프로그램목록.md'), md);
console.log(`프로그램 목록 박제 완료 — 크기: ${(md.length/1024).toFixed(1)} KB`);
