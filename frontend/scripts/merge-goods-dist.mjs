// Copies the goods build (dist-goods/) into the ezbook build as dist/__goods/, so one release directory
// carries both sites (ARCHITECTURE_CYCLE23.md §399.5, §401.3). Run after `npm run build && npm run build:goods`.
import { cpSync, existsSync, rmSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'

const root = fileURLToPath(new URL('..', import.meta.url))
const src = join(root, 'dist-goods')
const dest = join(root, 'dist', '__goods')

if (!existsSync(src)) {
  console.error('merge-goods-dist: dist-goods/ not found — run `npm run build:goods` first')
  process.exit(1)
}
if (!existsSync(join(root, 'dist'))) {
  console.error('merge-goods-dist: dist/ not found — run `npm run build` first')
  process.exit(1)
}
rmSync(dest, { recursive: true, force: true })
cpSync(src, dest, { recursive: true })
console.log(`merge-goods-dist: ${src} -> ${dest}`)
