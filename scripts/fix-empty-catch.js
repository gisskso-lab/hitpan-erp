// 5/27 헌법 #15 정합 봉합 — 빈 catch 27건 일괄 정리
// 패턴: try { tx.Rollback(); } catch { /* ... */ }
// 봉합: try { tx.Rollback(); } catch (Exception rbex) { Console.Error.WriteLine($"[Class] rollback failed: {rbex.Message}"); }
const fs = require('fs');
const path = require('path');

const files = [
  'src/HitPan.Application/Services/ApprovalService.cs',
  'src/HitPan.Application/Services/CollectionService.cs',
  'src/HitPan.Application/Services/FinanceService.cs',
  'src/HitPan.Application/Services/PurchaseService.cs',
  'src/HitPan.Application/Services/SalesService.cs',
  'src/HitPan.Application/Services/StockService.cs',
  'src/HitPan.Application/Services/TaxInvoiceService.cs',
];

let total = 0;
for (const f of files) {
  const fullPath = path.resolve(f);
  let c = fs.readFileSync(fullPath, 'utf8');
  const cn = path.basename(f, '.cs');

  // 패턴 1: try { tx.Rollback(); } catch { /* ... */ }
  const re1 = /(try\s*\{\s*tx\.Rollback\(\);\s*\})\s*catch\s*\{\s*\/\*[^*]*\*\/\s*\}/g;
  // 패턴 2: try { await tx.RollbackAsync(ct); } catch { /* ... */ }
  const re2 = /(try\s*\{\s*await\s+tx\.RollbackAsync\(ct\);\s*\})\s*catch\s*\{\s*\/\*[^*]*\*\/\s*\}/g;

  const replacement = (_, p1) =>
    `${p1} catch (Exception rbex) { Console.Error.WriteLine($"[${cn}] rollback failed: {rbex.Message}"); }`;

  const before = c;
  c = c.replace(re1, replacement);
  c = c.replace(re2, replacement);

  // 변경 카운트
  const n1 = (before.match(re1) || []).length;
  const n2 = (before.match(re2) || []).length;
  const cnt = n1 + n2;

  if (cnt > 0) {
    fs.writeFileSync(fullPath, c, 'utf8');
    console.log(`${f}: ${cnt}건 봉합`);
    total += cnt;
  } else {
    console.log(`${f}: 0건 (패턴 미일치)`);
  }
}
console.log(`\n총 ${total}건 봉합 완료`);
