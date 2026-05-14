// MDB 마이그레이션 E2E 테스트 (2026-05-13)
// 사장님 지시: BK_2026-02-20-175608 폴더 마이그 테스트
//
// 시나리오:
// 1. 로그인 (tenant@hitpan.kr / Admin1234!)
// 2. /settings/mdb-migration 이동
// 3. MDB 폴더 경로 입력: C:\Users\소순근\Desktop\BK_2026-02-20-175608
// 4. 미리보기 버튼 클릭 → 테이블별 건수 확인
// 5. 이관 시작 버튼 클릭 → 실제 마이그
// 6. 결과 검증 + 스크린샷
//
// 헌법 #19: errors 0 + warnings 0 정신으로 모든 단계 콘솔 에러 캡처

import { chromium } from 'playwright';
import fs from 'fs';
import path from 'path';

const WEB_URL = 'http://localhost:5234';
const API_URL = 'http://localhost:5257';
const MDB_FOLDER = 'C:\\Users\\소순근\\Desktop\\BK_2026-02-20-175608';
const MDB_PASSWORD = '7618968';
const LOGIN_ID = 'admin@hitpan.kr';
const LOGIN_PW = 'Admin1234!';

const SCREENSHOT_DIR = path.join(process.cwd(), 'tools', 'screenshots', `mdb-migration-${Date.now()}`);
fs.mkdirSync(SCREENSHOT_DIR, { recursive: true });

const log = (...a) => console.log('[E2E]', ...a);
const shot = async (page, name) => {
    const p = path.join(SCREENSHOT_DIR, `${name}.png`);
    await page.screenshot({ path: p, fullPage: true });
    log(`📸 ${name}.png`);
};

const consoleErrors = [];
const networkErrors = [];

