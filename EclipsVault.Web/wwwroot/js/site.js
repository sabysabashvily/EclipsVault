// EclipsVault UI behaviours. CSP forbids inline scripts, so every interaction is
// wired here through data attributes:
//   form[data-confirm] / a[data-confirm]  → custom confirm dialog before proceeding
//   [data-copy="#selector"]               → copy target's text to the clipboard
//   [data-print]                          → print the current page (CSP forbids inline onclick)
//   [data-flash]                          → dismissible, auto-hiding toast
//   input[data-filter="#id"]              → live row filter for the referenced table
//   tr[data-href]                         → make a whole table row clickable
//   input[data-reveal]                    → adds a show/hide eye button inside the field
//   input[data-strength]                  → adds a live strength meter below the field
//   [data-command-source]                 → containers the ⌘K palette harvests its links from
//   [data-command-open]                    → opens the ⌘K palette (the visible way in)
//   [data-command-chord]                   → shows ⌘K, rewritten to Ctrl K off the Mac
document.addEventListener('DOMContentLoaded', () => {
    wireConfirms();
    wireCopyButtons();
    wirePrintButtons();
    wireFlashes();
    wireTableFilters();
    wireRowLinks();
    wireAutoSubmit();
    wireMenus();
    wirePasskeys();
    wirePasswordBreachCheck();
    wireRevealToggles();
    wirePasswordStrength();
    wireCommandPalette();
    wireThemeToggle();
});

// --- Custom confirm dialog (replaces window.confirm for an Apple-style modal) ---
function confirmDialog(message) {
    return new Promise((resolve) => {
        const backdrop = document.createElement('div');
        backdrop.className = 'modal-backdrop';
        backdrop.innerHTML = `
            <div class="modal" role="alertdialog" aria-modal="true" aria-label="Please confirm">
                <h3>Please confirm</h3>
                <p></p>
                <div class="modal-actions">
                    <button type="button" class="button" data-cancel>Cancel</button>
                    <button type="button" class="button danger" data-ok>Continue</button>
                </div>
            </div>`;
        backdrop.querySelector('p').textContent = message;
        document.body.appendChild(backdrop);

        const close = (result) => {
            backdrop.remove();
            document.removeEventListener('keydown', onKey);
            resolve(result);
        };
        const onKey = (e) => {
            if (e.key === 'Escape') close(false);
            if (e.key === 'Enter') close(true);
        };

        backdrop.querySelector('[data-ok]').addEventListener('click', () => close(true));
        backdrop.querySelector('[data-cancel]').addEventListener('click', () => close(false));
        backdrop.addEventListener('click', (e) => { if (e.target === backdrop) close(false); });
        document.addEventListener('keydown', onKey);
        backdrop.querySelector('[data-ok]').focus();
    });
}

function wireConfirms() {
    // Forms: intercept submit.
    document.querySelectorAll('form[data-confirm]').forEach((form) => {
        form.addEventListener('submit', (event) => {
            if (form.dataset.confirmed === 'true') return;
            event.preventDefault();
            confirmDialog(form.dataset.confirm).then((ok) => {
                if (ok) { form.dataset.confirmed = 'true'; form.submit(); }
            });
        });
    });

    // Links (e.g. opening a honey-token decoy): intercept navigation.
    document.querySelectorAll('a[data-confirm]').forEach((link) => {
        link.addEventListener('click', (event) => {
            event.preventDefault();
            confirmDialog(link.dataset.confirm).then((ok) => {
                if (ok) window.location.href = link.href;
            });
        });
    });
}

function wireCopyButtons() {
    document.querySelectorAll('[data-copy]').forEach((button) => {
        button.addEventListener('click', async () => {
            const target = document.querySelector(button.dataset.copy);
            if (!target) return;
            try {
                await navigator.clipboard.writeText(target.textContent.trim());
                const original = button.textContent;
                button.textContent = 'Copied ✓';
                button.classList.add('copied');
                setTimeout(() => {
                    button.textContent = original;
                    button.classList.remove('copied');
                }, 1600);
            } catch {
                button.textContent = 'Copy failed';
            }
        });
    });
}

function wirePrintButtons() {
    document.querySelectorAll('[data-print]').forEach((button) => {
        button.addEventListener('click', () => window.print());
    });
}

function wireFlashes() {
    document.querySelectorAll('[data-flash]').forEach((flash) => {
        flash.querySelector('[data-flash-close]')?.addEventListener('click', () => flash.remove());
        setTimeout(() => {
            flash.classList.add('flash-hide');
            setTimeout(() => flash.remove(), 400);
        }, 6000);
    });
}

