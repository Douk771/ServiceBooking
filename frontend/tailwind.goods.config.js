import base from './tailwind.config.js'
import { goodsTailwindContent } from './goods-shared-sources.js'

// goods.ezbook.ru (ARCHITECTURE_CYCLE23.md §399.1): the ezbook palette/fonts/shadows are the single
// design system (preset), only the scanned files differ — goods sources plus the shared components and
// the shared pages goods mounts (§399.2) — the list lives in goods-shared-sources.js (ARCHITECTURE_CYCLE31.md §31.7).
/** @type {import('tailwindcss').Config} */
export default {
  presets: [base],
  content: goodsTailwindContent(),
}
