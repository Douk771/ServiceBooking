// ARCHITECTURE_CYCLE31.md §31.7 — the ONE list of ezbook sources goods is allowed to import (ESLint) and that the goods
// Tailwind build scans (content). Both configs are generated from here; the guard test
// (goods/src/sharedSources.guard.test.ts) proves the list is complete.
export const GOODS_SHARED_EZBOOK_PAGES = [
  'LegalDocumentPage',
  'SubjectRequestPage',
  'ConsentsPage',
  'LoginPage',
  'RegisterPage',
  'NoticesPage',
  'BillingPage',
  'owner/NotificationsSection',
] // paths under src/pages, no extension

export const GOODS_SCANNED_EZBOOK_DIRS = ['components', 'utils', 'hooks'] // under src/, scanned by goods Tailwind
export const GOODS_MARKUP_FREE_EZBOOK_DIRS = ['api', 'store', 'types'] // under src/, must contain no .tsx
// Individual src/ files outside the dirs above (found by the guard on its first run, §31.7.3 item 5).
export const GOODS_SHARED_EZBOOK_FILES = ['queryClient.ts', 'pages/billingPageHelpers.ts']

export function goodsTailwindContent() {
  return [
    './goods/index.html',
    './goods/src/**/*.{js,ts,jsx,tsx}',
    ...GOODS_SCANNED_EZBOOK_DIRS.map((d) => `./src/${d}/**/*.{js,ts,jsx,tsx}`),
    ...GOODS_SHARED_EZBOOK_PAGES.map((p) => `./src/pages/${p}.tsx`),
    ...GOODS_SHARED_EZBOOK_FILES.map((f) => `./src/${f}`),
  ]
}

export function goodsAllowedPagesRegex() {
  const alt = `(${GOODS_SHARED_EZBOOK_PAGES.join('|')})`
  return `(^@/pages/(?!${alt}$))|(src/pages/(?!${alt}$))`
}