function wireTableFilters() {
    document.querySelectorAll('input[data-filter]').forEach((input) => {
        const table = document.querySelector(input.dataset.filter);
        if (!table) return;
        input.addEventListener('input', () => {
            const query = input.value.trim().toLowerCase();
            table.querySelectorAll('tbody tr').forEach((row) => {
                row.hidden = query !== '' && !row.textContent.toLowerCase().includes(query);
            });
        });
    });
}

function wireRowLinks() {
    document.querySelectorAll('tr[data-href]').forEach((row) => {
        row.addEventListener('click', (event) => {
            // Let real controls inside the row do their own thing.
            if (event.target.closest('a, button, form, input, details')) return;
            window.location.href = row.dataset.href;
        });
    });
}

// A form marked data-autosubmit submits as soon as the referenced input changes
// (used for the "Upload image" file picker so there is no separate submit button).
function wireAutoSubmit() {
    document.querySelectorAll('form[data-autosubmit]').forEach((form) => {
        const input = document.querySelector(form.dataset.autosubmit);
        input?.addEventListener('change', () => {
            if (input.value) form.submit();
        });
    });
}

// Dismiss any open <details> menu — clicking away, or Escape.
//
// The selector lists every menu flavour rather than matching bare `details`, because the nav
// groups in the sidebar are <details> too and collapsing someone's open nav the moment they
// click the page would be a poltergeist. New menu? Add it here; the alternative is a menu that
// opens fine and then will not go away, which is exactly how .account-menu shipped its first
// draft.
const MENU_SELECTOR = 'details.row-menu[open], details.account-menu[open]';

function wireMenus() {
    const closeAll = (predicate) =>
        document.querySelectorAll(MENU_SELECTOR).forEach((menu) => {
            if (predicate(menu)) menu.removeAttribute('open');
        });

    document.addEventListener('click', (event) => closeAll((menu) => !menu.contains(event.target)));
    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape') return;
        // Give focus back to the control that opened it, or Escape strands the keyboard at the
        // top of the document.
        closeAll((menu) => {
            if (menu.contains(document.activeElement)) menu.querySelector('summary')?.focus();
            return true;
        });
    });
}

// Whether ⌘ is a key this keyboard actually has.
const IS_APPLE = /Mac|iPhone|iPad|iPod/.test(navigator.platform || navigator.userAgent);

// --- Theme toggle ----------------------------------------------------------------
// The server stamps <html data-theme> from the EclipsVault.Theme cookie so the first
// paint is already correct (no flash). Clicking the toggle flips it live and rewrites
// the cookie so the choice sticks on the next load.
function wireThemeToggle() {
    document.querySelectorAll('[data-theme-toggle]').forEach((button) => {
        button.addEventListener('click', () => {
            const next = document.documentElement.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
            document.documentElement.setAttribute('data-theme', next);
            document.cookie = `EclipsVault.Theme=${next}; path=/; max-age=31536000; SameSite=Strict; Secure`;
            labelThemeToggles();
        });
    });
    labelThemeToggles();
}

// Name the destination, not the mechanism. A menu row saying "Switch appearance" makes you click
// it to find out what happens; "Switch to light" has already told you. Only the menu row carries
// a label — the icon-only toggles say it with the icon.
function labelThemeToggles() {
    const next = document.documentElement.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
    document.querySelectorAll('[data-theme-label]').forEach((el) => {
        el.textContent = `Switch to ${next}`;
    });
}

// --- WebAuthn / passkeys ---------------------------------------------------------
// [data-passkey-register] registers a new authenticator for the signed-in user;
// [data-passkey-login] performs a passwordless sign-in. Both talk to the server over
// fetch(), converting the base64url fields the JSON API uses to/from the ArrayBuffers
// the WebAuthn browser API expects.
function wirePasskeys() {
    const registerButton = document.querySelector('[data-passkey-register]');
    const loginButton = document.querySelector('[data-passkey-login]');

    if (registerButton) {
        if (!window.PublicKeyCredential) return disablePasskeyUi(registerButton);
        registerButton.addEventListener('click', () => registerPasskey(registerButton));
    }
    if (loginButton) {
        if (!window.PublicKeyCredential) return disablePasskeyUi(loginButton);
        loginButton.addEventListener('click', () => loginWithPasskey(loginButton));
    }
}

