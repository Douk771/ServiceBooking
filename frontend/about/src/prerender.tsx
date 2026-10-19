import { renderToStaticMarkup } from 'react-dom/server'
import { resolveSiteUrls } from '@/utils/publicSites'
import type { SiteEnv, SiteMode } from '@/utils/publicSites'
import { AboutPage } from './components/AboutPage'
import { Head } from './components/Head'
import { NotFoundPage } from './components/NotFoundPage'
import { PAGE_COPY } from './content/page'
import { PRODUCTS } from './content/products'
import type { PageCopy, ProductCard, ProductKey } from './content/types'
import { visibleProducts } from './content/visible'

// API_CONTRACT_CYCLE44_PRODUCTS_LANDING.md §44.24. Pure: no I/O, no Date.now(), no window/document.

export interface RenderSiteInput {
  template: string
  env: SiteEnv
  mode: SiteMode
  year: number
  products?: readonly ProductCard[]
  copy?: PageCopy
}
export interface RenderReport {
  visibleProducts: readonly ProductKey[]
  hiddenProducts: readonly ProductKey[]
  canonical: string
}
export interface RenderSiteOutput {
  files: Readonly<{ 'index.html': string; '404.html': string; 'robots.txt': string; 'sitemap.xml': string }>
  report: RenderReport
}

const HEAD_MARKER = '<!--about:head-->'
const BODY_MARKER = '<!--about:body-->'

function fill(template: string, head: string, body: string): string {
  for (const marker of [HEAD_MARKER, BODY_MARKER]) {
    if (template.split(marker).length !== 2) throw new Error(`about template: marker ${marker} not found`)
  }
  // Function replacers: `$` sequences in the content must not be interpreted.
  return template.replace(HEAD_MARKER, () => head).replace(BODY_MARKER, () => body)
}

export function renderSite(input: RenderSiteInput): RenderSiteOutput {
  const products = input.products ?? PRODUCTS
  const copy = input.copy ?? PAGE_COPY
  const sites = resolveSiteUrls(input.env, input.mode)
  const canonical = `${sites.about}/`

  const head = renderToStaticMarkup(<Head copy={copy} canonical={canonical} />)
  const body = renderToStaticMarkup(<AboutPage products={products} sites={sites} copy={copy} year={input.year} />)

  const notFoundTitle = `${copy.notFound.title} — ${copy.brand}`
  const notFoundHead = renderToStaticMarkup(
    <>
      <title>{notFoundTitle}</title>
      <meta name="robots" content="noindex" />
    </>,
  )
  const notFoundBody = renderToStaticMarkup(<NotFoundPage copy={copy} />)

  const visible = visibleProducts(products)
  return {
    files: {
      'index.html': `<!doctype html>\n${fill(input.template.replace(/^<!doctype html>\s*/i, ''), head, body)}`,
      '404.html': `<!doctype html>\n${fill(input.template.replace(/^<!doctype html>\s*/i, ''), notFoundHead, notFoundBody)}`,
      'robots.txt': `User-agent: *\nAllow: /\n\nSitemap: ${sites.about}/sitemap.xml\n`,
      'sitemap.xml': `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n  <url><loc>${canonical}</loc></url>\n</urlset>\n`,
    },
    report: {
      visibleProducts: visible.map((p) => p.key),
      hiddenProducts: products.filter((p) => p.status === 'hidden').map((p) => p.key),
      canonical,
    },
  }
}
