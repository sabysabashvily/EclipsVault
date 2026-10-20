# Coding standards

**The bar:** a person reads any file in this repo (code, tests, templates, CSS, scripts, docs, commit
messages) and understands it without hesitation. The reviewer asks "can I understand this quickly?",
not "how many lines is it?" (ADR-0020).

Tools enforce everything they can, and CI fails on it. This file covers only what tools cannot check.
Each rule gives its reason and an example from this repo.

## Enforced by tools

These rules are listed here only so you know they exist. Fix their failures; don't argue with them.

- **C# formatting and `using` order:** the root `.editorconfig`, with `EnforceCodeStyleInBuild`. Fix with `dotnet format`.
- **TypeScript, HTML, CSS, JSON and YAML in `EclipsVault.AngularUi`:** Prettier with `.prettierrc`. Fix with `npm run format`.
- **Analyzer and ESLint findings:** warnings fail the build and `npm run lint`.
- **E2E locators:** `eslint-plugin-playwright` `no-raw-locators` (see "E2E tests" below).
- **Commit subjects:** 72 characters or fewer, checked on every PR.
- **Generated files** (`schema.generated.ts`) are never edited or formatted by hand.

## Names

**Names say what a thing is or does, so the reader never has to look it up.**
Why: an unclear name makes every reader re-derive the meaning, every time.

```ts
// ❌ What is dd number 1?
await expect(you.locator('dd').nth(1)).toHaveText('GLOBAL');
// ✅
await expect(myAccess.project).toHaveText('GLOBAL');
```

**Name a value whose meaning isn't obvious.**
Why: a bare number or colour hides the decision behind it.

```ts
// ❌
if (span < 7 * day) return `${Math.trunc(span / day)}d ago`;
// ✅
const week = 7 * day;
if (span < week) return `${Math.trunc(span / day)}d ago`;
```

## One job each

**A function, test or helper does one thing you can sum up in one sentence.** If the summary needs
"and", split it.
Why: a reader holds one idea at a time. `verifyMyAccess` checks the title, identity, network, grid,
inline styles, colours and a screenshot, so nobody can tell what it is for without reading every line.

## Comments

**A comment explains why, never what.** The code already says what.
Why: a "what" comment repeats the code and goes stale; a "why" comment is the one thing the code can't say.

```ts
// ✅ Says something the code can't:
// Docker allocates the proxy's forwarded source address; it is not necessarily loopback.
expect(access.isTrustedNetwork).toBe(true);
```

**Code never mentions tickets, sessions or history** ("fixed in #58", "now also handles X").
Why: history belongs in commit messages and issues. In code it is stale the day it merges.

**Doc comments only where the name and signature don't already say it.**
Why: a mandatory doc comment on every member produces filler that hides the useful ones.

## Tests

**A test name is a sentence: a scenario plus its outcome.** C# keeps the underscore-sentence style.
Why: the list of names should read as a description of the behaviour.

```text
❌ Test2, works, handles_null
✅ Security_checkup_for_a_deleted_account_requires_signing_in_again
✅ test('signed out, My activity sends the browser to sign-in with its return URL', …)
```

**Assert what a user or caller observes, not how it is built.**
Why: a test that pins markup breaks on every redesign, even when nothing is wrong.

```ts
// ❌ Pins SVG geometry
expect(svgShapes).toEqual(['path:d=M9 4h6a1 1 0 0 1 1 1v1h2…']);
// ✅ Pins what a person sees
expect(screen.getByRole('heading', { name: 'No activity yet' })).toBeVisible();
```

**No logic in tests.** Write the expected values out instead of computing them.
Why: a test with `if` and loops needs its own test, and a reader has to run it in their head.

```ts
// ❌
const values = account === 'standard' && row < 3 ? ['Clearance too low', …] : ['Can open', …];
// ✅ The expectation is a literal table, one row per sensitivity.
const standardGrid = [
  ['Top Secret', 'Clearance too low', 'Clearance too low', 'Clearance too low'],
  ['Internal',   'Can open',          'Can open',          'Outside access hours'],
];
```

Repeating a few clear lines across tests is fine. Prefer that to a shared helper the reader must open.

## E2E tests

**Find elements by role, label or text, the way a person does.** Using CSS needs a comment saying why.
Why: CSS classes are styling details. Role and label locators survive a redesign and check accessibility for free.

**One spec file per page or flow, with a page object for each page.** The page object names the page's
parts (`myAccess.clearance`) and actions (`signIn()`). Assertions stay in the test.
Why: the page object decides once how to find each part, and every test reads as plain steps.

**Colour, computed-style and SVG checks only in tests whose title starts with `visual parity:`.**
Why: they exist only while the Legacy and Angular UIs must match, and the redesign deletes them.
The prefix makes them easy to find.

## Commit messages

**The subject is one sentence of 72 characters or fewer, describing the change in behaviour.** Then a
blank line and a body explaining why, when that isn't obvious.
Why: `git log --oneline` is how history gets read. Detail belongs in the body.

```text
❌ Expect a deleted or unknown account's self-service session to be signed out rather than stale, and give the scoping tests source addresses the sign-out budget tests do not exhaust.
✅ Sign out a deleted account's session instead of marking it stale

   The cookie validator now refuses a ticket for a deleted account, so the endpoints never run.
   The scoping tests move to source addresses the sign-out tests don't exhaust.
```

## Existing code

The tools reformatted the whole repo once, in commits listed in `.git-blame-ignore-revs`. Beyond that,
**a change brings every file it touches up to this standard.** Untouched files wait.

## Review

- **A different model from the author reviews readability:** Codex writes, Claude reviews, and the
  other way round. If that isn't possible, use the same model in a fresh session and say so in the report.
- **Every ticket report quotes 2–3 short excerpts of new code:** the most complex function, one test,
  and one page object or template. The owner reads them as the final readability check.
- **A change is approved once it clearly improves the code's overall health.** It doesn't need to be perfect.
