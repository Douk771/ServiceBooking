import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import { UpdateBanner } from './UpdateBanner'

const page = (bundle: string) => `<html><script type="module" src="/assets/${bundle}.js"></script></html>`

function mockIndex(html: string) {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, text: () => Promise.resolve(html) }))
}

async function returnToApp() {
  await act(async () => {
    document.dispatchEvent(new Event('visibilitychange'))
  })
}

describe('UpdateBanner', () => {
  beforeEach(() => {
    const s = document.createElement('script')
    s.type = 'module'
    s.src = '/assets/index-AAAA.js'
    document.head.appendChild(s)
  })
  afterEach(() => {
    document.head.querySelectorAll('script').forEach((s) => s.remove())
    vi.unstubAllGlobals()
  })

  it('stays hidden while the deployed bundle is the loaded one', async () => {
    mockIndex(page('index-AAAA'))
    render(<UpdateBanner />)
    await returnToApp()
    expect(screen.queryByText('Доступна новая версия')).toBeNull()
  })

  it('appears when index.html points to a newer bundle', async () => {
    mockIndex(page('index-BBBB'))
    render(<UpdateBanner />)
    await returnToApp()
    expect(screen.getByText('Доступна новая версия')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Обновить' })).toBeTruthy()
  })

  it('ignores network errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')))
    render(<UpdateBanner />)
    await returnToApp()
    expect(screen.queryByText('Доступна новая версия')).toBeNull()
  })
})
