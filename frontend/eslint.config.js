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
    // design_handoff_site_redesign/ is static reference markup/JS handed off by design, not app
    // source built by Vite — it isn't part of tsconfig's `include` either.
    ignores: ['dist/**', 'coverage/**', 'node_modules/**', 'design_handoff_site_redesign/**'],
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
    files: ['**/*.test.{ts,tsx}', 'src/test/setup.ts'],
    languageOptions: { globals: globals.node },
  },
  {
    // ARCHITECTURE_CYCLE9.md §105.9 — plain JS served verbatim from public/, not built by Vite, so it
    // needs the service-worker global scope (`self`, `caches`, `clients`) instead of the browser one.
    files: ['public/sw.js'],
    languageOptions: { globals: globals.serviceworker },
  },
  prettierConfig,
)
