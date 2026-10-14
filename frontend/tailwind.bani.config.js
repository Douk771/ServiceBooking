import base from './tailwind.config.js'
import { appTailwindContent } from './shared-sources.js'

// bani.ezbook.ru (ARCHITECTURE_CYCLE42.md §42.13.2): the ezbook palette/fonts/shadows are the single design system (preset),
// only the scanned files differ — bani sources plus the shared components and pages (list: shared-sources.js).
/** @type {import('tailwindcss').Config} */
export default {
  presets: [base],
  content: appTailwindContent('bani'),
}
