import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { passwordRules } from '../lib/schemas'

// The very file the .NET suite reads (tests/fixtures/password-vectors.json). Keeping the rows
// outside both suites is what makes this a contract rather than two independent opinions: if the
// backend policy moves and this schema does not, these rows go red here.
// Resolved from the Vitest working directory (web/), which is where npm scripts run it from.
// A wrong path fails loudly here rather than silently skipping the contract.
const VECTOR_FILE = resolve(process.cwd(), '../tests/fixtures/password-vectors.json')

const vectors = JSON.parse(readFileSync(VECTOR_FILE, 'utf8')) as {
  requiredLength: number
  cases: { password: string; valid: boolean; why: string }[]
}

describe('Parola politikası — backend ile ortak vektörler', () => {
  it.each(vectors.cases)('$why → geçerli mi: $valid', ({ password, valid }) => {
    expect(passwordRules.safeParse(password).success).toBe(valid)
  })

  it('uzunluk kuralı vektör dosyasındaki sayıyla aynı', () => {
    // Aa1! plus filler: satisfies every rule except length, so only the length can reject it.
    const oneShort = 'Aa1!'.padEnd(vectors.requiredLength - 1, 'x')

    expect(oneShort).toHaveLength(vectors.requiredLength - 1)
    expect(passwordRules.safeParse(oneShort).success).toBe(false)
    expect(passwordRules.safeParse(oneShort + 'x').success).toBe(true)
  })
})
