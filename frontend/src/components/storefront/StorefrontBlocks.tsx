// API_CONTRACT_CYCLE48_STOREFRONT.md §48.22.3 — renders the blocks and inline strings of contracts/cycle48/storefront-pages.json as React nodes.
// No dangerouslySetInnerHTML: the text goes through the markup parser (storefrontMarkup.ts), `{{KEY}}` is filled afterwards into text nodes
// and hrefs. No network: the components take everything from the bundled JSON.
import { Fragment, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { fillOperatorTemplate, resolveStorefrontHref } from '@/utils/operatorRequisites'
import {
  itemVisibleOn,
  parseInline,
  type InlineNode,
  type StorefrontBlock,
  type StorefrontItem,
  type StorefrontSite,
} from '@/utils/storefrontMarkup'
import { resolveSiteUrls } from '@/utils/publicSites'

/** 'router': <Link> (SPA and SSR inside StaticRouter); 'plain': <a href> (<noscript>); { origin }: absolute <a> (about). */
export type LinkMode = 'router' | 'plain' | { origin: string }

const LINK_CLASS = 'text-ink underline underline-offset-2 hover:no-underline'

/** Optional look of the inline renderer (the footer line differs from the page body). */
export interface InlineOptions {
  /** Placeholders whose substituted value is wrapped in `<span class="whitespace-nowrap">` (ИНН, телефон in the footer). */
  nowrapKeys?: readonly string[]
  /** Replaces the default link class. */
  linkClassName?: string
}

interface Ctx extends InlineOptions {
  site: StorefrontSite
  linkMode: LinkMode
}

function renderText(value: string, ctx: Ctx, key: string): ReactNode {
  if (!ctx.nowrapKeys || ctx.nowrapKeys.length === 0) return fillOperatorTemplate(value)
  const wrapped = new Set(ctx.nowrapKeys.map((k) => `{{${k}}}`))
  const parts = value.split(/(\{\{[^{}]*\}\})/)
  return parts.map((part, i) =>
    wrapped.has(part) ? (
      <span key={`${key}.${i}`} className="whitespace-nowrap">
        {fillOperatorTemplate(part)}
      </span>
    ) : (
      fillOperatorTemplate(part)
    ),
  )
}

function renderNodes(nodes: InlineNode[], ctx: Ctx, keyPrefix: string): ReactNode[] {
  return nodes.map((node, i) => {
    const key = `${keyPrefix}.${i}`
    switch (node.kind) {
      case 'text':
        return <Fragment key={key}>{renderText(node.value, ctx, key)}</Fragment>
      case 'strong':
        return (
          <strong key={key} className="font-semibold text-ink">
            {renderNodes(node.children, ctx, key)}
          </strong>
        )
      case 'em':
        return <em key={key}>{renderNodes(node.children, ctx, key)}</em>
      case 'link':
        return renderLink(fillOperatorTemplate(node.href), renderNodes(node.children, ctx, key), ctx, key)
    }
  })
}

function renderLink(href: string, children: ReactNode, ctx: Ctx, key: string): ReactNode {
  const { site, linkMode } = ctx
  const LINK_CLASS_ = ctx.linkClassName ?? LINK_CLASS
  if (href.startsWith('https://')) {
    return (
      <a key={key} href={href} rel="noopener noreferrer" className={LINK_CLASS_}>
        {children}
      </a>
    )
  }
  if (href.startsWith('/')) {
    if (typeof linkMode === 'object') {
      return (
        <a key={key} href={linkMode.origin + href} className={LINK_CLASS_}>
          {children}
        </a>
      )
    }
    if (linkMode === 'plain') {
      return (
        <a key={key} href={href} className={LINK_CLASS_}>
          {children}
        </a>
      )
    }
    const target = resolveStorefrontHref(
      site,
      href,
      resolveSiteUrls(import.meta.env, import.meta.env.DEV ? 'development' : 'production'),
    )
    if (target.startsWith('https://') || target.startsWith('http://')) {
      return (
        <a key={key} href={target} className={LINK_CLASS_}>
          {children}
        </a>
      )
    }
    return (
      <Link key={key} to={target} className={LINK_CLASS_}>
        {children}
      </Link>
    )
  }
  // tel: / mailto:
  return (
    <a key={key} href={href} className={LINK_CLASS_}>
      {children}
    </a>
  )
}

interface CommonProps {
  site: StorefrontSite
  linkMode: LinkMode
}

/** One string of mini-markup (footer lines, notice-like places). */
export function StorefrontInline({
  source,
  site,
  linkMode,
  ...options
}: CommonProps & InlineOptions & { source: string }) {
  return <>{renderNodes(parseInline(source), { site, linkMode, ...options }, 'i')}</>
}

function renderItem(item: StorefrontItem, i: number, ctx: Ctx) {
  if (typeof item === 'string') return <li key={i}>{renderNodes(parseInline(item), ctx, `li${i}`)}</li>
  if ('paras' in item) {
    return (
      <li key={i}>
        {item.paras.map((para, j) => (
          <p key={j} className={j > 0 ? 'mt-2' : undefined}>
            {renderNodes(parseInline(para), ctx, `li${i}.${j}`)}
          </p>
        ))}
      </li>
    )
  }
  return <li key={i}>{renderNodes(parseInline(item.text), ctx, `li${i}`)}</li>
}

export function StorefrontBlocks({ blocks, site, linkMode }: CommonProps & { blocks: StorefrontBlock[] }) {
  const ctx: Ctx = { site, linkMode }
  return (
    <>
      {blocks.map((block, i) => {
        switch (block.type) {
          case 'h2':
            return (
              <h2 key={i} id={block.id} className="font-serif text-[22px] font-medium text-ink mt-10 mb-3">
                {renderNodes(parseInline(block.text), ctx, `b${i}`)}
              </h2>
            )
          case 'p':
            return (
              <p key={i} id={block.id} className="text-[15px] leading-[1.65] text-ink-soft mb-3">
                {renderNodes(parseInline(block.text), ctx, `b${i}`)}
              </p>
            )
          case 'ul':
          case 'ol': {
            const Tag = block.type
            const cls = `${block.type === 'ul' ? 'list-disc' : 'list-decimal'} pl-5 space-y-2 text-[15px] leading-[1.65] text-ink-soft mb-3`
            return (
              <Tag key={i} id={block.id} className={cls}>
                {block.items.filter((item) => itemVisibleOn(item, site)).map((item, j) => renderItem(item, j, ctx))}
              </Tag>
            )
          }
        }
      })}
    </>
  )
}