async function registerPasskey(button) {
    const status = document.querySelector('[data-passkey-status]');
    const nickname = document.querySelector(button.dataset.nickname || '#passkey-nickname');
    try {
        setPasskeyStatus(status, 'Follow the prompt on your device…', false);
        button.disabled = true;

        const options = await passkeyPost('/Profile/PasskeyRegisterBegin').then(readJson);
        options.challenge = base64urlToBuffer(options.challenge);
        options.user.id = base64urlToBuffer(options.user.id);
        options.excludeCredentials = (options.excludeCredentials || []).map(withBufferId);

        const credential = await navigator.credentials.create({ publicKey: options });
        const result = await passkeyPost('/Profile/PasskeyRegisterComplete', {
            nickname: nickname ? nickname.value : '',
            credential: {
                id: credential.id,
                clientDataJSON: bufferToBase64url(credential.response.clientDataJSON),
                attestationObject: bufferToBase64url(credential.response.attestationObject)
            }
        }).then(readJson);

        if (result.success) {
            window.location.reload();
        } else {
            setPasskeyStatus(status, result.error || 'Registration failed.', true);
            button.disabled = false;
        }
    } catch (err) {
        setPasskeyStatus(status, friendlyPasskeyError(err), true);
        button.disabled = false;
    }
}

async function loginWithPasskey(button) {
    const status = document.querySelector('[data-passkey-status]');
    const usernameInput = button.dataset.username ? document.querySelector(button.dataset.username) : null;
    try {
        setPasskeyStatus(status, 'Follow the prompt on your device…', false);
        button.disabled = true;

        const options = await passkeyPost('/Account/PasskeyLoginBegin', {
            username: usernameInput ? usernameInput.value : null,
            returnUrl: button.dataset.returnUrl || null
        }).then(readJson);
        options.challenge = base64urlToBuffer(options.challenge);
        options.allowCredentials = (options.allowCredentials || []).map(withBufferId);

        const assertion = await navigator.credentials.get({ publicKey: options });
        const result = await passkeyPost('/Account/PasskeyLoginComplete', {
            credential: {
                id: assertion.id,
                clientDataJSON: bufferToBase64url(assertion.response.clientDataJSON),
                authenticatorData: bufferToBase64url(assertion.response.authenticatorData),
                signature: bufferToBase64url(assertion.response.signature),
                userHandle: assertion.response.userHandle ? bufferToBase64url(assertion.response.userHandle) : null
            }
        }).then(readJson);

        if (result.success) {
            window.location.href = result.redirect;
        } else {
            setPasskeyStatus(status, result.error || 'Sign-in failed.', true);
            button.disabled = false;
        }
    } catch (err) {
        setPasskeyStatus(status, friendlyPasskeyError(err), true);
        button.disabled = false;
    }
}

// --- Live breached-password check ------------------------------------------------
// An input marked data-breach-check="<url>" is screened (debounced) against the
// compromised-password corpus once it reaches the minimum length; the verdict is shown
// in the element named by data-breach-status.
function wirePasswordBreachCheck() {
    document.querySelectorAll('input[data-breach-check]').forEach((input) => {
        const url = input.dataset.breachCheck;
        const status = document.querySelector(input.dataset.breachStatus);
        let timer;
        let seq = 0;
        input.addEventListener('input', () => {
            clearTimeout(timer);
            const value = input.value;
            if (value.length < 12) {
                setBreachStatus(status, '', false);
                return;
            }
            timer = setTimeout(async () => {
                const mine = ++seq;
                try {
                    const token = document.querySelector('input[name="__RequestVerificationToken"]');
                    const res = await fetch(url, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token ? token.value : '' },
                        body: JSON.stringify({ password: value })
                    });
                    if (!res.ok || mine !== seq) return;
                    const data = await res.json();
                    if (mine !== seq) return; // a newer keystroke already superseded this
                    setBreachStatus(status, data.compromised
                        ? '⚠ This password has appeared in known data breaches — choose a different one.'
                        : '✓ Not found in the breach corpus.', data.compromised);
                } catch {
                    // Network hiccup: stay silent; the server still screens on submit.
                }
            }, 400);
        });
    });
}

function setBreachStatus(el, message, isWarn) {
    if (!el) return;
    el.textContent = message;
    el.classList.toggle('status-error', !!isWarn);
    el.classList.toggle('status-ok', !isWarn && message !== '');
}

