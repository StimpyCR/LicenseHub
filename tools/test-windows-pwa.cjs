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
    await page.getByRole('button', {name: 'Comprobar ahora', exact: true}).click();
    await page.getByText('Servicio no disponible', {exact: true}).waitFor({timeout: 20000});
    const offlineStatus = await page.evaluate(async () => {try {return (await fetch('/api/v1/admin/auth/session')).status;} catch {return 0;}});
    check(offlineStatus === 0 || offlineStatus >= 500, 'Offline private session is not served as cached success');
    await page.reload({waitUntil: 'domcontentloaded'});
    await page.getByRole('heading', {name: /Tus aplicaciones/}).waitFor();
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
