// Fills the about.ezbook.ru HTML template with the server-rendered page and writes the release files into dist-about/
// (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.4.1, self-checks §44.4.3). Run after `vite build` and `vite build --ssr`.
// Exit code 0, or 1 with `prerender-about: <reason>` on stderr.
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const root = fileURLToPath(new URL('..', import.meta.url))
const outDir = join(root, 'dist-about')
const ssrModule = join(root, 'dist-about-ssr', 'prerender.js')

function fail(reason) {
  console.error(`prerender-about: ${reason}`)
  process.exit(1)
}

if (!existsSync(join(outDir, 'index.html'))) fail('dist-about/index.html not found — run `vite build --config vite.about.config.ts` first')
if (!existsSync(ssrModule)) fail('dist-about-ssr/prerender.js not found — run `vite build --config vite.about.config.ts --ssr` first')

const template = readFileSync(join(outDir, 'index.html'), 'utf8')
const { renderSite } = await import(pathToFileURL(ssrModule).href)

let result
try {
  result = renderSite({ template, env: process.env, mode: 'production', year: new Date().getFullYear() })
} catch (e) {
  fail(e instanceof Error ? e.message : String(e))
}
const { files, report } = result

for (const [name, content] of Object.entries(files)) {
  if (content.includes('<!--about:')) fail(`${name}: template marker left in the output`)
}
for (const name of ['index.html', '404.html']) {
  if (/<script/i.test(files[name])) fail(`${name}: contains <script> (the page must have no client JS)`)
  if (/\sstyle=/i.test(files[name])) fail(`${name}: contains a style attribute (blocked by the CSP)`)
}
const html = files['index.html']
const count = (re) => (html.match(re) ?? []).length
if (count(/<h1[\s>]/g) !== 1) fail('index.html: expected exactly one <h1>')
if (count(/<link rel="canonical"/g) !== 1) fail('index.html: expected exactly one <link rel="canonical">')
if (count(/<meta name="description"/g) !== 1) fail('index.html: expected exactly one <meta name="description">')
if (count(/<title>/g) !== 1) fail('index.html: expected exactly one <title>')
if (/noindex/i.test(html)) fail('index.html: contains noindex')
for (const key of report.hiddenProducts) {
  if (html.includes(`data-product="${key}"`)) fail(`index.html: hidden product "${key}" is rendered`)
}
if (count(/data-product="/g) !== report.visibleProducts.length) fail('index.html: number of data-product cards differs from the visible products')
if (html.includes('/api/')) fail('index.html: contains /api/')

for (const [name, content] of Object.entries(files)) writeFileSync(join(outDir, name), content)
console.log(`prerender-about: wrote ${Object.keys(files).join(', ')} (products: ${report.visibleProducts.join(', ')}; hidden: ${report.hiddenProducts.join(', ') || 'none'})`)
