import { spawn } from 'node:child_process';
import { generateKeyPairSync, randomBytes, randomUUID } from 'node:crypto';
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import https from 'node:https';
import net from 'node:net';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { setTimeout } from 'node:timers/promises';

const ui = fileURLToPath(new URL('../', import.meta.url));
const root = fileURLToPath(new URL('../../', import.meta.url));
const probes = process.argv.includes('--verify-guards');
const evidence = process.env.E2E_EVIDENCE_DIR || ui;
const output = new URL(probes ? 'guard-results/' : 'test-results/', `file://${evidence}/`);
const testResults = new URL('test-results/', `file://${evidence}/`);
// Cleanup removes this project's containers and volumes, so the runner always names a project of its
// own: a prefix may be chosen, the random suffix may not, and an existing project is never adopted.
// Specs still receive the name as E2E_COMPOSE_PROJECT.
const project = `${process.env.E2E_PROJECT_PREFIX || 'eclipsvault-e2e'}-${randomUUID().slice(0, 8)}`;
const image = process.env.E2E_IMAGE || `${project}:test`;
const abort = new AbortController();
for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => abort.abort());
}

function run(command, args, env, { capture = false, cleanup = false } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: ui,
      env,
      signal: cleanup ? undefined : abort.signal,
      stdio: capture ? ['ignore', 'pipe', 'inherit'] : 'inherit',
    });
    let stdout = '';
    child.stdout?.on('data', (data) => {
      stdout += data;
    });
    child.on('error', reject);
    child.on('close', (code) => resolve({ code, stdout: stdout.trim() }));
  });
}

async function checked(command, args, env, options) {
  const result = await run(command, args, env, options);
  if (result.code !== 0) throw new Error(`${command} ${args[0]} failed (exit ${result.code})`);
  return result.stdout;
}

async function waitForVault(baseURL) {
  const deadline = Date.now() + 180_000;
  while (Date.now() < deadline) {
    abort.signal.throwIfAborted();
    const status = await new Promise((resolve) => {
      const request = https.get(
        `${baseURL}/Account/Login`,
        { rejectUnauthorized: false },
        (response) => {
          response.resume();
          resolve(response.statusCode);
        },
      );
      request.setTimeout(2_000, () => request.destroy());
      request.on('error', () => resolve(0));
    });
    if (status === 200) return;
    await setTimeout(1_000, undefined, { signal: abort.signal });
  }
  throw new Error('The production vault did not serve sign-in within 180 seconds; see docker.log');
}

function verifyGuardFailures(report, exitCode) {
  const tests = [];
  function visit(suite) {
    for (const spec of suite.specs ?? []) {
      tests.push(...spec.tests.map((test) => ({ ...test, title: spec.title })));
    }
    for (const child of suite.suites ?? []) visit(child);
  }
  visit(report);
  const diagnostics = [
    ['CSP violation', 'CSP violation:'],
    ['an expected CSP violation excuses nothing else', 'CSP violation: script-src-elem: inline'],
    ['an expected CSP violation that never occurs', 'expected CSP violations that never occurred'],
    ['console.error after navigation in a second page', 'console.error: e2e console sentinel'],
    ['uncaught browser exception', 'pageerror: e2e exception sentinel'],
    ['console.error in a second browser context', 'console.error: e2e second-context sentinel'],
    ['an expected console error excuses nothing else', 'console.error: e2e unexcused sentinel'],
    ['an expected console error that never occurs', 'expected console errors that never occurred'],
  ];
  if (exitCode !== 1 || tests.length !== diagnostics.length || report.errors.length !== 0) {
    throw new Error(
      `Guard probes did not produce exactly ${diagnostics.length} ordinary test failures`,
    );
  }
  for (const [title, diagnostic] of diagnostics) {
    const match = tests.find(
      (test) =>
        test.title === title &&
        test.results.some(
          (result) =>
            result.status === 'failed' &&
            result.errors.some(
              (error) =>
                error.message?.includes('[browser-health]') && error.message.includes(diagnostic),
            ),
        ),
    );
    if (!match) throw new Error(`Guard probe did not fail in the shared fixture for ${diagnostic}`);
    if (
      title === 'an expected CSP violation excuses nothing else' &&
      match.results.some((result) =>
        result.errors.some((error) =>
          error.message?.includes('expected CSP violations that never occurred'),
        ),
      )
    ) {
      throw new Error('The CSP expectation probe never satisfied its expected violation');
    }
    console.log(`Verified real Playwright failure: ${diagnostic}`);
  }
}

