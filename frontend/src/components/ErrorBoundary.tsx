import { Component, type ReactNode } from 'react'
import { Card } from './ui/Card'
import { Button } from './ui/Button'
import { Icon } from './ui/Icon'

interface ErrorBoundaryProps {
  children: ReactNode
  /** Shown in the fallback card so it's clear which part of the page failed, not the whole app
   *  (§103.1: "падение одной вкладки не гасит остальные"). Defaults to a generic phrase for the
   *  outermost boundary around <Routes> in App.tsx. */
  label?: string
}

interface ErrorBoundaryState {
  error: Error | null
}

/**
 * React 18 offers no hook equivalent for `componentDidCatch` — a class component is the only way
 * to catch a render-time throw (ARCHITECTURE_CYCLE9.md §100.2/§103.1). Without this, an unguarded
 * field access anywhere below (e.g. `plan.highlights.length` on a pre-cycle-7 API response) unmounts
 * the whole React tree instead of just the one broken section — a white screen, not a broken widget.
 *
 * This does NOT fix the underlying bug (§103.1 warns against treating it as a substitute for the
 * real fix) — it only changes the failure class from "product disappeared" to "one block didn't
 * render", by rendering a small fallback card in place of the thrown subtree.
 */
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error }
  }

  componentDidCatch(error: Error, info: { componentStack: string }) {
    console.error('ErrorBoundary caught a render error', error, info.componentStack)
  }

  // A full reload rather than clearing local state: the error is almost always caused by data
  // already sitting in a react-query cache (stale shape from a mismatched API release) or by a
  // one-off render race, and simply unsetting `error` would just throw again on the next render.
  private reset = () => window.location.reload()

  render() {
    if (this.state.error) {
      return (
        <Card className="p-8 text-center text-muted my-4">
          <Icon name="alert-circle" size={32} strokeWidth={1.4} className="mx-auto mb-3 text-danger" />
          <p className="text-ink font-medium mb-1">Не удалось отобразить раздел</p>
          <p className="text-sm text-muted mb-4">
            {this.props.label
              ? `«${this.props.label}» временно недоступен.`
              : 'Остальная часть страницы работает как обычно.'}
          </p>
          <Button variant="secondary" onClick={this.reset}>
            Обновить
          </Button>
        </Card>
      )
    }
    return this.props.children
  }
}
