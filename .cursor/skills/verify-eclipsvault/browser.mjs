// One Chromium session for a verification run. Later commands reuse the same page,
// so an opened navigation group or the command palette stays open.
import { createHmac } from 'node:crypto';
import { unlinkSync } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { createConnection, createServer } from 'node:net';
import { dirname, resolve } from 'node:path';
import { spawn } from 'node:child_process';

const root = process.env.VERIFY_ROOT;
if (!root) throw new Error('VERIFY_ROOT is not set.');
const instance = resolve(root, 'logs/verify/instance');
const socketPath = resolve(instance, 'browser.sock');
const pidPath = resolve(instance, 'browser.pid');
const storagePath = resolve(instance, 'storage.json');
const urlPath = resolve(instance, 'url');
const require = createRequire(resolve(root, 'EclipsVault.AngularUi/package.json'));
const { chromium } = require('playwright');

let browser;
let context;
let page;

function base32(secret) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const cleaned = secret.replace(/=+$/u, '').toUpperCase();
  let bits = '';
  for (const char of cleaned) {
    const value = alphabet.indexOf(char);
    if (value < 0) throw new Error('The enrollment page did not show an authenticator key.');
    bits += value.toString(2).padStart(5, '0');
  }
  const bytes = [];
  for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(Number.parseInt(bits.slice(i, i + 8), 2));
  return Buffer.from(bytes);
}

async function totp(secret) {
  const step = 30;
  const remaining = step - (Math.floor(Date.now() / 1000) % step);
  if (remaining < 5) await new Promise((resolveWait) => setTimeout(resolveWait, (remaining + 1) * 1000));
  const counter = Math.floor(Date.now() / 1000 / step);
  const message = Buffer.alloc(8);
  message.writeBigUInt64BE(BigInt(counter));
  const digest = createHmac('sha1', base32(secret)).update(message).digest();
  const offset = digest[digest.length - 1] & 15;
  const code = (digest.readUInt32BE(offset) & 0x7fffffff) % 1_000_000;
  return String(code).padStart(6, '0');
}

async function adminPassword() {
  const env = await readFile(resolve(instance, 'env'), 'utf8');
  const line = env.split('\n').find((entry) => entry.startsWith('Seed__AdminPassword='));
  if (!line) throw new Error('The instance env file has no administrator password.');
  // The file is shell-quoted. A value we generated never contains a single quote.
  return line.slice('Seed__AdminPassword='.length).replace(/^'|'$/g, '');
}

async function baseUrl() {
  const state = JSON.parse(await readFile(resolve(instance, 'state.json'), 'utf8'));
  return state.baseUrl;
}

async function ensureBrowser() {
  if (page) return;
  browser = await chromium.launch({ headless: true });
  let storage;
  try {
    await readFile(storagePath);
    storage = storagePath;
  } catch {
    storage = undefined;
  }
  context = await browser.newContext({
    ignoreHTTPSErrors: true,
    viewport: { width: 1400, height: 900 },
    storageState: storage,
  });
  page = await context.newPage();
  page.setDefaultTimeout(15_000);
  page.setDefaultNavigationTimeout(30_000);
  try {
    const current = (await readFile(urlPath, 'utf8')).trim();
    if (current) await page.goto(current, { waitUntil: 'domcontentloaded' });
  } catch {
    // The first sign-in has no saved page yet.
  }
}

async function remember() {
  await context.storageState({ path: storagePath });
  await writeFile(urlPath, page.url());
}

function enrollmentHeading() {
  return page.getByRole('heading', { name: 'Set up two-factor authentication', exact: true });
}

function target(request) {
  // Chromium exposes a closed <summary> as text inside a group, not as a button.
  if (!request.role && request.text) {
    return page.getByText(request.text, { exact: Boolean(request.exact) });
  }
  const name = { name: request.name, exact: Boolean(request.exact) };
  if (request.row) {
    return page.getByRole('row', { name: request.row }).getByRole(request.role, name);
  }
  return page.getByRole(request.role, name);
}

