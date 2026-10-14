// Fast loop (ARCHITECTURE_CYCLE36.md §36.10): run only the vitest files of the named areas.
// Usage: npm run test:area -- orders notifications
// Does NOT replace the full run before a merge.
import fs from 'node:fs'
import path from 'node:path'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const config = JSON.parse(fs.readFileSync(path.join(root, '../contracts/cycle36/test-areas.json'), 'utf8'))
const known = config.areas.map((a) => a.id)

function walk(dir, out) {
  if (!fs.existsSync(dir)) return out
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name)
    if (e.isDirectory()) walk(p, out)
    else if (/\.test\.tsx?$/.test(e.name)) out.push(path.relative(root, p).split(path.sep).join('/'))
  }
  return out
}

const wanted = process.argv.slice(2)
const unknown = wanted.filter((a) => !known.includes(a))
if (wanted.length === 0 || unknown.length > 0) {
  if (unknown.length > 0) console.error(`Unknown area: ${unknown.join(', ')}`)
  console.error(`Usage: npm run test:area -- <area> [<area>...]\nAvailable areas: ${known.join(', ')}`)
  process.exit(1)
}

const prefixes = config.areas.filter((a) => wanted.includes(a.id)).flatMap((a) => a.frontendPathPrefixes)
const files = [...walk(path.join(root, 'src'), []), ...walk(path.join(root, 'goods/src'), []), ...walk(path.join(root, 'dom/src'), []), ...walk(path.join(root, 'bani/src'), [])].filter((f) =>
  prefixes.some((p) => f.startsWith(p)),
)
if (files.length === 0) {
  console.error(`No test files for: ${wanted.join(', ')}`)
  process.exit(1)
}
console.log(`[test:area] ${wanted.join(', ')}: ${files.length} file(s)`)
// Vitest treats positional args as substring filters: absolute paths keep `src/x.test.ts` from matching `goods/src/x.test.ts`.
const abs = files.map((f) => path.join(root, f))
// cmd.exe limits the command line to ~8 KB: run in batches.
const batches = []
let cur = [], len = 0
for (const f of abs) {
  if (cur.length > 0 && len + f.length + 1 > 6000) { batches.push(cur); cur = []; len = 0 }
  cur.push(f); len += f.length + 1
}
batches.push(cur)
let code = 0
for (const b of batches) {
  const r = spawnSync('npx', ['vitest', 'run', ...b], { cwd: root, stdio: 'inherit', shell: process.platform === 'win32' })
  if ((r.status ?? 1) !== 0) code = r.status ?? 1
}
process.exit(code)
