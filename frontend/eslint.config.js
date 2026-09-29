import js from '@eslint/js'
import tseslint from 'typescript-eslint'
import react from 'eslint-plugin-react'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import globals from 'globals'
import prettierConfig from 'eslint-config-prettier'

// US-50 (T-F6). Rules are picked to describe the codebase's already-established conventions
// (CURRENT_STATE.md §6) rather than to introduce a new style: named exports (only App.tsx has a
// default export), Tailwind utility classes in JSX, explicit `describe`/`it`/`expect` imports in
// tests (globals: false — vitest.config.ts doesn't inject test globals). `eslint-config-prettier`
// is last in the array so it can turn off any formatting rule that would otherwise fight Prettier.
export default tseslint.config(
  {
    ignores: ['dist/**', 'dist-goods/**', 'coverage/**', 'node_modules/**'],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2022,
      globals: globals.browser,
    },
    plugins: {
      react,
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    settings: {
      react: { version: '18.3' },
    },
    rules: {
      ...react.configs.recommended.rules,
      ...react.configs['jsx-runtime'].rules, // React 18 JSX transform — no `import React` needed
      ...reactHooks.configs.recommended.rules,
      // App.tsx is the sole default export by convention; every other module exports named symbols
      // (CURRENT_STATE.md §6), which is exactly what this rule enforces for React Fast Refresh.
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      // Explicit `any` is used sparingly and deliberately in a few error-mapping utilities; keep it a
      // warning, not a build-breaking error, so it stays visible on review without blocking CI on
      // pre-existing patterns this cycle didn't touch.
      '@typescript-eslint/no-explicit-any': 'warn',
      '@typescript-eslint/no-unused-vars': ['warn', { argsIgnorePattern: '^_' }],
      'react/prop-types': 'off', // TypeScript already checks props
    },
  },
  {
    // ARCHITECTURE_CYCLE23.md §399.4 — import boundaries between the two frontends in one package.
    // goods may reuse shared modules from src/, but not ezbook's app shell or its own pages (except the
    // shared ones — cycle 24 adds BillingPage and owner/NotificationsSection); ezbook must never reach into goods/.
    files: ['goods/src/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            { group: ['@/App', '**/src/App', '../../src/App', '../src/App'], message: 'goods must not import the ezbook app shell.' },
            {
              regex: '(^@/pages/(?!(LegalDocumentPage|SubjectRequestPage|ConsentsPage|LoginPage|RegisterPage|NoticesPage|BillingPage|owner/NotificationsSection)$))|(src/pages/(?!(LegalDocumentPage|SubjectRequestPage|ConsentsPage|LoginPage|RegisterPage|NoticesPage|BillingPage|owner/NotificationsSection)$))',
              message: 'goods may import only LegalDocumentPage, SubjectRequestPage, ConsentsPage, LoginPage, RegisterPage, NoticesPage, BillingPage (cycle 24, ARCHITECTURE_CYCLE24.md §462.2) and owner/NotificationsSection (ChannelCard, §462.1) from ezbook pages.',
            },
          ],
        },
      ],
    },
  },
  {
    files: ['src/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': [
        'error',
        { patterns: [{ regex: '(^@goods/)|(/goods/)|(^goods/)', message: 'ezbook (src/) must not import from goods/.' }] },
      ],
    },
  },
  {
    files: ['**/*.test.{ts,tsx}', 'src/test/setup.ts'],
    languageOptions: { globals: globals.node },
  },
  {
    // Build helper scripts run in Node (frontend/scripts/*.mjs, e.g. merge-goods-dist.mjs).
    files: ['scripts/**/*.mjs'],
    languageOptions: { globals: globals.node },
  },
  {
    // ARCHITECTURE_CYCLE9.md §105.9 — plain JS served verbatim from public/, not built by Vite, so it
    // needs the service-worker global scope (`self`, `caches`, `clients`) instead of the browser one.
    files: ['public/sw.js', 'goods/public/sw.js'], // goods: ARCHITECTURE_CYCLE24.md §454
    languageOptions: { globals: globals.serviceworker },
  },
  prettierConfig,
)