async function signIn() {
  await ensureBrowser();
  const origin = await baseUrl();
  await page.goto(`${origin}/Account/Login`, { waitUntil: 'domcontentloaded' });
  await page.getByRole('heading', { name: 'Sign in', exact: true }).waitFor();
  await page.getByLabel('Username or email').fill('vault-admin');
  await page.getByLabel('Password', { exact: true }).fill(await adminPassword());
  await page.getByRole('button', { name: 'Continue', exact: true }).click();
  const enrolled = page.getByRole('heading', { name: 'Two-factor verification', exact: true });
  await enrollmentHeading().or(enrolled).waitFor();
  if (!(await enrollmentHeading().isVisible())) {
    throw new Error(
      'This account is already enrolled. The harness signs vault-admin in once, on a fresh database.',
    );
  }
  const secret = (await page.locator('code.secret-code').innerText()).trim();
  await page.getByLabel('Confirmation code').fill(await totp(secret));
  await page.getByRole('button', { name: 'Confirm enrollment', exact: true }).click();
  await page.getByRole('heading', { name: 'Secrets', exact: true }).waitFor();
  await remember();
  return `signed in vault-admin at ${page.url()}`;
}

async function refuseSecretPage() {
  if (await enrollmentHeading().isVisible().catch(() => false)) {
    throw new Error('Refusing to capture the authenticator enrollment page.');
  }
}

async function handle(request) {
  if (request.action === 'sign-in') return signIn();
  await ensureBrowser();
  if (!page.url() || page.url() === 'about:blank') {
    throw new Error('No page yet. Run verify-eclipsvault sign-in first.');
  }
  if (request.action === 'goto') {
    if (!request.path?.startsWith('/')) throw new Error('goto needs an absolute vault path.');
    await page.goto(`${await baseUrl()}${request.path}`, { waitUntil: 'domcontentloaded' });
  } else if (request.action === 'click') {
    await target(request).click();
  } else if (request.action === 'fill') {
    await target(request).fill(request.value);
  } else if (request.action === 'press') {
    await page.keyboard.press(request.key);
  } else if (request.action === 'expect') {
    if (request.text) await page.getByText(request.text, { exact: false }).first().waitFor();
    else await target(request).waitFor();
  } else if (request.action === 'screenshot' || request.action === 'snapshot') {
    await refuseSecretPage();
    if (!request.path) throw new Error(`${request.action} needs --path.`);
    const file = resolve(root, request.path);
    await mkdir(dirname(file), { recursive: true });
    if (request.action === 'screenshot') await page.screenshot({ path: file });
    else await writeFile(file, await page.locator('body').ariaSnapshot());
  } else {
    throw new Error(`Unknown browser action ${request.action}.`);
  }
  await remember();
  if (request.action === 'expect' && request.text) return `saw ${request.text}`;
  if (request.action === 'expect') return `saw ${request.role} ${request.name}`;
  if (request.action === 'screenshot' || request.action === 'snapshot') return `${request.action} ${request.path}`;
  return page.url();
}

function requestFromArgs(args) {
  const [action, ...rest] = args;
  if (action === 'sign-in') return { action: 'sign-in' };
  const request = { action };
  for (let i = 0; i < rest.length; i += 1) {
    const arg = rest[i];
    if (arg === '--exact') request.exact = true;
    else if (arg.startsWith('--')) {
      const value = rest[i + 1];
      if (!value || value.startsWith('--')) throw new Error(`${arg} needs a value.`);
      request[arg.slice(2)] = value;
      i += 1;
    } else if (action === 'goto' && !request.path) request.path = arg;
    else throw new Error(`Unexpected argument ${arg}.`);
  }
  return request;
}

async function processCommandLine(pid) {
  try {
    const raw = await readFile(`/proc/${pid}/cmdline`);
    return raw.toString('utf8').replaceAll('\0', ' ');
  } catch {
    return new Promise((resolveLine) => {
      const child = spawn('ps', ['-p', String(pid), '-o', 'args='], { stdio: ['ignore', 'pipe', 'ignore'] });
      let output = '';
      child.stdout?.on('data', (chunk) => {
        output += chunk;
      });
      child.on('close', () => resolveLine(output));
      child.on('error', () => resolveLine(''));
    });
  }
}

