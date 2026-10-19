import { describe, it, expect } from 'vitest'
import { transportFromConnectParam } from './connectParam'

describe('transportFromConnectParam', () => {
  it.each([['WhatsApp', 'WhatsApp'], ['whatsapp', 'WhatsApp'], ['Max', 'Max'], [' MAX ', 'Max']])('%s', (raw, t) => expect(transportFromConnectParam(raw)).toBe(t))
  it.each([[null], [''], ['Telegram'], ['Sms']])('%s is ignored', (raw) => expect(transportFromConnectParam(raw as string | null)).toBeNull())
})