// --- Show/hide password ----------------------------------------------------------
// An input marked data-reveal gets an eye button inside it. Typing a long password blind
// and finding out it was wrong only after submitting is how people end up choosing shorter
// ones — the button is built here rather than in each view so no markup has to be repeated,
// and so it can never be added to a field that is not opted in.
function wireRevealToggles() {
    document.querySelectorAll('input[data-reveal]').forEach((input) => {
        const wrap = document.createElement('div');
        wrap.className = 'input-affix';
        input.parentNode.insertBefore(wrap, input);
        wrap.appendChild(input);

        const button = document.createElement('button');
        button.type = 'button';                 // never submits the form it lives in
        button.className = 'affix-button';
        button.setAttribute('aria-label', 'Show password');
        button.innerHTML = EYE_OPEN;
        wrap.appendChild(button);

        button.addEventListener('click', () => {
            const shown = input.type === 'text';
            input.type = shown ? 'password' : 'text';
            button.innerHTML = shown ? EYE_OPEN : EYE_CLOSED;
            button.setAttribute('aria-label', shown ? 'Show password' : 'Hide password');
            // Put the caret back where it was; toggling type resets it to the start.
            const end = input.value.length;
            input.focus();
            input.setSelectionRange(end, end);
        });
    });
}

const EYE_OPEN = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/></svg>';
const EYE_CLOSED = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12s3.6-7 10-7c2 0 3.8.7 5.2 1.6M22 12s-3.6 7-10 7c-2 0-3.8-.7-5.2-1.6"/><path d="m4 4 16 16"/></svg>';

// --- Password strength meter ------------------------------------------------------
// Deliberately advisory, not a gate. The vault enforces exactly two rules — at least 12
// characters, and not in the breach corpus (NIST 800-63B 5.1.1.2) — and those are the two
// shown as pass/fail. Everything else here is an estimate of how much guessing the password
// would cost, shown as a bar.
//
// It does NOT demand an uppercase letter and a symbol. The same NIST guidance this vault
// screens against advises against composition rules: they push people towards Password1!
// and away from length, which is what actually helps. A checklist insisting on a symbol
// would also be claiming a rule the server does not enforce — telling a user their password
// is invalid when the vault would accept it.
//
// Everything is computed in the browser. The breach check already talks to the server; there
// is no reason for the shape of a candidate password to make a second trip.
function wirePasswordStrength() {
    document.querySelectorAll('input[data-strength]').forEach((input) => {
        const meter = document.createElement('div');
        meter.className = 'strength';
        meter.hidden = true;
        meter.innerHTML = `
            <div class="strength-track" role="img">
                <span class="strength-fill"></span>
            </div>
            <p class="strength-label"></p>`;
        // After the affix wrapper if there is one, so the meter sits under the whole field.
        const anchor = input.closest('.input-affix') || input;
        anchor.parentNode.insertBefore(meter, anchor.nextSibling);

        const fill = meter.querySelector('.strength-fill');
        const label = meter.querySelector('.strength-label');
        const track = meter.querySelector('.strength-track');

        input.addEventListener('input', () => {
            const value = input.value;
            if (!value) { meter.hidden = true; return; }
            meter.hidden = false;

            const { score, text } = scorePassword(value);
            fill.style.width = `${(score + 1) * 20}%`;
            meter.dataset.score = String(score);
            label.textContent = text;
            track.setAttribute('aria-label', `Password strength: ${text}`);
        });
    });
}

// A rough guessing-cost estimate: variety widens the alphabet, length multiplies it, and the
// obvious shapes (one repeated character, a straight run off the keyboard) are discounted
// because an attacker tries those first. Not a substitute for the server's corpus screen —
// that catches the passwords that are weak for reasons no formula can see.
function scorePassword(value) {
    let alphabet = 0;
    if (/[a-z]/.test(value)) alphabet += 26;
    if (/[A-Z]/.test(value)) alphabet += 26;
    if (/[0-9]/.test(value)) alphabet += 10;
    if (/[^A-Za-z0-9]/.test(value)) alphabet += 33;

    let bits = value.length * Math.log2(Math.max(alphabet, 2));

    const distinct = new Set(value).size;
    if (distinct <= 2) bits *= 0.35;                       // "aaaaaaaaaaaa"
    else if (distinct / value.length < 0.4) bits *= 0.7;   // heavy repetition
    if (/^(?:0123456789|abcdefghij|qwertyuiop)/i.test(value)) bits *= 0.4;

    if (value.length < 12) {
        return { score: 0, text: `Too short — ${12 - value.length} more character${value.length === 11 ? '' : 's'} needed` };
    }
    if (bits < 60) return { score: 1, text: 'Weak — predictable for its length' };
    if (bits < 80) return { score: 2, text: 'Fair' };
    if (bits < 110) return { score: 3, text: 'Strong' };
    return { score: 4, text: 'Very strong' };
}

