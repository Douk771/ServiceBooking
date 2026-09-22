import { forwardRef, useImperativeHandle, useRef, type InputHTMLAttributes } from 'react'
import { Input } from './Input'
import { maskPhoneInput, toCanonicalPhone, looksRussian } from '../../utils/phone'

interface PhoneInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'> {
  label?: string
  error?: string
  /**
   * What this holds depends on `restrictToRussia` and, when it's `false`, on what's been typed so
   * far — see that prop's doc comment.
   */
  value: string
  /** Fires with the value described under `restrictToRussia` below. */
  onChange: (canonical: string) => void
  /**
   * Defaults to `true` — the Russian-only input policy (US-61, §48.1/§48.2) applies to forms that
   * *create* a phone number (registration, profile phone change). `value`/`onChange` carry the
   * canonical digit string (`79990000000`) and the field always shows the live `+7 (900) 000-00-00`
   * mask.
   *
   * `false` is the sign-in form's mode (SPEC.md §0.1 Q8): an account may already have a foreign
   * number on file (the server's `AuthController.Login` deliberately still normalizes with
   * `Normalize`, not `TryNormalizeRussian`, §48.2), so the field can't reject non-Russian input
   * outright the way registration does. Per the customer's answer, it also can't just drop the mask
   * for everyone — most users still expect to see `+7 (900) 000-00-00` as they type. So in this mode
   * the mask applies only while the input still "looks Russian" ({@link looksRussian}): no country
   * code yet, or a `7`/`8` one. The moment a different country code appears (e.g. `+380…`), the field
   * switches to plain unmangled text for the rest of that session and `value`/`onChange` carry the
   * raw typed text instead of canonical digits — the server extracts digits from arbitrary text
   * itself (`PhoneNormalizer.Normalize`), so no client-side normalization is needed here at all.
   *
   * Why the mode can't just be recomputed from scratch on every keystroke: a controlled mask that
   * re-derives "is this Russian?" purely from the latest digits would see `+3` (while typing
   * `+380…`) as "not enough digits yet, keep masking" and silently rewrite it back into `+7 (3XX)…`
   * on the very next render — exactly the bug this mode exists to avoid. Instead, `looksRussian`
   * treats a `+` with a not-yet-typed country code as genuinely undecided (kept masked) and a `+`
   * with a decided non-`7`/`8` code as final (mask released), and once released the raw text is
   * carried through `value` as-is (not round-tripped through the canonical-digit mask logic again),
   * so it can never be folded back.
   */
  restrictToRussia?: boolean
}

/**
 * Masked phone input for US-61 (`ARCHITECTURE.md` §48.3): displays `+7 (900) 000-00-00` while the
 * caller keeps working with the canonical digit string. Wraps the existing `Input` — a second
 * phone-formatting component would violate the "one function" rule from the same section.
 *
 * `Backspace`/`Delete` remove the digit adjacent to the cursor, not whatever character happens to be
 * there — without this, deleting into a `)` or `-` visually "sticks" because re-masking recreates the
 * same separator right back.
 */
export const PhoneInput = forwardRef<HTMLInputElement, PhoneInputProps>(
  ({ label, error, value, onChange, className, restrictToRussia = true, ...props }, forwardedRef) => {
    const innerRef = useRef<HTMLInputElement>(null)
    useImperativeHandle(forwardedRef, () => innerRef.current as HTMLInputElement)

    // In lenient (`restrictToRussia={false}`) mode, once the current value has settled on a foreign
    // country code, it's carried as raw text (see the prop doc comment) — recognizable because it's
    // not a plain digit string.
    const isForeignPassthrough = !restrictToRussia && value !== '' && !/^\d*$/.test(value)

    if (isForeignPassthrough) {
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
      const raw = e.target.value

      // Lenient mode only: release the mask — passing the raw text through untouched — as soon as
      // either (a) a foreign country code has been decided (`looksRussian` is false), or (b) a bare
      // `+` with no digits yet has been typed. (b) matters on its own: without it, a lone `+` would
      // canonicalize to `''` (no digits to work with) and the display would go blank, so the very
      // next keystroke starts from "nothing typed" rather than continuing after the `+` — that's the
      // "hold the plus while the code isn't typed yet" state the mode needs to survive.
      if (!restrictToRussia) {
        const digits = raw.replace(/\D/g, '')
        const bareLeadingPlus = raw.trim().startsWith('+') && digits.length === 0
        if (bareLeadingPlus || !looksRussian(raw)) {
          onChange(raw)
          return
        }
      }

      onChange(toCanonicalPhone(raw))
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
