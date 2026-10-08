// Copies the secondary site builds into the ezbook build, so one release directory carries all sites:
//   node scripts/merge-site-dist.mjs goods dom  ->  dist/__goods (from dist-goods), dist/__dom (from dist-dom)
// (ARCHITECTURE_CYCLE37.md §37.14.1; replaces merge-goods-dist.mjs, ARCHITECTURE_CYCLE23.md §399.5, §401.3).
// Run after `npm run build` and the per-site builds (`build:goods`, `build:dom`).
import { cpSync, existsSync, rmSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'

const root = fileURLToPath(new URL('..', import.meta.url))
const sites = process.argv.slice(2)
if (sites.length === 0) {
  console.error('merge-site-dist: usage: node scripts/merge-site-dist.mjs <site> [<site>...]  (e.g. goods dom)')
  process.exit(1)
}
if (!existsSync(join(root, 'dist'))) {
  console.error('merge-site-dist: dist/ not found — run `npm run build` first')
  process.exit(1)
}
for (const site of sites) {
  if (!/^[a-z]+$/.test(site)) {
    console.error(`merge-site-dist: bad site name "${site}"`)
    process.exit(1)
  }
  const src = join(root, `dist-${site}`)
  const dest = join(root, 'dist', `__${site}`)
  if (!existsSync(src)) {
    console.error(`merge-site-dist: dist-${site}/ not found — run \`npm run build:${site}\` first`)
    process.exit(1)
  }
  rmSync(dest, { recursive: true, force: true })
  cpSync(src, dest, { recursive: true })
  console.log(`merge-site-dist: ${src} -> ${dest}`)
}
