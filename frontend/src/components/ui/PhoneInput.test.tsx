import { describe, it, expect } from 'vitest'
import { useState } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PhoneInput } from './PhoneInput'

/** `PhoneInput` is controlled — a thin stateful wrapper lets tests drive it like a real form field. */
function ControlledPhoneInput() {
  const [value, setValue] = useState('')
  return <PhoneInput label="Телефон" value={value} onChange={setValue} />
}

/** Lenient (`restrictToRussia={false}`) mode, as used by the login form (SPEC.md §0.1 Q8). */
function ControlledLenientPhoneInput() {
  const [value, setValue] = useState('')
  return <PhoneInput label="Телефон" value={value} onChange={setValue} restrictToRussia={false} />
}

describe('PhoneInput', () => {
  it('masks digits typed one at a time, starting with 8', async () => {
    const user = userEvent.setup()
    render(<ControlledPhoneInput />)
    const input = screen.getByPlaceholderText('+7 (900) 000-00-00') as HTMLInputElement
    await user.type(input, '89990001122')
    expect(input.value).toBe('+7 (999) 000-11-22')
  })

  it('masks digits typed one at a time, starting with +7', async () => {
    const user = userEvent.setup()
    render(<ControlledPhoneInput />)
    const input = screen.getByPlaceholderText('+7 (900) 000-00-00') as HTMLInputElement
    await user.type(input, '+79990001122')
    expect(input.value).toBe('+7 (999) 000-11-22')
  })

  it('formats a pasted number the same way as typed digits', async () => {
    const user = userEvent.setup()
    render(<ControlledPhoneInput />)
    const input = screen.getByPlaceholderText('+7 (900) 000-00-00') as HTMLInputElement
    await user.click(input)
    await user.paste('8 (999) 000-11-22')
    expect(input.value).toBe('+7 (999) 000-11-22')
  })

  it('Backspace removes a digit, not a stuck separator', async () => {
    const user = userEvent.setup()
    render(<ControlledPhoneInput />)
    const input = screen.getByPlaceholderText('+7 (900) 000-00-00') as HTMLInputElement
    await user.type(input, '89990001122')
    expect(input.value).toBe('+7 (999) 000-11-22')
    input.setSelectionRange(input.value.length, input.value.length)
    await user.keyboard('{Backspace}')
    expect(input.value).toBe('+7 (999) 000-11-2')
    await user.keyboard('{Backspace}')
    expect(input.value).toBe('+7 (999) 000-11')
  })
})

describe('PhoneInput — lenient mode (restrictToRussia={false}, SPEC.md §0.1 Q8)', () => {
  it('keeps the Russian mask for Russian-shaped input, same as the default mode', async () => {
    const user = userEvent.setup()
    render(<ControlledLenientPhoneInput />)
    const input = screen.getByPlaceholderText('+7 (900) 000-00-00') as HTMLInputElement
    await user.type(input, '89990001122')
    expect(input.value).toBe('+7 (999) 000-11-22')
  })

  it('releases the mask once a foreign country code is decided, without corrupting digits already typed', async () => {
    const user = userEvent.setup()
    render(<ControlledLenientPhoneInput />)
    const input = screen.getByPlaceholderText('+7 (900) 000-00-00') as HTMLInputElement
    await user.type(input, '+380671234567')
    expect(input.value).toBe('+380671234567')
  })

  it('does not fold a partially-typed foreign code back into a Russian one as more digits arrive', async () => {
    const user = userEvent.setup()
    render(<ControlledLenientPhoneInput />)
    const input = screen.getByPlaceholderText('+7 (900) 000-00-00') as HTMLInputElement
    // Typed one keystroke at a time — the failure mode this guards against only shows up when the
    // component has to decide "Russian or not?" fresh on every render, not just once at the end.
    await user.type(input, '+3')
    expect(input.value).toBe('+3')
    await user.type(input, '80671234567')
    expect(input.value).toBe('+380671234567')
  })

  it('holds a lone "+" (country code not typed yet) instead of collapsing it to a Russian mask', async () => {
    const user = userEvent.setup()
    render(<ControlledLenientPhoneInput />)
    const input = screen.getByPlaceholderText('+7 (900) 000-00-00') as HTMLInputElement
    await user.type(input, '+')
    expect(input.value).toBe('+')
    await user.type(input, '7')
    // "+7" is Russian-shaped, so it resolves into the mask at this point.
    expect(input.value).toBe('+7')
  })
})
