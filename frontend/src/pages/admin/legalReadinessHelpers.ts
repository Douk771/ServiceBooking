import type { LegalReadiness } from '../../types'

// Вынесено из LegalReadinessTab.tsx: react-refresh/only-export-components — экспорт функции рядом с
// компонентом лишал файл горячей подмены. Проверка формы ответа от рендера не зависит и проверяется
// без него (LegalReadinessTab.test.tsx импортирует её отсюда).

// A 503 that reaches here from something other than the controller itself (reverse proxy/gateway
// health-check page, HTML error body, etc.) must not be cast blindly — an untyped truthy string
// would otherwise crash `LegalReadinessReport` on `data.placeholders.reduce`. Only trust the body
// when it actually has the report's shape.
export function isLegalReadinessShape(value: unknown): value is LegalReadiness {
  if (!value || typeof value !== 'object') return false
  const v = value as Partial<LegalReadiness>
  return Array.isArray(v.blockers) && Array.isArray(v.placeholders) && typeof v.ready === 'boolean'
}
