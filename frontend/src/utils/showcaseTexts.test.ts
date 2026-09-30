import { describe, it, expect } from 'vitest'
import { resolveShowcaseText, SHOWCASE_FALLBACK_TEXTS } from './showcaseTexts'
import type { LegalText } from '../types'

function live(contentHtml: string): LegalText {
  return { key: 'ShowcaseNotice', version: '2026-10-01', isDraft: false, contentHtml }
}

describe('resolveShowcaseText (API_CONTRACT_CYCLE28.md §600)', () => {
  it('fallback texts are the §600 wording, verbatim', () => {
    expect(SHOWCASE_FALLBACK_TEXTS.ShowcaseNotice).toBe(
      'Это пример страницы салона: компания вымышленная и показана, чтобы продемонстрировать возможности сервиса. ' +
        'Запись здесь не означает настоящего визита, салон не свяжется с вами. ' +
        'Данные такой записи удаляются автоматически в течение суток.',
    )
    expect(SHOWCASE_FALLBACK_TEXTS.ShowcaseBookingClosed).toBe(
      'Это пример страницы салона: компания вымышленная, запись к ней не принимается.',
    )
    expect(SHOWCASE_FALLBACK_TEXTS.DemoBanner).toBe(
      'Демо-версия. Данные удаляются каждую ночь. Не вводите настоящие имена и телефоны.',
    )
  })

  it('404 / error / still loading (no live text) → fallback, not empty', () => {
    for (const absent of [undefined, null]) {
      const r = resolveShowcaseText('ShowcaseNotice', absent)
      expect(r).toEqual({ text: SHOWCASE_FALLBACK_TEXTS.ShowcaseNotice, html: null, isLive: false })
    }
  })

  it('takes the "Текст" section and drops the service note that precedes it', () => {
    const r = resolveShowcaseText(
      'ShowcaseNotice',
      live('<p>Служебная справка для юриста.</p><h2>Текст</h2><p>Пример <b>салона</b>.</p>'),
    )
    expect(r.isLive).toBe(true)
    expect(r.html).toBe('<p>Пример <b>салона</b>.</p>')
    expect(r.text).toBe('Пример салона .')
    expect(r.text).not.toContain('юриста')
  })

  it('a live text without a "Текст" heading is shown whole', () => {
    const r = resolveShowcaseText('ShowcaseBookingClosed', live('<p>Запись не принимается.</p>'))
    expect(r).toMatchObject({ isLive: true, html: '<p>Запись не принимается.</p>', text: 'Запись не принимается.' })
  })

  it('a live text that is empty after stripping tags falls back instead of showing a blank note', () => {
    const r = resolveShowcaseText('ShowcaseNotice', live('<h2>Текст</h2><p>&nbsp;</p>'))
    expect(r.isLive).toBe(false)
    expect(r.text).toBe(SHOWCASE_FALLBACK_TEXTS.ShowcaseNotice)
  })

  it('decodes HTML entities so «&laquo;» never reaches the screen or the screen reader literally', () => {
    const r = resolveShowcaseText(
      'ShowcaseBookingClosed',
      live('<p>&laquo;Пример&raquo; &mdash; салон &amp; студия&nbsp;&#8470;1.</p>'),
    )
    expect(r.text).toBe('«Пример» — салон & студия №1.')
  })

  it('an escaped tag stays text and is not turned into markup', () => {
    const r = resolveShowcaseText('ShowcaseNotice', live('<p>a &lt;b&gt; c</p>'))
    expect(r.text).toBe('a <b> c')
  })
})
