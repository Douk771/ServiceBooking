// ARCHITECTURE_CYCLE37.md §37.14.1 — generalisation of goods-shared-sources.js (ARCHITECTURE_CYCLE31.md §31.7): the ONE list of
// ezbook sources that the secondary apps (goods, dom) are allowed to import (ESLint) and that their Tailwind builds scan
// (content). Every app config is generated from here; each app's guard test (goods/src/sharedSources.guard.test.ts,
// dom/src/sharedSources.guard.test.ts) proves the list is complete.
export const SHARED_EZBOOK_PAGES = [
  'LegalDocumentPage',
  'SubjectRequestPage',
  'ConsentsPage',
  'LoginPage',
  'RegisterPage',
  'NoticesPage',
  'BillingPage',
] // paths under src/pages, no extension

export const SCANNED_EZBOOK_DIRS = ['components', 'utils', 'hooks'] // under src/, scanned by the app Tailwind builds
export const MARKUP_FREE_EZBOOK_DIRS = ['api', 'store', 'types'] // under src/, must contain no .tsx
// Individual src/ files outside the dirs above (found by the guard on its first run, §31.7.3 item 5).
export const SHARED_EZBOOK_FILES = ['queryClient.ts', 'pages/billingPageHelpers.ts']

export const SECONDARY_APPS = ['goods', 'dom', 'bani']

function assertApp(app) {
  if (!SECONDARY_APPS.includes(app)) throw new Error(`unknown app "${app}" (expected one of ${SECONDARY_APPS.join(', ')})`)
}

/** Tailwind `content` of a secondary app: its own sources plus everything shared from ezbook. */
export function appTailwindContent(app) {
  assertApp(app)
  return [
    `./${app}/index.html`,
    `./${app}/src/**/*.{js,ts,jsx,tsx}`,
    ...SCANNED_EZBOOK_DIRS.map((d) => `./src/${d}/**/*.{js,ts,jsx,tsx}`),
    ...SHARED_EZBOOK_PAGES.map((p) => `./src/pages/${p}.tsx`),
    ...SHARED_EZBOOK_FILES.map((f) => `./src/${f}`),
  ]
}

/** ESLint `no-restricted-imports` regex: any ezbook page except the shared ones. Same for every secondary app. */
export function appAllowedPagesRegex() {
  const alt = `(${SHARED_EZBOOK_PAGES.join('|')})`
  return `(^@/pages/(?!${alt}$))|(src/pages/(?!${alt}$))`
}

// ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.5, §44.6.1 — about.ezbook.ru is a static page, not a "secondary app with shared pages":
// it is deliberately NOT in SECONDARY_APPS (appTailwindContent('about') still throws). Its allowed ezbook imports are this whitelist.
export const ABOUT_SHARED_SOURCES = [
  'src/components/ui/Icon.tsx',
  'src/components/landing/classes.ts',
  'src/utils/publicSites.ts',
]

/** Tailwind `content` of `about`: its own sources plus the whitelist only (keeps the CSS small). */
export function aboutTailwindContent() {
  return ['./about/index.html', './about/src/**/*.{js,ts,jsx,tsx}', ...ABOUT_SHARED_SOURCES.map((f) => `./${f}`)]
}
