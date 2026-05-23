import { describe, expect, it } from 'vitest'
import { isPostmortemEditable, postmortemStatusChipClass } from './postmortemStatus'

describe('postmortemStatusChipClass', () => {
  it('returns an amber tone for Draft', () => {
    expect(postmortemStatusChipClass('Draft')).toContain('amber')
  })

  it('returns an emerald tone for Published', () => {
    expect(postmortemStatusChipClass('Published')).toContain('emerald')
  })
})

describe('isPostmortemEditable', () => {
  it('is true for Draft', () => {
    expect(isPostmortemEditable('Draft')).toBe(true)
  })

  it('is false for Published', () => {
    expect(isPostmortemEditable('Published')).toBe(false)
  })
})
