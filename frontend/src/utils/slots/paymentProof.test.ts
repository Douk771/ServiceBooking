// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { DEFAULT_PROOF_RULES, PROOF_ACCEPT, formatFileSize, proofFileProblem, splitBySlots } from './paymentProof'

describe('payment proof files', () => {
  it('accept list excludes HEIC so iOS converts to JPEG itself (ARCHITECTURE_CYCLE37.md §37.18 п. 2)', () => {
    expect(PROOF_ACCEPT).toBe('application/pdf,image/jpeg,image/png,image/webp')
    expect(PROOF_ACCEPT).not.toMatch(/heic|heif/i)
  })

  it('refuses an empty file, a file over 10 MB and a foreign type, with the server wording', () => {
    expect(proofFileProblem({ size: 0, type: 'image/png' }, DEFAULT_PROOF_RULES)).toBe('Выберите файл')
    expect(proofFileProblem({ size: 10 * 1024 * 1024 + 1, type: 'application/pdf' }, DEFAULT_PROOF_RULES)).toBe('Файл больше 10 МБ')
    expect(proofFileProblem({ size: 1000, type: 'image/heic' }, DEFAULT_PROOF_RULES)).toBe('Можно приложить PDF, JPEG, PNG или WebP')
    expect(proofFileProblem({ size: 1000, type: 'application/zip' }, DEFAULT_PROOF_RULES)).toBe('Можно приложить PDF, JPEG, PNG или WebP')
  })

  it('lets a file of exactly 10 MB, the four types and an unknown type through (the server checks the bytes)', () => {
    expect(proofFileProblem({ size: 10 * 1024 * 1024, type: 'image/webp' }, DEFAULT_PROOF_RULES)).toBeNull()
    expect(proofFileProblem({ size: 5, type: 'application/pdf' }, DEFAULT_PROOF_RULES)).toBeNull()
    expect(proofFileProblem({ size: 5, type: '' }, DEFAULT_PROOF_RULES)).toBeNull()
  })

  it('takes only as many files as there are free slots (up to 3 per booking)', () => {
    expect(splitBySlots(['a', 'b', 'c', 'd'], 0, 3)).toEqual({ take: ['a', 'b', 'c'], skipped: 1 })
    expect(splitBySlots(['a', 'b'], 2, 3)).toEqual({ take: ['a'], skipped: 1 })
    expect(splitBySlots(['a'], 3, 3)).toEqual({ take: [], skipped: 1 })
  })

  it('formats sizes', () => {
    expect([500, 2048, 1536 * 1024].map(formatFileSize)).toEqual(['500 Б', '2 КБ', '1,5 МБ'])
  })
})
