import { SHOWCASE_ROW_LABEL } from '../../utils/showcaseFilter'

/** Admin rows: the «Витрина» mark of a generated user / company / billing account. */
export function ShowcaseRowBadge() {
  return (
    <span className="text-xs px-2 py-0.5 rounded-full font-medium bg-cream-deep text-ink-soft border border-line-strong">
      {SHOWCASE_ROW_LABEL}
    </span>
  )
}
