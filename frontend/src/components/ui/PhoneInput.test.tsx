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
