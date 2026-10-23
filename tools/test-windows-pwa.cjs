// Browser integration test with a temporary isolated profile; never reads operator credentials.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');
const { chromium } = require(process.env.LICENSEHUB_PLAYWRIGHT_MODULE || 'playwright');
const state = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const url = new URL(state.baseUrl);
if (process.platform !== 'win32' || state.purpose !== 'synthetic-local-production-rehearsal' || !state.passed || url.hostname !== 'localhost' || url.protocol !== 'https:' || +url.port < 8450 || +url.port > 8999) throw new Error('A verified synthetic local Windows rehearsal is required');
if (!/^[a-f0-9]{32}$/.test(state.id) || state.api !== `licensehub-release-${state.id}-tests-api` || state.database !== 'licensehub_release_tests') throw new Error('Only isolated synthetic API containers are permitted');
const fixture = JSON.parse(fs.readFileSync(path.join(state.privateDirectory, 'bootstrap.json'), 'utf8'));
if (!/^release-[a-f0-9]{32}@example\.invalid$/.test(fixture.email)) throw new Error('Only synthetic admin fixtures are accepted');
function totp(key) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...key].map(c => alphabet.indexOf(c).toString(2).padStart(5, '0')).join('');
  const secret = Buffer.from(bits.match(/.{8}/g).map(x => parseInt(x, 2)));
  const counter = Buffer.alloc(8); counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const hash = crypto.createHmac('sha1', secret).update(counter).digest();
  return ((hash.readUInt32BE(hash[19] & 15) & 0x7fffffff) % 1000000).toString().padStart(6, '0');
}
const results = [];
function check(condition, name) { if (!condition) throw new Error(name); results.push(name); console.log('PASS ' + name); }
(async () => {
  const browser = await chromium.launch({channel: 'msedge', headless: true, args: ['--disable-background-networking', '--disable-component-update', '--disable-sync', '--no-first-run']});
  try {
    const context = await browser.newContext({viewport: {width: 1366, height: 900}});
    await context.route('**/*', route => {
      const requestUrl = new URL(route.request().url());
      return requestUrl.origin === url.origin ? route.continue() : route.abort();
    });
    const page = await context.newPage();
    await page.goto(url.origin, {waitUntil: 'networkidle'});
    await page.getByText('Servicio disponible', {exact: true}).waitFor();
    check(true, 'Trusted HTTPS and actual Angular rendering on Windows Edge');
    await page.getByRole('button', {name: 'Modo oscuro', exact: true}).click();
    await page.reload({waitUntil: 'networkidle'});
    check(await page.getByRole('button', {name: 'Modo claro', exact: true}).isVisible(), 'Dark theme persists after reload');
    await page.getByLabel('Correo electrónico', {exact: true}).fill(fixture.email);
    await page.getByLabel('Contraseña', {exact: true}).fill(fixture.password);
    await page.getByRole('button', {name: 'Continuar con MFA', exact: true}).click();
    // Fresh time step on reruns; exercise replay protection rather than disabling it.
    await page.waitForTimeout(30000 - (Date.now() % 30000) + 1000);
    await page.getByLabel('Código de autenticador', {exact: true}).fill(totp(fixture.authenticatorKey));
    await page.getByRole('button', {name: 'Verificar MFA', exact: true}).click();
    await page.getByText('MFA verificado.', {exact: true}).waitFor();
    check(true, 'Synthetic admin login and MFA through real UI');
    const cookies = (await context.cookies()).filter(c => c.name.startsWith('__Host-'));
    check(cookies.length >= 2 && cookies.every(c => c.secure && c.httpOnly && c.sameSite === 'Strict'), 'Administrative cookies Secure HttpOnly SameSite Strict');
    for (const width of [360, 390, 768, 1366]) {
      await page.setViewportSize({width, height: 900});
      await page.waitForTimeout(250);
      const dimensions = await page.evaluate(() => ({width: innerWidth, scroll: document.documentElement.scrollWidth}));
      await page.screenshot({path: path.join(state.evidenceDirectory, `windows-${width}.png`), fullPage: true});
      const overflow = await page.evaluate(() => [...document.querySelectorAll('*')].map(e => ({tag:e.tagName,classes:e.className?.toString(),right:e.getBoundingClientRect().right,width:e.getBoundingClientRect().width})).filter(e=>e.right>innerWidth+1));
      fs.writeFileSync(path.join(state.evidenceDirectory, `layout-${width}.json`), JSON.stringify({dimensions,overflow},null,2));
      check(dimensions.scroll <= dimensions.width + 1, `No horizontal overflow at ${width}px (emulated viewport)`);
    }
    // Real browser workflow against the isolated PostgreSQL/API, with no mocked responses.
    await page.setViewportSize({width:1366,height:900});
    const appName = `Tienda prueba ${Date.now()}`;
    const slug = appName.toLowerCase().replaceAll(' ', '-');
    await page.getByRole('button', {name:'+ Registrar aplicación',exact:true}).click();
    await page.getByLabel('Nombre de la aplicación',{exact:true}).fill(appName);
    check(await page.getByLabel('Identificador corto (slug)',{exact:true}).inputValue() === slug, 'Application slug suggested automatically from name');
    page.once('dialog',dialog=>dialog.dismiss());
    await page.getByRole('button',{name:'Cancelar',exact:true}).click();
    check(await page.getByLabel('Nombre de la aplicación',{exact:true}).inputValue() === appName,'Cancelling navigation preserves unsaved application draft');
    const createdResponse = page.waitForResponse(r=>r.url().endsWith('/api/v1/admin/applications') && r.request().method()==='POST');
    await page.getByRole('button',{name:'Crear aplicación',exact:true}).click();
    const created = await createdResponse;
    check(created.status()===201,'Application created through real API with CSRF and idempotency');
    const application = await created.json();
    const card=page.locator('.application-card').filter({has:page.getByRole('heading',{name:appName,exact:true})});
    await card.getByRole('button',{name:'Abrir aplicación →',exact:true}).click();
    await page.getByRole('button',{name:'Conexión y funciones',exact:true}).click();
    await page.getByLabel('Código inmutable',{exact:true}).fill('inventory.premium');
    await page.getByLabel('Nombre',{exact:true}).fill('Inventario Premium');
    const featureResponse=page.waitForResponse(r=>r.url().endsWith('/features') && r.request().method()==='POST');
    await page.getByRole('button',{name:'Guardar funcionalidad',exact:true}).click();
    check((await featureResponse).status()===201,'Premium feature created in selected application catalog');
    await page.getByRole('button',{name:'Inicio',exact:true}).click();
    await page.getByRole('button',{name:/02Gestionar clientes|Gestionar clientes/}).click();
    await page.getByLabel('Identificador de negocio',{exact:true}).fill('usability-business');
    await page.getByLabel('Nombre del cliente',{exact:true}).fill('Cliente de prueba');
    const customerResponse=page.waitForResponse(r=>r.url().endsWith('/api/v1/admin/customers') && r.request().method()==='POST');
    await page.getByRole('button',{name:'Registrar cliente',exact:true}).click();
    const customerCreated=await customerResponse;
    check(customerCreated.status()===201 && (await customerCreated.json()).subjectId==='usability-business','Customer registration within selected application');
    await page.getByRole('button',{name:'Buscar / actualizar clientes',exact:true}).click();
    await page.getByRole('button',{name:'Preasignar licencia',exact:true}).click();
    check(await page.getByLabel('Cliente (opcional)',{exact:true}).locator('option:checked').textContent()==='Cliente de prueba','Customer assigned by name without pasting UUID');
    check(await page.getByLabel('Duración',{exact:true}).inputValue()==='30' && await page.getByLabel('Unidad',{exact:true}).inputValue()==='Day','New licenses default to thirty days');
    await page.getByRole('checkbox',{name:/Inventario Premium/}).check();
    const issueResponse=page.waitForResponse(r=>r.url().endsWith('/api/v1/admin/licenses') && r.request().method()==='POST');
    await page.getByRole('button',{name:'Emitir licencias',exact:true}).click();
    const issuedResponse=await issueResponse;
    check(issuedResponse.status()===201 && issuedResponse.request().postDataJSON().applicationId===application.id && issuedResponse.request().postDataJSON().features.includes('inventory.premium'),'Real license issuance scoped to selected application');
    await page.getByLabel('Código de licencia 1',{exact:true}).waitFor();
    page.once('dialog',dialog=>dialog.dismiss());
    await page.getByRole('button',{name:'Pagos',exact:true}).click();
    check(await page.getByLabel('Código de licencia 1',{exact:true}).isVisible(),'Navigation protects one-time license codes');
    page.once('dialog',dialog=>dialog.accept());
    await page.getByRole('button',{name:'Ya guardé los códigos: ocultar',exact:true}).click();
    await page.getByRole('button',{name:'Pagos',exact:true}).click();
    await page.locator('app-payment-manager').waitFor();
    check(await page.locator('app-payment-manager').isVisible() && await page.locator('app-license-manager').count()===0,'Only selected workspace section remains visible');
    await page.getByRole('button',{name:'Inicio',exact:true}).click();
    for(const width of [360,390,768,1366]) {
      await page.setViewportSize({width,height:900});
      await page.screenshot({path:path.join(state.evidenceDirectory,`workspace-${width}.png`),fullPage:true});
      check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`Application workspace fits ${width}px`);
    }
    await page.getByRole('button',{name:'Modo claro',exact:true}).click();
    await page.screenshot({path:path.join(state.evidenceDirectory,'workspace-light.png'),fullPage:true});
    await page.getByRole('button',{name:'← Mis aplicaciones',exact:true}).click();
    const docker = process.env.LICENSEHUB_DOCKER_EXE || path.join(process.env.LOCALAPPDATA, 'Programs/DockerDesktop/resources/bin/docker.exe');
    execFileSync(docker, ['restart', state.api], {stdio:'ignore', windowsHide:true, timeout:30000});
    await page.waitForTimeout(4000);
    await page.reload({waitUntil: 'networkidle'});
    await page.getByText('MFA verificado.', {exact: true}).waitFor();
    check(true, 'Encrypted persistent keyring preserves session across synthetic API restart');
    await page.waitForFunction(() => !!navigator.serviceWorker.controller, null, {timeout: 45000});
    check(true, 'Angular service worker controls page');
    const cachedPrivate = await page.evaluate(async () => {
      for (const name of await caches.keys()) for (const request of await (await caches.open(name)).keys()) if (/\/(api|health)\//.test(new URL(request.url).pathname)) return true;
      return false;
    });
    check(!cachedPrivate, 'No API or health entries in service worker caches');
    await context.setOffline(true);
    await page.locator('.connection-panel summary').click();
    await page.getByRole('button', {name: 'Comprobar ahora', exact: true}).click();
    await page.getByText('Servicio no disponible', {exact: true}).waitFor({timeout: 20000});
    const offlineStatus = await page.evaluate(async () => {try {return (await fetch('/api/v1/admin/auth/session')).status;} catch {return 0;}});
    check(offlineStatus === 0 || offlineStatus >= 500, 'Offline private session is not served as cached success');
    await page.reload({waitUntil: 'domcontentloaded'});
    await page.getByRole('heading', {name: /Bienvenido a LicenseHub/}).waitFor();
    check(true, 'App shell reopens offline without caching private API data');
    await context.setOffline(false);
    await page.reload({waitUntil: 'networkidle'});
    await page.getByText('Servicio disponible', {exact: true}).waitFor();
    await page.getByText('MFA verificado.', {exact: true}).waitFor();
    check(true, 'Reconnect retrieves current session from server');
    await page.getByRole('button', {name: 'Cerrar sesión', exact: true}).click();
    await page.getByRole('button', {name: 'Continuar con MFA', exact: true}).waitFor();
    const afterLogout = await page.evaluate(async () => (await fetch('/api/v1/admin/auth/session')).status);
    check(afterLogout === 401, 'Logout revokes session on backend');
    const report = {platform: process.platform, browser: await browser.version(), mode: 'headless automation on Windows, not installed PWA verification', passed: true, checks: results, testedAt: new Date().toISOString(), pending: ['Manual installation/open/reopen/update in Windows', 'Physical Android/iOS/macOS/Linux']};
    fs.writeFileSync(path.join(state.evidenceDirectory, 'windows-browser-result.json'), JSON.stringify(report, null, 2));
    console.log(`Windows automated checks passed: ${results.length}`);
    await context.close();
  } finally { await browser.close(); }
})().catch(error => {fs.writeFileSync(path.join(state.evidenceDirectory,'windows-failure.json'),JSON.stringify({name:error.name,message:error.message},null,2));console.error('Windows browser test failed: ' + error.name + '; review private evidence. No credentials printed.'); process.exitCode = 1;});
