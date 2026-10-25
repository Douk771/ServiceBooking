// ARCHITECTURE_CYCLE48_STOREFRONT.md §48.6, §48.10.1, API_CONTRACT_CYCLE48_STOREFRONT.md §48.23 — the two requisites lines of the site
// footers (US-48-01) and the content of <noscript> (US-48-06). One implementation for ezbook, goods, dom, bani and about; the text comes
// from contracts/cycle48/storefront-pages.json, the values from contracts/legal/operator-public.json.
import { StorefrontBlocks, StorefrontInline, type LinkMode } from '@/components/storefront/StorefrontBlocks'
import { STOREFRONT_PAGES, type StorefrontBlock, type StorefrontSite } from '@/utils/storefrontMarkup'

// Footer text is text-ink-soft (≈ 5.8 : 1 on cream), not text-muted (≈ 2.6 : 1): WCAG 2.1 AA, §48.6.2.
const FOOTER_LINK =
  'inline-flex min-h-[24px] items-center text-ink-soft underline underline-offset-2 hover:text-ink transition-colors'
const NOWRAP_KEYS = ['ИНН_ОПЕРАТОРА', 'ТЕЛЕФОН_ОПЕРАТОРА']

export function OperatorRequisitesLine({ linkMode, site = 'zapis' }: { linkMode: LinkMode; site?: StorefrontSite }) {
  const { identity, contacts } = STOREFRONT_PAGES.footer
  return (
    <div className="flex flex-col items-center gap-1 text-center" data-testid="operator-requisites">
      <p className="text-center [overflow-wrap:anywhere]">
        <StorefrontInline source={identity} site={site} linkMode={linkMode} nowrapKeys={NOWRAP_KEYS} />
      </p>
      <p className="text-center [overflow-wrap:anywhere]">
        <StorefrontInline
          source={contacts}
          site={site}
          linkMode={linkMode}
          nowrapKeys={NOWRAP_KEYS}
          linkClassName={FOOTER_LINK}
        />
      </p>
    </div>
  )
}

function documentsBlock(): StorefrontBlock {
  const block = STOREFRONT_PAGES.pages.contacts.blocks.find((b) => b.id === 'documents')
  if (!block) throw new Error('storefront-pages.json: pages.contacts has no block with id "documents"')
  return block
}

/** Content of <noscript> (no new words: footer lines + the «Документы» list of /contacts). Plain <a>, no style=, no scripts. */
export function OperatorNoscriptContent({ site }: { site: StorefrontSite }) {
  const { identity, contacts } = STOREFRONT_PAGES.footer
  return (
    <div className="mx-auto max-w-[760px] px-4 py-6 text-sm text-ink-soft" data-operator-requisites="noscript">
      <p>
        <StorefrontInline source={identity} site={site} linkMode="plain" nowrapKeys={NOWRAP_KEYS} />
      </p>
      <p>
        <StorefrontInline source={contacts} site={site} linkMode="plain" nowrapKeys={NOWRAP_KEYS} />
      </p>
      <StorefrontBlocks blocks={[documentsBlock()]} site={site} linkMode="plain" />
    </div>
  )
}
