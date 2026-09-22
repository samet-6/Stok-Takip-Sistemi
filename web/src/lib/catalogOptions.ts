import type { SelectOption } from '../components/SelectBox'

/**
 * Categories and suppliers as select options, with the one rule they share: a passive record
 * is marked and cannot be picked — except the product's current one in edit mode, so a product
 * whose category or supplier went passive stays editable instead of losing it.
 */
export function catalogOptions<T extends { id: number; name: string; isActive: boolean }>(
  items: T[],
  currentId: number | undefined,
  describe?: (item: T) => string,
): SelectOption[] {
  return items.map((item) => ({
    value: String(item.id),
    label: item.isActive ? item.name : `${item.name} (Pasif)`,
    description: describe?.(item),
    disabled: !item.isActive && item.id !== currentId,
  }))
}