function freeLoopbackPort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      if (!address || typeof address === 'string') {
        reject(new Error('Could not reserve a loopback port'));
        return;
      }
      const { port } = address;
      server.close((error) => (error ? reject(error) : resolve(port)));
    });
  });
}

// 20 bytes is 32 Base32 characters, which is the authenticator secret length Keycloak imports.
function base32Secret() {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...randomBytes(20)].map((byte) => byte.toString(2).padStart(8, '0')).join('');
  return bits
    .match(/.{5}/g)
    .map((chunk) => alphabet[Number.parseInt(chunk, 2)])
    .join('');
}

async function writeRealm(path, values) {
  let realm = await readFile(new URL('./realm.json', import.meta.url), 'utf8');
  const tokens = {
    __VAULT_ORIGIN__: values.baseURL,
    __CLIENT_SECRET__: values.clientSecret,
    __USER_PASSWORD__: values.idpPassword,
    __OTP_SECRET__: values.otpSecret,
  };
  for (const [token, value] of Object.entries(tokens)) {
    if (/["\\\n]/.test(value)) throw new Error(`Refusing to embed ${token}`);
    if (!realm.includes(token)) throw new Error(`Realm template is missing ${token}`);
    realm = realm.replaceAll(token, value);
  }
  if (realm.includes('__')) throw new Error('Realm template still has a placeholder');
  // Keycloak runs as its own user. On CI that uid is not the runner's, so mode 600 is
  // unreadable through the bind mount and the import fails with "Permission denied".
  // The file exists only for this run, under a private temporary directory.
  await writeFile(path, realm, { mode: 0o644 });
}

async function trustProxyCertificate(compose, bundlePath) {
  const root = await compose(
    ['exec', '-T', 'proxy', 'cat', '/data/caddy/pki/authorities/local/root.crt'],
    { capture: true },
  );
  if (!root.includes('BEGIN CERTIFICATE')) {
    throw new Error('The proxy did not write its local certificate authority');
  }
  const system = await readFile('/etc/ssl/certs/ca-certificates.crt', 'utf8');
  await writeFile(bundlePath, `${system.trim()}\n${root.trim()}\n`, { mode: 0o644 });
}

async function configureIdpAuthenticationReferences(compose) {
  // The imported browser flow has no authenticator reference values. With the default max age of
  // zero, Keycloak also drops a reference unless the token is minted in the same second the step
  // completed, and the code exchange is a later request.
  await compose([
    'exec',
    '-T',
    'keycloak',
    'bash',
    '-c',
    '/opt/keycloak/bin/kcadm.sh config credentials --server http://127.0.0.1:8080 --realm master --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null',
  ]);
  const listed = await compose(
    [
      'exec',
      '-T',
      'keycloak',
      '/opt/keycloak/bin/kcadm.sh',
      'get',
      'authentication/flows/browser/executions',
      '-r',
      'e2e',
    ],
    { capture: true },
  );
  const jsonStart = listed.indexOf('[');
  if (jsonStart < 0) throw new Error('Keycloak did not list its browser authentication steps');
  const executions = JSON.parse(listed.slice(jsonStart));
  const references = new Map([
    ['auth-username-password-form', 'pwd'],
    ['auth-otp-form', 'otp'],
  ]);
  for (const execution of executions) {
    const reference = references.get(execution.providerId);
    if (!reference || !/^[0-9a-f-]{36}$/i.test(execution.id)) continue;
    await compose([
      'exec',
      '-T',
      'keycloak',
      '/opt/keycloak/bin/kcadm.sh',
      'create',
      `authentication/executions/${execution.id}/config`,
      '-r',
      'e2e',
      '-b',
      JSON.stringify({
        alias: `amr-${reference}`,
        config: {
          'default.reference.value': reference,
          'default.reference.maxAge': '300',
        },
      }),
    ]);
    references.delete(execution.providerId);
  }
  if (references.size) {
    throw new Error(
      `Keycloak is missing authentication steps: ${[...references.keys()].join(', ')}`,
    );
  }
}

function readHttps(port) {
  return new Promise((resolve) => {
    // The host resolver does not treat *.localhost as loopback. Chromium does, which is what the
    // browser uses; this check talks to the published port with the proxy's name in SNI.
    const request = https.get(
      {
        host: '127.0.0.1',
        port,
        path: '/realms/e2e/.well-known/openid-configuration',
        servername: 'keycloak.localhost',
        headers: { host: `keycloak.localhost:${port}` },
        rejectUnauthorized: false,
      },
      (response) => {
        let body = '';
        response.setEncoding('utf8');
        response.on('data', (chunk) => {
          body += chunk;
        });
        response.on('end', () => resolve(response.statusCode === 200 ? body : ''));
      },
    );
    request.setTimeout(2_000, () => request.destroy());
    request.on('error', () => resolve(''));
  });
}

async function assertIssuer(origin, port) {
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    abort.signal.throwIfAborted();
    const body = await readHttps(port);
    if (body) {
      const document = JSON.parse(body);
      if (document.issuer !== `${origin}/realms/e2e`) {
        throw new Error(`Keycloak issuer is ${document.issuer}`);
      }
      return;
    }
    await setTimeout(1_000, undefined, { signal: abort.signal });
  }
  throw new Error('Keycloak did not publish its discovery document');
}

async function main() {
  if (!process.env.Seed__AdminPassword || process.env.Seed__AdminPassword.length < 12) {
    throw new Error('Supply Seed__AdminPassword (at least 12 characters) in the test environment');
  }
  if (
    !process.env.ECLIPSVAULT_KEK ||
    Buffer.from(process.env.ECLIPSVAULT_KEK, 'base64').length !== 32
  ) {
    throw new Error('Supply ECLIPSVAULT_KEK (base64-encoded 32 bytes) in the test environment');
  }
  // A fresh signing identity for the disposable vault. It goes straight to the child environment;
  // private key material is never logged or written to test artifacts.
  const { privateKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  // Chosen before the proxy starts, so the identity provider's issuer and the vault's redirect
  // URL can be written into the realm. Both are this port: the provider has its own hostname.
  const httpsPort = await freeLoopbackPort();
  const baseURL = `https://localhost:${httpsPort}`;
  const idpOrigin = `https://keycloak.localhost:${httpsPort}`;
  const generated = `${tmpdir()}/${project}`;
  const realmFile = `${generated}/realm.json`;
  const caBundle = `${generated}/ca-bundle.crt`;
  const idpPassword = `E2e-${randomBytes(24).toString('hex')}`;
  const env = {
    ...process.env,
    E2E_TEST_RESULTS: fileURLToPath(testResults),
    E2E_IMAGE: image,
    // Lets a spec reach this run's own disposable database, for state no production screen can create.
    E2E_COMPOSE_PROJECT: project,
    E2E_REDIS_PASSWORD: randomBytes(24).toString('hex'),
    E2E_SQL_PASSWORD: `Sa1!${randomBytes(24).toString('hex')}`,
    E2E_PROXY_IP: '127.0.0.1', // Replaced with the proxy's actual address before starting the vault.
    E2E_HTTPS_PORT: String(httpsPort),
    E2E_BASE_URL: baseURL,
    E2E_SSO_AUTHORITY: `${idpOrigin}/realms/e2e`,
    E2E_SSO_CLIENT_SECRET: randomBytes(24).toString('hex'),
    E2E_SSO_PASSWORD: idpPassword,
    E2E_SSO_OTP_SECRET: base32Secret(),
    E2E_KEYCLOAK_ADMIN_PASSWORD: randomBytes(24).toString('hex'),
    E2E_REALM_FILE: realmFile,
    E2E_CA_BUNDLE: caBundle,
    ECLIPSVAULT_AUDIT_SIGNING_KEY: privateKey
      .export({ type: 'pkcs8', format: 'der' })
      .toString('base64'),
  };
  const composeArgs = ['compose', '--project-name', project, '--file', 'e2e/compose.yml'];
  const compose = (args, options) => checked('docker', [...composeArgs, ...args], env, options);
  await mkdir(output, { recursive: true });
  let builtImage = false;
  try {
    await mkdir(generated, { recursive: true });
    // Compose interpolates the bundle path before the proxy has a certificate. The file has to exist
    // then; the vault is not started until the real bundle replaces it.
    await writeFile(caBundle, '', { mode: 0o644 });
    await writeRealm(realmFile, {
      baseURL,
      clientSecret: env.E2E_SSO_CLIENT_SECRET,
      idpPassword,
      otpSecret: env.E2E_SSO_OTP_SECRET,
    });
    if (!process.env.E2E_IMAGE) {
      await checked('docker', ['build', '--tag', image, root], env);
      builtImage = true;
    }
    // Discover the address Docker assigned instead of trusting arbitrary forwarded headers or
    // reserving a fixed subnet that could collide with another developer's network.
    await compose([
      'up',
      '-d',
      '--wait',
      '--wait-timeout',
      '300',
      'sql',
      'proxy',
      'smtp',
      'redis',
      'keycloak',
    ]);
    const proxyId = await compose(['ps', '-q', 'proxy'], { capture: true });
    env.E2E_PROXY_IP = await checked(
      'docker',
      ['inspect', '--format', '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}', proxyId],
      env,
      { capture: true },
    );
    const address = await compose(['port', 'proxy', String(httpsPort)], { capture: true });
    if (address.split(':').at(-1) !== String(httpsPort)) {
      throw new Error(`Proxy published ${address}, expected port ${httpsPort}`);
    }
    await trustProxyCertificate(compose, caBundle);
    await configureIdpAuthenticationReferences(compose);
    await assertIssuer(idpOrigin, httpsPort);
    await compose(['up', '-d', 'vault']);
    await waitForVault(env.E2E_BASE_URL);

    // A failed runner must never be mistaken for the previous invocation's expected failures.
    if (probes) await rm(new URL('results.json', output), { force: true });
    // Spec names after `npm run e2e --` narrow an ordinary run; guard probes always run whole.
    const specs = probes ? [] : process.argv.slice(2);
    const result = await run(
      process.execPath,
      ['node_modules/@playwright/test/cli.js', 'test', ...specs],
      {
        ...env,
        E2E_VERIFY_GUARDS: probes ? '1' : '0',
      },
    );
    if (probes) {
      verifyGuardFailures(
        JSON.parse(await readFile(new URL('results.json', output), 'utf8')),
        result.code,
      );
    } else if (result.code !== 0) {
      throw new Error(
        `Playwright failed (exit ${result.code}); see test-results and playwright-report`,
      );
    }
  } finally {
    try {
      try {
        const logs = await compose(['logs', '--no-color'], { capture: true, cleanup: true });
        await writeFile(new URL('docker.log', output), logs);
      } finally {
        // These containers belong to this run's unique project. Capture their anonymous image
        // volumes explicitly so cleanup never asks Compose to remove an unbounded set of volumes.
        const containers = await compose(['ps', '--all', '--quiet'], {
          capture: true,
          cleanup: true,
        });
        const volumes = new Set();
        if (containers) {
          const mounts = await checked(
            'docker',
            ['inspect', '--format', '{{json .Mounts}}', ...containers.split(/\s+/)],
            env,
            { capture: true, cleanup: true },
          );
          for (const line of mounts.split('\n')) {
            for (const mount of JSON.parse(line)) {
              if (mount.Type === 'volume') volumes.add(mount.Name);
            }
          }
        }
        await compose(['down', '--remove-orphans'], { cleanup: true });
        if (volumes.size)
          await checked('docker', ['volume', 'rm', ...volumes], env, { cleanup: true });
        if (builtImage) await checked('docker', ['image', 'rm', image], env, { cleanup: true });
      }
    } finally {
      // The rendered realm holds this run's identity-provider secret and passwords.
      await rm(generated, { recursive: true, force: true });
    }
  }
}

main().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
