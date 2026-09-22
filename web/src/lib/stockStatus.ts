import type { ChipVariant } from '../components/StatusChip'

/**
 * The stock chip's verdict, in one place for every table and page that shows it. Zero is its
 * own state, not the bottom of "low": the bell already calls it "Tükendi", so the chip does too.
 * The threshold is inclusive, matching the backend's low-stock rule. Null means no chip.
 */
export function stockStatus(
  stockQuantity: number,
  minStockLevel: number,
): { variant: ChipVariant; label: string } | null {
  if (stockQuantity === 0) return { variant: 'crit', label: 'Tükendi' }
  if (stockQuantity <= minStockLevel) return { variant: 'warn', label: 'Düşük' }
  return null
}
