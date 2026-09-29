import { createContext, useContext, type ReactNode } from 'react'
import { Link } from 'react-router-dom'

/**
 * A platform notice's `linkUrl` is a relative ezbook path (API_CONTRACT_CYCLE20.md §434.5: starts with `/`,
 * never `//`). On ezbook it is an in-app `<Link>`; goods.ezbook.ru has none of those pages, so goods provides
 * the ezbook origin and the link becomes an absolute `<a>` to ezbook (ARCHITECTURE_CYCLE23.md, post-merge
 * decision: platform notices are shown on goods too). `null` = the origin isn't known yet → no link rendered.
 */
export type NoticeLinkBase = { kind: 'in-app' } | { kind: 'external'; origin: string | null }

const NoticeLinkContext = createContext<NoticeLinkBase>({ kind: 'in-app' })

export function NoticeLinkProvider({ base, children }: { base: NoticeLinkBase; children: ReactNode }) {
  return <NoticeLinkContext.Provider value={base}>{children}</NoticeLinkContext.Provider>
}

export function NoticeLink({ to, className, children }: { to: string; className?: string; children: ReactNode }) {
  const base = useContext(NoticeLinkContext)
  if (base.kind === 'in-app') {
    return (
      <Link to={to} className={className}>
        {children}
      </Link>
    )
  }
  if (!base.origin) return null
  return (
    <a href={`${base.origin}${to}`} className={className}>
      {children}
    </a>
  )
}
