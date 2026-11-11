const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
(async () => {
  assert(process.env.DEPARTMENT_QA_PASSWORD, 'Run with AccountingLegal --departments-ui');
  const browser = await chromium.launch({ headless: true, channel: 'msedge' });
  const errors = [];
  try {
    const page = await browser.newPage({ baseURL: 'http://127.0.0.1:15039', viewport: { width: 1440, height: 1000 } });
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('/Login');
    await page.locator('[name=username]').fill('director');
    await page.locator('[name=password]').fill(process.env.DEPARTMENT_QA_PASSWORD);
    await Promise.all([page.waitForURL(url => !url.pathname.includes('/Login')), page.locator('button[type=submit]').click()]);
    await page.goto('/Director/Department');
    await page.waitForFunction(() => document.getElementById('departmentTotal')?.textContent === '6');
    assert.equal(await page.locator('#departmentEmployees').innerText(), '12');
    assert.equal(await page.locator('#departmentActive').innerText(), '5');
    fs.mkdirSync('tests/artifacts/departments', { recursive: true });
    for (const width of [1440, 1024, 768, 390]) {
      await page.setViewportSize({ width, height: 1000 });
      await page.waitForTimeout(200);
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `No page overflow at ${width}`);
      if (width >= 768) {
        assert(await page.locator('.department-table-region').isVisible());
        assert.equal(await page.locator('#departmentRows tr').count(), 6);
        const aligned = await page.locator('.department-table').evaluate(table => {
          const headers = [...table.tHead.rows[0].cells].map(c => c.getBoundingClientRect());
          return [...table.tBodies[0].rows].every(row => [...row.cells].every((cell, i) => Math.abs(cell.getBoundingClientRect().left - headers[i].left) < 1 && Math.abs(cell.getBoundingClientRect().width - headers[i].width) < 1));
        });
        assert(aligned, `Headers and all cells align at ${width}`);
      } else {
        assert(!await page.locator('.department-table-region').isVisible());
        assert.equal(await page.locator('.department-item').count(), 6);
        await page.locator('#mobileMenuToggle').click();
        assert.equal(await page.locator('#mobileMenuToggle').getAttribute('aria-expanded'), 'true');
        await page.keyboard.press('Escape');
      }
      await page.screenshot({ path: `tests/artifacts/departments/departments-${width}.png`, fullPage: true });
      console.log(`PASS Department layout ${width}px`);
    }
    await page.locator('#departmentSearch').fill('phap che');
    assert.equal(await page.locator('.department-item').count(), 1);
    assert((await page.locator('.department-item').innerText()).includes('Pháp chế'));
    assert((await page.locator('.department-item .department-link').getAttribute('href')).includes('department=PHAP_LY'));
    await page.locator('#departmentSearch').fill('nothing-matches');
    assert((await page.locator('#departmentCards').innerText()).includes('Không có phòng ban phù hợp'));
    await page.locator('#departmentSearch').fill('');
    await page.locator('#departmentStatus').selectOption('inactive');
    assert.equal(await page.locator('.department-item').count(), 1);
    assert((await page.locator('.department-item').innerText()).includes('Công nghệ thông tin'));
    assert.equal(await page.locator('#departmentTotal').innerText(), '6');
    await page.locator('#departmentStatus').selectOption('');
    console.log('PASS Search, status filter, empty results and employee links');
    await page.route('**/Director/Department/ApiList', route => route.fulfill({ status: 503, body: 'Unavailable' }));
    await page.reload();
    await page.locator('#departmentError').waitFor({ state: 'visible' });
    assert(!await page.locator('#departmentResults').isVisible());
    await page.unroute('**/Director/Department/ApiList');
    await page.locator('#departmentRetry').click();
    await page.waitForFunction(() => document.getElementById('departmentTotal')?.textContent === '6');
    assert(!await page.locator('#departmentError').isVisible());
    console.log('PASS API failure and retry');
    await page.route('**/Director/Department/ApiList', route => route.fulfill({ json: { success: true, data: { list: [], summary: { departments: 0, activeDepartments: 0, employees: 0 } } } }));
    await page.reload();
    await page.waitForFunction(() => document.getElementById('departmentTotal')?.textContent === '0');
    assert((await page.locator('#departmentCards').innerText()).includes('Chưa có dữ liệu'));
    assert.deepEqual(errors, []);
    console.log('PASS Empty database and no browser script errors');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
