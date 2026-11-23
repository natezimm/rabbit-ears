// @ts-check
import eslint from '@eslint/js';
import angular from 'angular-eslint';
import tseslint from 'typescript-eslint';

// Formatting is Prettier's job; these rules catch mistakes and hold the
// project's Angular conventions (ears- prefix, OnPush, no Component suffix).
export default tseslint.config(
  { ignores: ['e2e/**/*.js', 'playwright-report/**', 'test-results/**'] },
  {
    files: ['**/*.ts'],
    extends: [eslint.configs.recommended, ...tseslint.configs.recommended, ...angular.configs.tsRecommended],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/component-selector': ['error', { type: 'element', prefix: 'ears', style: 'kebab-case' }],
      '@angular-eslint/directive-selector': ['error', { type: 'attribute', prefix: 'ears', style: 'camelCase' }],
      '@angular-eslint/component-class-suffix': ['error', { suffixes: [''] }],
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
  },
);