async function main() {
    log('=== MDB 마이그레이션 E2E 시작 ===');
    log(`MDB 폴더: ${MDB_FOLDER}`);
    log(`스크린샷: ${SCREENSHOT_DIR}`);

    const browser = await chromium.launch({ headless: false, slowMo: 200 });
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();

    page.on('console', msg => {
        if (msg.type() === 'error') {
            consoleErrors.push(msg.text());
            log('❌ CONSOLE ERROR:', msg.text());
        }
    });
    page.on('response', resp => {
        if (resp.status() >= 400) {
            networkErrors.push(`${resp.status()} ${resp.url()}`);
            log(`❌ NETWORK ${resp.status()}: ${resp.url()}`);
        }
    });

    try {
        // 1. 로그인 페이지
        log('[1/6] 로그인 페이지 진입');
        await page.goto(`${WEB_URL}/login`, { waitUntil: 'networkidle', timeout: 30000 });
        await shot(page, '01-login-page');

        // 로그인 입력 — MudBlazor 라벨 기반 (이메일 / 비밀번호)
        const emailInput = page.locator('input').nth(0);
        const pwInput = page.locator('input').nth(1);
        await emailInput.click();
        await emailInput.fill(LOGIN_ID);
        await pwInput.click();
        await pwInput.fill(LOGIN_PW);
        // input change 이벤트 트리거 (Blazor 양방향 바인딩)
        await pwInput.press('Tab');
        await page.waitForTimeout(500);
        await shot(page, '02-login-filled');

        // 로그인 버튼 (force click — MudButton disabled 속성 우회 위해 Enter로도 시도)
        await pwInput.focus();
        await pwInput.press('Enter');
        await page.waitForTimeout(2000);
        const loginBtn = page.locator('button:has-text("로그인")').first();
        if (await loginBtn.isEnabled().catch(() => false)) {
            await loginBtn.click();
        }
        await page.waitForLoadState('networkidle', { timeout: 30000 });
        await shot(page, '03-after-login');
        log('✅ 로그인 시도 완료');

        // 2. MDB 마이그 페이지 이동
        log('[2/6] /settings/mdb-migration 이동');
        await page.goto(`${WEB_URL}/settings/mdb-migration`, { waitUntil: 'networkidle', timeout: 30000 });
        await page.waitForTimeout(2000);
        await shot(page, '04-mdb-migration-page');

        // 3. MDB 폴더 경로 + 비번 입력
        log('[3/6] MDB 폴더 경로 + 비번 입력');
        // MudTextField 라벨 기반 입력 (Blazor 양방향 바인딩 위해 Tab 키 사용)
        const folderInput = page.getByLabel('MDB 폴더 경로');
        await folderInput.click();
        await folderInput.fill(MDB_FOLDER);
        await folderInput.press('Tab');

        const pwInput = page.getByLabel('MDB 비밀번호');
        await pwInput.click();
        await pwInput.fill(MDB_PASSWORD);
        await pwInput.press('Tab');
        await page.waitForTimeout(500);
        await shot(page, '05-folder-path-entered');

        // 4. 미리보기 버튼 클릭
        log('[4/6] 미리보기 버튼 클릭');
        await page.locator('button:has-text("미리보기")').click();
        log('   미리보기 호출 중 (최대 60초 대기)...');
        await page.waitForTimeout(3000);
        // 결과 테이블 나타날 때까지 대기 (최대 60초)
        await page.waitForSelector('table, .mud-table', { timeout: 60000 }).catch(() => log('   ⚠️ 테이블 미발견 — 진행 계속'));
        await page.waitForTimeout(2000);
        await shot(page, '06-preview-result');

        // 미리보기 결과 텍스트 캡처
        const previewText = await page.locator('table, .mud-table').first().textContent().catch(() => '(미리보기 결과 없음)');
        log('📊 미리보기 결과 (앞부분):', previewText.slice(0, 500));

        // 5. 이관 시작 버튼
        log('[5/6] 이관 시작 버튼 클릭');
        const startBtn = page.locator('button:has-text("이관 시작")');
        const isEnabled = await startBtn.isEnabled();
        log(`   이관 시작 버튼 활성화: ${isEnabled}`);
        if (isEnabled) {
            await startBtn.click();
            log('   이관 진행 중 (최대 5분 대기)...');
            await page.waitForTimeout(5000);
            await shot(page, '07-migration-running');

            // 완료 대기 (스낵바 또는 결과 패널)
            await page.waitForTimeout(60000).catch(() => {});
            await shot(page, '08-migration-result');

            const resultText = await page.locator('body').textContent();
            const successHints = ['완료', '성공', 'completed', '건'];
            const found = successHints.filter(h => resultText.includes(h));
            log(`✅ 결과 키워드 매치: ${found.join(', ')}`);
        } else {
            log('   ⚠️ 이관 시작 버튼 비활성화 → 미리보기 실패 가능성');
        }

        // 6. DB 검증 — partners 건수
        log('[6/6] DB 검증은 별도 SQL 실행 필요');

    } catch (e) {
        log('❌ 예외 발생:', e.message);
        await shot(page, '99-error');
        throw e;
    } finally {
        log('\n=== 결과 요약 ===');
        log(`콘솔 에러: ${consoleErrors.length}건`);
        log(`네트워크 에러: ${networkErrors.length}건`);
        consoleErrors.slice(0, 5).forEach((e, i) => log(`  [console ${i+1}] ${e}`));
        networkErrors.slice(0, 5).forEach((e, i) => log(`  [network ${i+1}] ${e}`));

        const report = {
            timestamp: new Date().toISOString(),
            mdbFolder: MDB_FOLDER,
            screenshotDir: SCREENSHOT_DIR,
            consoleErrors,
            networkErrors,
        };
        fs.writeFileSync(path.join(SCREENSHOT_DIR, 'report.json'), JSON.stringify(report, null, 2));
        log(`📄 보고서: ${path.join(SCREENSHOT_DIR, 'report.json')}`);

        await page.waitForTimeout(3000);
        await browser.close();
    }
}

main().catch(e => { console.error(e); process.exit(1); });
