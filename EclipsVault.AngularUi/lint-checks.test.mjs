import assert from 'node:assert/strict';
import { it } from 'node:test';
import { ESLint } from 'eslint';

const eslint = new ESLint();
const methods = [
  'bypassSecurityTrustHtml',
  'bypassSecurityTrustStyle',
  'bypassSecurityTrustScript',
  'bypassSecurityTrustUrl',
  'bypassSecurityTrustResourceUrl',
];

for (const method of methods) {
  it(`rejects ${method} in TypeScript, external templates and inline templates`, async () => {
    const expressions = [
      `sanitizer.${method}('probe')`,
      `sanitizer['${method}']('probe')`,
      `sanitizer?.${method}('probe')`,
      `sanitizer?.['${method}']('probe')`,
    ];
    const template = expressions
      .map((expression) => `<div [innerHTML]="${expression}"></div>`)
      .join('\n');
    const sources = [
      {
        filePath: 'src/app/lint-probe.ts',
        source: `export function probe(sanitizer: object) { ${expressions.join(';')}; const { ${method}: alias } = sanitizer; return alias; }`,
        rule: 'no-restricted-properties',
        count: 5,
      },
      {
        filePath: 'src/app/lint-probe.html',
        source: template,
        rule: 'no-restricted-syntax',
        count: 4,
      },
      {
        filePath: 'src/app/lint-probe.component.ts',
        source: `import { Component } from '@angular/core'; @Component({ selector: 'app-lint-probe', template: ${JSON.stringify(template)} }) export class LintProbe {}`,
        rule: 'no-restricted-syntax',
        count: 4,
      },
    ];
    for (const { filePath, source, rule, count } of sources) {
      const results = await eslint.lintText(source, { filePath });
      const messages = results.flatMap((result) => result.messages);
      assert.equal(
        messages.filter((message) => message.ruleId === rule && message.severity === 2).length,
        count,
        filePath,
      );
      assert.equal(
        messages.length,
        count,
        `Unexpected lint errors in ${filePath}: ${JSON.stringify(messages)}`,
      );
    }
  });
}

it('allows ordinary sanitization and safe template bindings', async () => {
  for (const [filePath, source] of [
    [
      'src/app/lint-probe.ts',
      "export function probe(sanitizer: object) { return sanitizer.sanitize(1, 'safe'); }",
    ],
    ['src/app/lint-probe.html', '<div [innerHTML]="safeHtml"></div>'],
  ]) {
    const results = await eslint.lintText(source, { filePath });
    assert.deepEqual(
      results.flatMap((result) => result.messages),
      [],
    );
  }
});
