// ARCHITECTURE_CYCLE37.md §37.14.1 — the list now lives in shared-sources.js (goods and dom share it). This file keeps the
// goods-specific names so goods imports, the ESLint config and tailwind.goods.config.js do not change.
export {
  SHARED_EZBOOK_PAGES as GOODS_SHARED_EZBOOK_PAGES,
  SCANNED_EZBOOK_DIRS as GOODS_SCANNED_EZBOOK_DIRS,
  MARKUP_FREE_EZBOOK_DIRS as GOODS_MARKUP_FREE_EZBOOK_DIRS,
  SHARED_EZBOOK_FILES as GOODS_SHARED_EZBOOK_FILES,
} from './shared-sources.js'
import { appTailwindContent, appAllowedPagesRegex } from './shared-sources.js'

export function goodsTailwindContent() {
  return appTailwindContent('goods')
}

export function goodsAllowedPagesRegex() {
  return appAllowedPagesRegex()
}
