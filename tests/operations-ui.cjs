const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
(async () => {
 const browser = await chromium.launch({headless:true, channel:'msedge'});
 const errors=[]; fs.mkdirSync('tests/artifacts/operations',{recursive:true});
 async function login(role) {
  const page=await browser.newPage({baseURL:'http://127.0.0.1:15039',viewport:{width:1440,height:1000}});
  page.on('pageerror',e=>errors.push(e.message));
  await page.goto('/Login'); await page.locator('[name=username]').fill(role); await page.locator('[name=password]').fill(process.env.OPERATIONS_QA_PASSWORD);
  await Promise.all([page.waitForURL(u=>!u.pathname.includes('/Login')),page.locator('button[type=submit]').click()]); return page;
 }
 async function layout(page,path,name) {
  const res=await page.goto(path); assert.equal(res.status(),200,path);
  for (const width of [1440,1024,768,390]) {
   await page.setViewportSize({width,height:1000});
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${name} overflow ${width}`);
   assert(await page.locator('.main').isVisible());
   if(width===390) { await page.locator('#mobileMenuToggle').click(); assert.equal(await page.locator('#mobileMenuToggle').getAttribute('aria-expanded'),'true'); await page.keyboard.press('Escape'); }
   for (const table of await page.locator('.ops-table').all()) {
    assert(await table.evaluate(t=>{ const h=[...t.tHead.rows[0].cells].map(c=>c.getBoundingClientRect());return [...t.tBodies[0].rows].filter(r=>r.cells.length===h.length).every(r=>[...r.cells].every((c,i)=>Math.abs(c.getBoundingClientRect().left-h[i].left)<1)); }),`${name} table alignment`);
   }
   await page.screenshot({path:`tests/artifacts/operations/${name}-${width}.png`,fullPage:true});
  }
 }
 try {
  const accounting=await login('accounting'); assert.equal(new URL(accounting.url()).pathname,'/Accounting/Receivables');
  await layout(accounting,'/Accounting/Receivables','receivables');
  assert.equal(await accounting.locator('.sidebar-nav a.active').count(),1);
  await accounting.locator('select[name=status]').selectOption('soon'); await accounting.locator('main button[type=submit],main button').first().click();
  assert((await accounting.locator('main').innerText()).includes('QA-soon')); assert(!(await accounting.locator('tbody').innerText()).includes('QA-future'));
  await layout(accounting,'/Accounting','accounting');
  await accounting.locator('details summary').first().click();
  assert.equal(await accounting.locator('form[method=post]').count(),0);
  await layout(accounting,`/Bookings/Details/${process.env.OPERATIONS_BOOKING_ID}`,'booking-detail');
  assert.equal(await accounting.locator('form[action^="/Bookings/"]').count(),0);
  const legal=await login('legal'); await layout(legal,'/Legal','legal-monitor');
  await layout(legal,'/LegalDocuments?kind=booking','legal-documents');
  await layout(legal,'/LegalDocuments/Edit?kind=booking','legal-editor');
  await legal.locator('#BookingId').selectOption(process.env.OPERATIONS_BOOKING_ID);
  await legal.locator('#Title').fill('Browser handoff'); await legal.locator('#Content').fill('Nội dung hợp đồng kiểm tra trình duyệt.');
  await Promise.all([legal.waitForURL('**/LegalDocuments/Details/*'),legal.getByRole('button',{name:'Lưu hồ sơ',exact:true}).click()]);
  const draftPath=new URL(legal.url()).pathname;
  assert((await legal.locator('main').innerText()).includes('Bản soạn chưa gửi'));
  await Promise.all([legal.waitForNavigation(),legal.getByRole('button',{name:'Chuyển cho Booking',exact:true}).click()]);
  assert((await legal.locator('main').innerText()).includes('Đã chuyển Booking'));
  assert.equal(await legal.getByRole('link',{name:'Chỉnh sửa',exact:true}).count(),0);
  await layout(legal,draftPath,'legal-sent');
  const owner=await login('booking'); await owner.goto(`/Bookings/Details/${process.env.OPERATIONS_BOOKING_ID}`);
  assert.equal(await owner.locator('input[name=contractFile],form[action*="UploadContract"]').count(),0);
  assert((await owner.locator('#booking-legal-contracts').innerText()).includes('Browser handoff'));
  await owner.locator(`#booking-legal-contracts a[href="${draftPath}"]`).click();
  assert.equal(new URL(owner.url()).pathname,draftPath);
  await layout(accounting,draftPath,'accountant-contract'); assert.equal(await accounting.locator('form[method=post]').count(),0);
  for(const kind of ['employment','policy']) {
   await layout(legal,`/LegalDocuments/Edit?kind=${kind}`,`editor-${kind}`);
   assert(!await legal.locator('#booking-choice').isVisible()); assert.equal(await legal.locator('#employee-choice').isVisible(),kind==='employment');
   await legal.locator('#Title').fill(`Browser ${kind}`); await legal.locator('#Content').fill('Nội dung kiểm tra');
   if(kind==='employment') await legal.locator('#EmployeeId').selectOption(process.env.OPERATIONS_EMPLOYEE_ID);
   await Promise.all([legal.waitForURL('**/LegalDocuments/Details/*'),legal.getByRole('button',{name:'Lưu hồ sơ',exact:true}).click()]);
   assert.equal(await legal.getByRole('button',{name:'Chuyển cho Booking',exact:true}).count(),0);
  }
  await legal.goto(`/LegalDocuments?kind=booking&bookingId=${process.env.OPERATIONS_BOOKING_ID}`);
  await legal.getByRole('button',{name:'Lọc danh sách'}).click(); assert.equal(new URL(legal.url()).searchParams.get('bookingId'),process.env.OPERATIONS_BOOKING_ID);
  assert.deepEqual(errors,[]); console.log('PASS 10 screens x 4 widths; readonly controls; filters; draft/send/receive; employment/policy saves; no JS errors');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
