import base from './tailwind.config.js'
import { appTailwindContent } from './shared-sources.js'

// dom.ezbook.ru (ARCHITECTURE_CYCLE37.md §37.14.1): the ezbook palette/fonts/shadows are the single design system (preset),
// only the scanned files differ — dom sources plus the shared components and pages (list: shared-sources.js).
/** @type {import('tailwindcss').Config} */
export default {
  presets: [base],
  content: appTailwindContent('dom'),
}
