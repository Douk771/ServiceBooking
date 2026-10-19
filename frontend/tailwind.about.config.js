import base from './tailwind.config.js'
import { aboutTailwindContent } from './shared-sources.js'

// about.ezbook.ru (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.6.1): the same palette preset; only its own sources and the
// ABOUT_SHARED_SOURCES whitelist are scanned.
/** @type {import('tailwindcss').Config} */
export default {
  presets: [base],
  content: aboutTailwindContent(),
}
