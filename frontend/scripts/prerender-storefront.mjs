// Puts the operator requisites into the release HTML of the four sites (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.10, A48-5):
//   1. replaces the marker <!--storefront:noscript--> of every index.html with <noscript>…</noscript> (requisites + document links);
//   2. writes contacts/index.html and payment-info/index.html from the same template (title + prerendered page in <div id="root">).
// Run after `npm run build*` and `vite build --config vite.storefront.config.ts --ssr`, before merge-site-dist.mjs.
// Exit code 0, or 1 with `prerender-storefront: <reason>` on stderr.
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const root = fileURLToPath(new URL('..', import.meta.url))
const ssrModule = join(root, 'dist-storefront-ssr', 'prerender.js')
const MARKER = '<!--storefront:noscript-->'
const TARGETS = [
  { dir: 'dist', site: 'zapis', noindex: false },
  { dir: 'dist-goods', site: 'goods', noindex: false },
  { dir: 'dist-dom', site: 'dom', noindex: true },
  { dir: 'dist-bani', site: 'bani', noindex: true },
]
const PAGES = [
  { key: 'contacts', dir: 'contacts', title: 'Контакты и реквизиты — EZBOOK' },
  { key: 'paymentInfo', dir: 'payment-info', title: 'Оплата и возврат — EZBOOK' },
]

function fail(reason) {
  console.error(`prerender-storefront: ${reason}`)
  process.exit(1)
}

if (!existsSync(ssrModule)) fail('dist-storefront-ssr/prerender.js not found — run `vite build --config vite.storefront.config.ts --ssr` first')
const operator = JSON.parse(readFileSync(join(root, '..', 'contracts', 'legal', 'operator-public.json'), 'utf8'))
const inn = operator.values['ИНН_ОПЕРАТОРА']
if (!/^[0-9]{12}$/.test(inn ?? '')) fail('ИНН_ОПЕРАТОРА in contracts/legal/operator-public.json is not 12 digits')
const { renderNoscript, renderPage } = await import(pathToFileURL(ssrModule).href)

/** Common checks of a fragment that goes into the page (no inline style/script: CSP default-src 'self'; no API; the INN is there). */
function checkFragment(where, fragment) {
  if (/\sstyle=/i.test(fragment)) fail(`${where}: contains a style attribute (blocked by the CSP)`)
  if (/<script/i.test(fragment)) fail(`${where}: contains <script>`)
  if (fragment.includes('/api/')) fail(`${where}: contains /api/`)
  if (fragment.includes('{{') || fragment.includes('}}')) fail(`${where}: unfilled placeholder`)
}

const written = []
for (const { dir, site, noindex } of TARGETS) {
  const outDir = join(root, dir)
  const indexPath = join(outDir, 'index.html')
  if (!existsSync(indexPath)) fail(`${dir}/index.html not found — build the "${site}" application first`)
  const source = readFileSync(indexPath, 'utf8')
  if (source.split(MARKER).length !== 2) fail(`${dir}/index.html: marker ${MARKER} must occur exactly once`)

  const noscriptInner = renderNoscript(site)
  checkFragment(`${dir}/index.html <noscript>`, noscriptInner)
  const template = source.replace(MARKER, () => `<noscript>${noscriptInner}</noscript>`)
  if (template.includes('<!--storefront:')) fail(`${dir}/index.html: a storefront marker is left in the output`)
  if (!template.includes(inn)) fail(`${dir}/index.html: the INN is missing`)
  if (noindex && !/noindex/i.test(template)) fail(`${dir}/index.html: noindex is gone`)
  writeFileSync(indexPath, template)
  written.push(`${dir}/index.html`)

  if (!template.includes('<div id="root"></div>')) fail(`${dir}/index.html: <div id="root"></div> not found in the template`)
  if ((template.match(/<title>[^<]*<\/title>/g) ?? []).length !== 1) fail(`${dir}/index.html: expected exactly one <title>`)
  for (const { key, dir: pageDir, title } of PAGES) {
    const page = renderPage(site, key)
    if (page.title !== title) fail(`${dir}/${pageDir}: title "${page.title}" differs from the contract`)
    checkFragment(`${dir}/${pageDir}/index.html`, page.html)
    if ((page.html.match(/<h1[\s>]/g) ?? []).length !== 1) fail(`${dir}/${pageDir}/index.html: expected exactly one <h1>`)
    const html = template
      .replace(/<title>[^<]*<\/title>/, () => `<title>${title}</title>`)
      .replace('<div id="root"></div>', () => `<div id="root">${page.html}</div>`)
    if (!html.includes(inn)) fail(`${dir}/${pageDir}/index.html: the INN is missing`)
    if (noindex && !/noindex/i.test(html)) fail(`${dir}/${pageDir}/index.html: noindex is gone`)
    mkdirSync(join(outDir, pageDir), { recursive: true })
    writeFileSync(join(outDir, pageDir, 'index.html'), html)
    written.push(`${dir}/${pageDir}/index.html`)
  }
}
console.log(`prerender-storefront: wrote ${written.join(', ')}`)
