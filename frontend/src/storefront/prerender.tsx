import { renderToStaticMarkup } from 'react-dom/server'
import { StaticRouter } from 'react-router-dom/server'
import { OperatorNoscriptContent, OperatorRequisitesLine } from '@/components/sites/OperatorRequisites'
import { ContactsPage } from '@/pages/ContactsPage'
import { PaymentInfoPage } from '@/pages/PaymentInfoPage'
import { STOREFRONT_PAGES, type StorefrontSite } from '@/utils/storefrontMarkup'

// ARCHITECTURE_CYCLE48_STOREFRONT.md §48.10.2, API_CONTRACT_CYCLE48_STOREFRONT.md §48.23 — entry of the SSR build
// (vite.storefront.config.ts -> dist-storefront-ssr/prerender.js). scripts/prerender-storefront.mjs calls these two functions and writes the
// results into dist*/. Pure: no I/O, no window/document, no network. Not imported by any application.

/** Inside of `<noscript>…</noscript>`: the requisites lines and the document list, plain links, no style=, no scripts. */
export function renderNoscript(site: StorefrontSite): string {
  return renderToStaticMarkup(<OperatorNoscriptContent site={site} />)
}

const PAGES = {
  contacts: { path: '/contacts', page: STOREFRONT_PAGES.pages.contacts },
  paymentInfo: { path: '/payment-info', page: STOREFRONT_PAGES.pages.paymentInfo },
} as const

/** `title` for `<title>` and `html` for the inside of `<div id="root">` of /contacts or /payment-info of the given site. */
export function renderPage(site: StorefrontSite, page: 'contacts' | 'paymentInfo'): { title: string; html: string } {
  const { path, page: content } = PAGES[page]
  const Page = page === 'contacts' ? ContactsPage : PaymentInfoPage
  const html = renderToStaticMarkup(
    <StaticRouter location={path}>
      <div className="min-h-screen bg-cream font-sans text-ink">
        <Page site={site} />
        <footer className="border-t border-line mt-16">
          <div className="max-w-[1180px] mx-auto px-4 sm:px-8 py-6 flex flex-col items-center gap-3 text-xs text-ink-soft">
            <OperatorRequisitesLine linkMode="router" site={site} />
          </div>
        </footer>
      </div>
    </StaticRouter>,
  )
  return { title: content.title, html }
}
