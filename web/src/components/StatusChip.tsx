import type { ReactNode } from 'react'

export type ChipVariant = 'neutral' | 'ok' | 'warn' | 'crit'

interface StatusChipProps {
  variant: ChipVariant
  children: ReactNode
  /** Small leading dot; on by default. */
  dot?: boolean
}

/**
 * Status pill for the visual system. Colour is reserved for exceptions:
 * neutral = Aktif · ok = Giriş · warn = Düşük · crit = Pasif/Çıkış.
 */
export function StatusChip({ variant, children, dot = true }: StatusChipProps) {
  return (
    <span className={`chip chip-${variant}`}>
      {dot && <span className="chip-dot" />}
      {children}
    </span>
  )
}