async function stopRecordedVault() {
  let state;
  try {
    state = JSON.parse(await readFile(resolve(instance, 'state.json'), 'utf8'));
  } catch {
    return;
  }
  const pid = Number(state.pid);
  const dll = state.dll;
  if (!Number.isInteger(pid) || pid <= 0 || typeof dll !== 'string' || dll.length === 0) return;
  const commandLine = await processCommandLine(pid);
  if (!commandLine.includes(dll)) return;
  try {
    process.kill(pid, 'SIGTERM');
  } catch {
    return;
  }
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await new Promise((resolveWait) => setTimeout(resolveWait, 250));
    try {
      process.kill(pid, 0);
    } catch {
      return;
    }
  }
  try {
    process.kill(pid, 'SIGKILL');
  } catch {
    // It exited between the check and this signal.
  }
}

async function serve() {
  try {
    unlinkSync(socketPath);
  } catch {
    // No stale socket from an earlier session.
  }
  await writeFile(pidPath, String(process.pid));
  const idleMs = 30 * 60 * 1000;
  let idleTimer;
  let activeRequests = 0;
  let closing = false;
  let server;
  const armIdle = () => {
    clearTimeout(idleTimer);
    idleTimer = setTimeout(() => {
      if (activeRequests === 0) void shutDown({ stopVault: true });
    }, idleMs);
  };
  const shutDown = async ({ stopVault }) => {
    if (closing) return;
    closing = true;
    clearTimeout(idleTimer);
    if (stopVault) await stopRecordedVault();
    await browser?.close().catch(() => {});
    server.close();
    process.exit(0);
  };
  server = createServer((connection) => {
    let body = '';
    connection.on('data', (chunk) => {
      body += chunk;
      const newline = body.indexOf('\n');
      if (newline < 0) return;
      const line = body.slice(0, newline);
      body = body.slice(newline + 1);
      activeRequests += 1;
      clearTimeout(idleTimer);
      Promise.resolve()
        .then(() => handle(JSON.parse(line)))
        .then((message) => connection.end(`${JSON.stringify({ ok: true, message })}\n`))
        .catch((error) => {
          const message = error instanceof Error ? error.message : String(error);
          connection.end(`${JSON.stringify({ ok: false, message })}\n`);
        })
        .finally(() => {
          activeRequests -= 1;
          if (!closing && activeRequests === 0) armIdle();
        });
    });
  });
  server.listen(socketPath);
  await new Promise((resolveListen) => server.on('listening', resolveListen));
  armIdle();
  process.on('SIGTERM', () => void shutDown({ stopVault: false }));
  process.on('SIGINT', () => void shutDown({ stopVault: false }));
}

function daemonAlive() {
  return new Promise((resolveAlive) => {
    const connection = createConnection(socketPath);
    const finish = (alive) => {
      connection.removeAllListeners();
      connection.destroy();
      resolveAlive(alive);
    };
    connection.once('connect', () => finish(true));
    connection.once('error', () => finish(false));
  });
}

async function startDaemon() {
  const child = spawn(process.execPath, [resolve(root, '.cursor/skills/verify-eclipsvault/browser.mjs'), 'daemon'], {
    detached: true,
    stdio: 'ignore',
    env: process.env,
  });
  child.unref();
  for (let attempt = 0; attempt < 50; attempt += 1) {
    if (await daemonAlive()) return;
    await new Promise((resolveWait) => setTimeout(resolveWait, 100));
  }
  throw new Error('The browser session did not start.');
}

function ask(request) {
  return new Promise((resolveAsk, rejectAsk) => {
    const connection = createConnection(socketPath);
    let body = '';
    connection.on('connect', () => connection.write(`${JSON.stringify(request)}\n`));
    connection.on('data', (chunk) => {
      body += chunk;
      if (!body.includes('\n')) return;
      const response = JSON.parse(body.slice(0, body.indexOf('\n')));
      connection.destroy();
      if (!response.ok) rejectAsk(new Error(response.message));
      else resolveAsk(response.message);
    });
    connection.on('error', rejectAsk);
  });
}

const [command, ...rest] = process.argv.slice(2);
try {
  if (command === 'daemon') {
    await serve();
  } else if (command === 'start') {
    // Launch starts the session so its idle timer also covers a vault nobody signs in to.
    if (!(await daemonAlive())) await startDaemon();
  } else {
    if (!(await daemonAlive())) await startDaemon();
    const request = command === 'sign-in' ? { action: 'sign-in' } : requestFromArgs(rest);
    const message = await ask(request);
    process.stdout.write(`${message}\n`);
  }
} catch (error) {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
}
