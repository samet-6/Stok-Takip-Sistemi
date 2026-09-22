import { useId, useState } from 'react'
import { Dropdown } from 'react-bootstrap'

export interface SelectOption {
  value: string
  label: string
  /** Second, smaller line — shown in the open list only, never in the closed box. */
  description?: string
  disabled?: boolean
}

/** 18rem: tall enough for about four two-line rows. */
const MENU_MAX_HEIGHT = 288
/** Gap kept between the menu's bottom edge and the window's, so it never touches it. */
const VIEWPORT_MARGIN = 8

/**
 * The room left under the box, capped at the menu's normal height. The menu gets exactly this
 * much and scrolls inside it — so opening it can never make the page taller or scrollable.
 */
function menuHeightFor(toggleId: string): number {
  const bottom = document.getElementById(toggleId)?.getBoundingClientRect().bottom ?? 0
  return Math.min(MENU_MAX_HEIGHT, window.innerHeight - bottom - VIEWPORT_MARGIN)
}

/**
 * The app's select box. A native <select> can only show one line of plain text per option and
 * its open list is drawn by the browser, so it looked different from everything around it —
 * and two suppliers may share a name (D9c), which a single line cannot tell apart. Closed, this
 * is one line tall like a native select; open, each row can carry a description underneath.
 */
export function SelectBox({
  id,
  options,
  value,
  onChange,
  onBlur,
  placeholder = '',
  isInvalid,
}: Readonly<{
  id?: string
  options: SelectOption[]
  value: string
  onChange: (value: string) => void
  onBlur?: () => void
  /** Shown while no option matches the value. */
  placeholder?: string
  isInvalid?: boolean
}>) {
  const fallbackId = useId()
  const toggleId = id ?? fallbackId
  const selected = options.find((o) => o.value === value)
  const [menuHeight, setMenuHeight] = useState(MENU_MAX_HEIGHT)

  return (
    <Dropdown
      onSelect={(key) => key !== null && onChange(key)}
      onToggle={(open) => {
        // Measured at the moment of opening: the box's position depends on scroll and
        // window size, both of which may have changed since the last time.
        if (open) setMenuHeight(menuHeightFor(toggleId))
        else onBlur?.()
      }}
    >
      {/* bsPrefix swaps the dropdown-toggle class (and its caret) for form-select, so the closed
          control looks and sizes like a native select. It is still a .btn underneath, though:
          fw-normal undoes the theme's bold button text, and the right padding is set back to the
          select's own so a long label ends in "…" before the arrow instead of running under it. */}
      <Dropdown.Toggle
        id={toggleId}
        bsPrefix="form-select"
        variant=""
        className={`text-start fw-normal text-truncate${isInvalid ? ' is-invalid' : ''}`}
        style={{ paddingRight: '2.25rem' }}
      >
        {selected ? selected.label : placeholder}
      </Dropdown.Toggle>

      {/* flip={false}: always downwards, like a native select. Left on, Popper turned the menu
          upwards whenever the room below was a few pixels short — so a slight scroll flipped it
          back and forth. minWidth rather than a fixed width: a list wider than its box (a long
          product name under a narrow filter) grows instead of being cut. */}
      <Dropdown.Menu
        className="overflow-auto"
        style={{ minWidth: '100%', maxHeight: `${menuHeight}px` }}
        flip={false}
      >
        {options.map((o) => (
          <Dropdown.Item
            key={o.value}
            as="button"
            type="button"
            eventKey={o.value}
            active={o.value === value}
            disabled={o.disabled}
          >
            <div>{o.label}</div>
            {o.description && <div className="small opacity-75">{o.description}</div>}
          </Dropdown.Item>
        ))}
      </Dropdown.Menu>
    </Dropdown>
  )
}
