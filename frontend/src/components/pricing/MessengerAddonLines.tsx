import { Link } from 'react-router-dom'
import type { components } from '../../types/api-cycle40.generated'

type Schemas = components['schemas']
export type MessengerAddonDto = Schemas['MessengerAddonDto']
export type MessengerAddonsNoteDto = Schemas['MessengerAddonsNoteDto']

interface Props {
  /** `messengerAddons` из GET /api/pricing или /api/billing/subscription (API_CONTRACT_CYCLE40.md §40.32). */
  addons: MessengerAddonDto[] | null | undefined
  note: MessengerAddonsNoteDto | null | undefined
  /** Карточка на тёмном фоне (выделенный тариф). */
  inverted?: boolean
}

/**
 * Т40-L-11 / US-11: по строке на каждый продаваемый мессенджер. Всё — `text`, `footnote`, `taxNote`,
 * `conditionsLabel`, `conditionsUrl` — выводится дословно из ответа сервера; цену, слово «от» и
 * состав строк фронт не вычисляет. Пустой список → не рендерим ничего (WhatsApp закрыт — сервер его не отдаёт).
 */
export function MessengerAddonLines({ addons, note, inverted = false }: Props) {
  if (!addons || addons.length === 0) return null
  const soft = inverted ? 'text-cream/75' : 'text-muted'
  return (
    <div className="flex flex-col gap-1.5 mb-5" data-testid="messenger-addon-lines">
      <ul className="flex flex-col gap-1.5">
        {addons.map((a) => (
          <li key={a.transport} className="text-sm" data-testid={`messenger-addon-${a.label}`}>
            <span className="font-medium">{a.text}</span>
            {a.footnote && <p className={`text-xs leading-[1.5] mt-0.5 ${soft}`}>{a.footnote}</p>}
          </li>
        ))}
      </ul>
      {note && (
        <p className={`text-xs leading-[1.5] ${soft}`}>
          <Link to={note.conditionsUrl} className="underline">
            {note.conditionsLabel}
          </Link>
          {note.taxNote && <> · {note.taxNote}</>}
        </p>
      )}
    </div>
  )
}
