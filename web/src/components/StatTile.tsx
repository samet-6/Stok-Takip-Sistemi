import type { ReactNode } from 'react'

interface StatTileProps {
  label: ReactNode
  value: ReactNode
  /** Optional value colour token (e.g. 'var(--warn)' for low-stock counts). */
  valueColor?: string
}

/** One summary tile. Lay several inside a `.stat-tiles` (or `.stat-tiles cols-3`) grid. */
export function StatTile({ label, value, valueColor }: StatTileProps) {
  return (
    <div className="stat-tile">
      <div className="stat-label">{label}</div>
      <div className="stat-value" style={valueColor ? { color: valueColor } : undefined}>
        {value}
      </div>
    </div>
  )
}