// --- Command palette (⌘K / Ctrl+K) ------------------------------------------------
// Two sources, and neither of them is a list maintained here.
//
// Navigation is harvested from the sidebar's own <a> elements. That is not a shortcut — it is
// what makes the palette safe. The sidebar is already rendered per-caller (an admin-only entry
// is simply not in the DOM for anyone else), so reading it back means the palette offers exactly
// the routes this user was already offered. A hardcoded command list here would be a second
// copy of the navigation *and* of its authorization, and the copy would rot: the day someone
// adds an admin page, the palette would happily show it to everyone.
//
// Secrets come from /Secrets/Search, which runs the same ABAC enumeration filter as the Secrets
// list. See the comment on SecretsController.Search — a name is a disclosure, and the palette
// is not allowed to be a way around that.
function wireCommandPalette() {
    // Every marked container, not just the nav: Profile lives in the account menu at the foot of
    // the sidebar, and harvesting only the nav would leave it out of the palette entirely.
    const sources = [...document.querySelectorAll('[data-command-source]')];
    if (!sources.length) return;   // signed-out pages have no shell and nothing to command

    const searchUrl = sources.find((s) => s.dataset.commandSearch)?.dataset.commandSearch;
    const commands = sources
        .flatMap((s) => [...s.querySelectorAll('a[href]')])
        .map((a) => ({
            kind: 'Go to',
            label: a.textContent.trim(),
            url: a.getAttribute('href'),
        }))
        .filter((c) => c.label);

    let backdrop = null;
    let opener = null;
    let seq = 0;
    let timer;

    document.addEventListener('keydown', (e) => {
        if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
            e.preventDefault();
            backdrop ? close() : open();
        }
    });

    // The visible way in. Without this the palette is a rumour.
    document.querySelectorAll('[data-command-open]').forEach((trigger) => {
        trigger.addEventListener('click', () => { if (!backdrop) open(); });
    });

    // ⌘ is a Mac key. Telling a Windows user to press ⌘K is telling them to press a key their
    // keyboard does not have, which is worse than saying nothing.
    if (!IS_APPLE) {
        document.querySelectorAll('[data-command-chord]').forEach((el) => { el.textContent = 'Ctrl K'; });
    }

    function close() {
        if (!backdrop) return;
        backdrop.remove();
        backdrop = null;
        clearTimeout(timer);
        seq++;                       // any in-flight search result is now stale

        // Hand focus back where it came from. Dismissing a dialog that dumps focus on <body>
        // sends a keyboard user back to the top of the document to find their place again.
        if (opener && document.contains(opener)) opener.focus();
        opener = null;
    }

    function open() {
        opener = document.activeElement;
        backdrop = document.createElement('div');
        backdrop.className = 'modal-backdrop palette-backdrop';
        backdrop.innerHTML = `
            <div class="palette" role="dialog" aria-modal="true" aria-label="Command palette">
                <div class="palette-field">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
                    <input type="text" class="palette-input" role="combobox" aria-expanded="true"
                           aria-controls="palette-list" aria-autocomplete="list"
                           placeholder="Search secrets, or jump to a page…" autocomplete="off" spellcheck="false" />
                </div>
                <ul class="palette-list" id="palette-list" role="listbox" aria-label="Results"></ul>
                <p class="palette-hint">
                    <span><kbd>↑</kbd><kbd>↓</kbd> to move</span>
                    <span><kbd>↵</kbd> to open</span>
                    <span><kbd>esc</kbd> to close</span>
                </p>
            </div>`;
        document.body.appendChild(backdrop);

        const input = backdrop.querySelector('.palette-input');
        const list = backdrop.querySelector('.palette-list');
        let items = [];
        let active = 0;

        backdrop.addEventListener('click', (e) => { if (e.target === backdrop) close(); });

        function render(results) {
            items = results;
            active = 0;
            list.innerHTML = '';

            if (!results.length) {
                const empty = document.createElement('li');
                empty.className = 'palette-empty';
                empty.textContent = input.value.trim()
                    ? 'Nothing matches that.'
                    : 'Type to search.';
                list.appendChild(empty);
                return;
            }

            results.forEach((r, i) => {
                const li = document.createElement('li');
                li.className = i === 0 ? 'palette-item is-active' : 'palette-item';
                li.setAttribute('role', 'option');
                li.setAttribute('aria-selected', String(i === 0));

                const kind = document.createElement('span');
                kind.className = 'palette-kind';
                kind.textContent = r.kind;

                // textContent, never innerHTML: a secret's name is user-supplied data and this is
                // the one place it would be trivially easy to hand it to the parser.
                const label = document.createElement('span');
                label.className = 'palette-label';
                label.textContent = r.label;

                li.append(kind, label);

                if (r.meta) {
                    const meta = document.createElement('span');
                    meta.className = 'palette-meta';
                    meta.textContent = r.meta;
                    li.appendChild(meta);
                }
                if (r.sensitivity) {
                    const badge = document.createElement('span');
                    badge.className = `badge sens-${r.sensitivity}`;
                    badge.textContent = r.sensitivityName;
                    li.appendChild(badge);
                }

                li.addEventListener('click', () => activate(i));
                li.addEventListener('mousemove', () => highlight(i));
                list.appendChild(li);
            });
        }

        function highlight(i) {
            if (i === active) return;
            active = i;
            [...list.children].forEach((li, n) => {
                li.classList.toggle('is-active', n === i);
                li.setAttribute('aria-selected', String(n === i));
            });
            list.children[i]?.scrollIntoView({ block: 'nearest' });
        }

        function activate(i) {
            const item = items[i];
            if (!item) return;
            close();
            window.location.assign(item.url);
        }

        function localMatches(q) {
            const needle = q.toLowerCase();
            return commands.filter((c) => c.label.toLowerCase().includes(needle));
        }

        async function search() {
            const q = input.value.trim();
            const local = localMatches(q);

            // Pages resolve instantly and offline; secrets need the server. Show what is already
            // known rather than making the whole palette wait on a round trip.
            render(q ? local : commands);
            if (q.length < 2 || !searchUrl) return;

            const mine = ++seq;
            try {
                const res = await fetch(`${searchUrl}?q=${encodeURIComponent(q)}`, {
                    headers: { 'Accept': 'application/json' }
                });
                if (!res.ok || mine !== seq || !backdrop) return;
                const secrets = await res.json();
                if (mine !== seq || !backdrop) return;   // a later keystroke already won

                render([
                    ...secrets.map((s) => ({
                        kind: 'Secret',
                        label: s.name,
                        meta: `${s.project} · ${s.environment}`,
                        sensitivity: s.sensitivity,
                        sensitivityName: s.sensitivityName,
                        url: s.url,
                    })),
                    ...local,
                ]);
            } catch {
                // Offline or refused: the navigation half still works, so leave it standing.
            }
        }

        input.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(search, 160); });

        input.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') { e.preventDefault(); close(); }
            else if (e.key === 'ArrowDown') { e.preventDefault(); highlight(Math.min(active + 1, items.length - 1)); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); highlight(Math.max(active - 1, 0)); }
            else if (e.key === 'Enter') { e.preventDefault(); activate(active); }
            // The input is the dialog's only focusable element, so holding it is the whole trap:
            // without this, Tab walks focus out into the page behind an open modal.
            else if (e.key === 'Tab') { e.preventDefault(); }
        });

        render(commands);
        input.focus();
    }
}

function passkeyPost(url, body) {
    const token = document.querySelector('input[name="__RequestVerificationToken"]');
    return fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token ? token.value : ''
        },
        body: body === undefined ? undefined : JSON.stringify(body)
    });
}

async function readJson(response) {
    if (!response.ok) throw new Error('The server rejected the request.');
    return response.json();
}

function withBufferId(descriptor) {
    return Object.assign({}, descriptor, { id: base64urlToBuffer(descriptor.id) });
}

function setPasskeyStatus(el, message, isError) {
    if (!el) return;
    el.textContent = message;
    el.classList.toggle('status-error', isError);
    el.classList.toggle('status-ok', !isError && message !== '');
}

function disablePasskeyUi(button) {
    button.disabled = true;
    button.title = 'This browser does not support passkeys.';
}

function friendlyPasskeyError(err) {
    if (err && (err.name === 'NotAllowedError' || err.name === 'AbortError')) {
        return 'The passkey prompt was dismissed or timed out.';
    }
    return (err && err.message) || 'Something went wrong with the passkey.';
}

function base64urlToBuffer(value) {
    const base64 = value.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '==='.slice((base64.length + 3) % 4);
    const binary = atob(padded);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes.buffer;
}

function bufferToBase64url(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
