// @ts-check
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = defineConfig([
  {
    files: ['**/*.ts'],
    extends: [
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      // ADR-0013: no sanitization bypasses, including property references and destructuring.
      'no-restricted-properties': [
        'error',
        ...[
          'bypassSecurityTrustHtml',
          'bypassSecurityTrustStyle',
          'bypassSecurityTrustScript',
          'bypassSecurityTrustUrl',
          'bypassSecurityTrustResourceUrl',
        ].map((property) => ({
          property,
          message: 'Angular sanitization bypasses are forbidden (ADR-0013).',
        })),
      ],
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
    },
  },
  {
    files: ['src/app/ui-api/schema.generated.ts'],
    // Keep the generator's index signatures verbatim so regeneration stays reproducible.
    rules: { '@typescript-eslint/consistent-indexed-object-style': 'off' },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      // Inline templates are processed as HTML too. Cover both named and bracket member access.
      'no-restricted-syntax': [
        'error',
        {
          selector:
            ':matches(PropertyRead, SafePropertyRead)[name=/^bypassSecurityTrust/], :matches(KeyedRead, SafeKeyedRead)[key.value=/^bypassSecurityTrust/]',
          message: 'Angular sanitization bypasses are forbidden (ADR-0013).',
        },
      ],
    },
  },
]);
