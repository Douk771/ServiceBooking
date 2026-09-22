import { forwardRef, useImperativeHandle, useRef, type InputHTMLAttributes } from 'react'
import { Input } from './Input'
import { maskPhoneInput, toCanonicalPhone } from '../../utils/phone'

interface PhoneInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'> {
  label?: string
  error?: string
  /**
   * Canonical digits (`79990000000`) when `restrictToRussia` is true (the default). When
   * `restrictToRussia` is false, this is instead the raw text as typed (e.g. `+380671234567`) — see
   * below for why.
   */
  value: string
  /** Fires with the canonical digit string (or, in `restrictToRussia={false}` mode, the raw text). */
  onChange: (canonical: string) => void
  /**
   * Defaults to `true` — the Russian-only input policy (US-61, §48.1/§48.2) applies to forms that
   * *create* a phone number (registration, profile phone change). Sign-in is explicitly exempt
   * (ARCHITECTURE_CYCLE6.md §947): an account may already have a foreign number on file, and the
   * server still normalizes logins with `Normalize`, not `TryNormalizeRussian`. LoginPage passes
   * `false` so such a number can actually be typed and submitted.
   *
   * When `false`, the grouped `+7 (900) 000-00-00` live mask is dropped in favor of plain,
   * unmangled text entry: the mask's controlled round-trip (value → canonical digits → re-render)
   * has nowhere to keep a bare `+` the user just typed before any digits follow it, so a `+380…`
   * number typed digit-by-digit would lose its `+` and get folded back into a Russian number. The
   * caller (LoginPage) normalizes the raw text with `toCanonicalPhoneLenient` once, at submit time,
   * instead.
   */
  restrictToRussia?: boolean
}

/**
 * Masked phone input for US-61 (`ARCHITECTURE_CYCLE6.md` §48.3): displays `+7 (900) 000-00-00`
 * while the caller keeps working with the canonical digit string. Wraps the existing `Input` — a
 * second phone-formatting component would violate the "one function" rule from the same section.
 *
 * `Backspace`/`Delete` remove the digit adjacent to the cursor, not whatever character happens to be
 * there — without this, deleting into a `)` or `-` visually "sticks" because re-masking recreates the
 * same separator right back.
 */
export const PhoneInput = forwardRef<HTMLInputElement, PhoneInputProps>(
  ({ label, error, value, onChange, className, restrictToRussia = true, ...props }, forwardedRef) => {
    const innerRef = useRef<HTMLInputElement>(null)
    useImperativeHandle(forwardedRef, () => innerRef.current as HTMLInputElement)

    if (!restrictToRussia) {
      // Plain, unmangled text entry — see the `restrictToRussia` doc comment above for why the
      // masked round-trip doesn't work here.
      return (
        <Input
          ref={innerRef}
          label={label}
          error={error}
          className={className}
          type="tel"
          autoComplete="tel"
          placeholder="+7 (900) 000-00-00"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          {...props}
        />
      )
    }

    const toCanonical = toCanonicalPhone
    const mask = maskPhoneInput

    const displayValue = mask(value)

    const setCanonicalAndCursor = (nextCanonical: string, cursorDigitIndex: number) => {
      onChange(nextCanonical)
      const nextDisplay = mask(nextCanonical)
      // The re-render hasn't happened yet; place the caret once the new masked value is painted.
      requestAnimationFrame(() => {
        const el = innerRef.current
        if (!el) return
        let seen = 0
        let pos = nextDisplay.length
        for (let i = 0; i < nextDisplay.length; i++) {
          if (/\d/.test(nextDisplay[i])) {
            seen++
            if (seen === cursorDigitIndex) {
              pos = i + 1
              break
            }
          }
        }
        if (cursorDigitIndex === 0) pos = nextDisplay.startsWith('+7') ? 2 : 0
        el.setSelectionRange(pos, pos)
      })
    }

    const digitsBeforeCursor = (text: string, cursor: number) => {
      let count = 0
      for (let i = 0; i < cursor && i < text.length; i++) if (/\d/.test(text[i])) count++
      return count
    }

    const handleKeyDown: React.KeyboardEventHandler<HTMLInputElement> = (e) => {
      if (e.key !== 'Backspace' && e.key !== 'Delete') return
      const el = e.currentTarget
      if (el.selectionStart !== el.selectionEnd) return // let the browser handle range deletes as a normal edit
      const cursor = el.selectionStart ?? 0
      const canonical = toCanonical(value)
      const current = mask(value)

      if (e.key === 'Backspace') {
        if (cursor === 0) return
        const idx = digitsBeforeCursor(current, cursor) // 1-based count of digits before caret
        if (idx === 0) return
        e.preventDefault()
        const nextCanonical = canonical.slice(0, idx - 1) + canonical.slice(idx)
        setCanonicalAndCursor(nextCanonical, idx - 1)
      } else {
        // Delete: remove the digit at/after the caret.
        const idx = digitsBeforeCursor(current, cursor) // digits strictly before caret
        if (idx >= canonical.length) return
        e.preventDefault()
        const nextCanonical = canonical.slice(0, idx) + canonical.slice(idx + 1)
        setCanonicalAndCursor(nextCanonical, idx)
      }
    }

    const handleChange: React.ChangeEventHandler<HTMLInputElement> = (e) => {
      const nextCanonical = toCanonical(e.target.value)
      onChange(nextCanonical)
    }

    return (
      <Input
        ref={innerRef}
        label={label}
        error={error}
        className={className}
        type="tel"
        inputMode="numeric"
        autoComplete="tel"
        placeholder="+7 (900) 000-00-00"
        value={displayValue}
        onChange={handleChange}
        onKeyDown={handleKeyDown}
        {...props}
      />
    )
  },
)
PhoneInput.displayName = 'PhoneInput'
