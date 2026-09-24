import { useEffect, useRef, useState, useId } from 'react'
import { useQuery } from '@tanstack/react-query'
import { citiesApi } from '../../api/cities'
import { Icon } from './Icon'
import type { City } from '../../types'

interface Props {
  label?: string
  value: City | null
  onChange: (city: City | null) => void
  error?: string
  placeholder?: string
}

/**
 * Accessible city search combobox (T4-F2, US-30 п. 2). Free text is never sent to the server — only
 * a `City` picked from the dropdown is valid, so `onChange` fires a full `City` or `null`, never a
 * string. Keyboard: ↑/↓ moves the highlighted option, Enter selects it, Escape closes the list.
 */
export function CityCombobox({ label = 'Город', value, onChange, error, placeholder = 'Начните вводить город...' }: Props) {
  const [query, setQuery] = useState(value?.label ?? '')
  const [open, setOpen] = useState(false)
  const [highlighted, setHighlighted] = useState(0)
  // ARCHITECTURE_CYCLE9.md §103.4 (US-114): distinguishes "field shows the selection" from "user is
  // typing". While `dirty === false` the server sees `value.name`, never the full "{Name}, {Region}"
  // label — sending the label produced zero matches and a false "not found" on every focus.
  const [dirty, setDirty] = useState(false)
  const listboxId = useId()
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    setQuery(value?.label ?? '')
    setDirty(false)
  }, [value])

  useEffect(() => {
    const onClickOutside = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false)
        setQuery(value?.label ?? '')
        setDirty(false)
      }
    }
    document.addEventListener('mousedown', onClickOutside)
    return () => document.removeEventListener('mousedown', onClickOutside)
  }, [value])

  const searchTerm = dirty ? query : (value?.name ?? '')
  const { data: options } = useQuery({
    queryKey: ['cities', searchTerm],
    queryFn: () => citiesApi.search(searchTerm),
    enabled: open,
    staleTime: 60_000,
  })
  const items = options ?? []

  const select = (city: City) => {
    onChange(city)
    setQuery(city.label)
    setDirty(false)
    setOpen(false)
  }

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (!open && (e.key === 'ArrowDown' || e.key === 'Enter')) {
      setOpen(true)
      return
    }
    if (!open) return
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setHighlighted((h) => Math.min(h + 1, items.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setHighlighted((h) => Math.max(h - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      const city = items[highlighted]
      if (city) select(city)
    } else if (e.key === 'Escape') {
      setOpen(false)
    }
  }

  return (
    <div className="flex flex-col gap-1.5 relative" ref={containerRef}>
      {label && (
        <label className="text-[13px] font-medium text-[#4A4038]" htmlFor={`${listboxId}-input`}>
          {label}
        </label>
      )}
      <div className="relative">
        <input
          id={`${listboxId}-input`}
          role="combobox"
          aria-label={label || placeholder}
          aria-expanded={open}
          aria-controls={listboxId}
          aria-autocomplete="list"
          aria-activedescendant={open && items[highlighted] ? `${listboxId}-opt-${items[highlighted].id}` : undefined}
          autoComplete="off"
          value={query}
          placeholder={placeholder}
          onFocus={() => setOpen(true)}
          onChange={(e) => {
            setQuery(e.target.value)
            setDirty(true)
            setHighlighted(0)
            setOpen(true)
            if (value) onChange(null)
          }}
          onKeyDown={onKeyDown}
          className={`w-full rounded-xl border px-4 py-3 text-sm outline-none transition-all bg-white text-ink placeholder:text-muted ${
            error ? 'border-danger focus:ring-2 focus:ring-danger-bg' : 'border-line focus:border-gold focus:ring-[3px] focus:ring-cream-deep'
          }`}
        />
        {open && items.length > 0 && (
          <ul
            id={listboxId}
            role="listbox"
            className="absolute z-20 mt-1 w-full max-h-64 overflow-y-auto rounded-xl border border-line bg-white shadow-modal py-1"
          >
            {items.map((city, i) => (
              <li
                key={city.id}
                id={`${listboxId}-opt-${city.id}`}
                role="option"
                aria-selected={i === highlighted}
                onMouseDown={(e) => {
                  e.preventDefault()
                  select(city)
                }}
                onMouseEnter={() => setHighlighted(i)}
                className={`px-4 py-2 text-sm cursor-pointer flex items-center justify-between gap-2 ${
                  i === highlighted ? 'bg-cream-deep text-ink' : 'text-ink-soft'
                }`}
              >
                <span>{city.label}</span>
                {value?.id === city.id && <Icon name="check" size={13} strokeWidth={2} className="text-gold-dark" />}
              </li>
            ))}
          </ul>
        )}
        {open && items.length === 0 && dirty && query.trim().length > 0 && (
          <div className="absolute z-20 mt-1 w-full rounded-xl border border-line bg-white shadow-modal px-4 py-3 text-sm text-muted">
            Город не найден
          </div>
        )}
      </div>
      {error && <p className="text-xs text-danger">{error}</p>}
    </div>
  )
}
