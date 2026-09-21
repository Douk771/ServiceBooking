import { forwardRef, useImperativeHandle, useRef, type InputHTMLAttributes } from 'react'
import { Input } from './Input'
import { maskPhoneInput, toCanonicalPhone } from '../../utils/phone'

interface PhoneInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'> {
  label?: string
  error?: string
  /** Canonical digits (`79990000000`), matching what the server stores/expects (US-61). */
  value: string
  /** Fires with the canonical digit string, not the masked display string. */
  onChange: (canonical: string) => void
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
  ({ label, error, value, onChange, className, ...props }, forwardedRef) => {
    const innerRef = useRef<HTMLInputElement>(null)
    useImperativeHandle(forwardedRef, () => innerRef.current as HTMLInputElement)

    const displayValue = maskPhoneInput(value)

    const setCanonicalAndCursor = (nextCanonical: string, cursorDigitIndex: number) => {
      onChange(nextCanonical)
      const nextDisplay = maskPhoneInput(nextCanonical)
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
      const canonical = toCanonicalPhone(value)
      const current = maskPhoneInput(value)

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
      const nextCanonical = toCanonicalPhone(e.target.value)
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
