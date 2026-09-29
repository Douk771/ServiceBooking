import base from './tailwind.config.js'

// goods.ezbook.ru (ARCHITECTURE_CYCLE23.md §399.1): the ezbook palette/fonts/shadows are the single
// design system (preset), only the scanned files differ — goods sources plus the shared components and
// the five shared pages goods mounts (§399.2).
/** @type {import('tailwindcss').Config} */
export default {
  presets: [base],
  content: [
    './goods/index.html',
    './goods/src/**/*.{js,ts,jsx,tsx}',
    './src/components/**/*.{js,ts,jsx,tsx}',
    './src/pages/{LegalDocumentPage,SubjectRequestPage,ConsentsPage,LoginPage,RegisterPage}.tsx',
  ],
}
