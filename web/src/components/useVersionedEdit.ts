import { useEffect, useState } from 'react'
import { useToast } from './toastContext'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'

interface Versioned {
  id: number
  rowVersion: number
}

/** The row the open form was seeded from; id 0 = the create form. */
interface Baseline {
  id: number
  rowVersion: number
}

/**
 * Create/edit modals over a row with a concurrency token (D10). ProductForm solves the same
 * problem on a full page; this is the modal-sized version, shared so the category and supplier
 * modals cannot drift apart.
 *
 * - The form is seeded once per opening. Callers hand the modal either a snapshot (list pages) or
 *   live query data (detail pages); a later copy of the row must neither wipe what is being typed
 *   nor swap in a version the user never saw — that is exactly how an edit would silently
 *   overwrite someone else's.
 * - A 409 concurrency_conflict keeps the typed values and raises `conflict`. Reloading is the
 *   user's explicit choice, and fetches the row itself, because a snapshot caller has no fresh
 *   copy to give.
 */
export function useVersionedEdit<T extends Versioned>({
  show,
  row,
  apply,
  reload,
  goneMessage,
}: Readonly<{
  show: boolean
  row: T | null
  /** Resets the form to this row, or to the create defaults when null. */
  apply: (row: T | null) => void
  /** Fetches the current server copy of the row; undefined if it no longer exists. */
  reload: (id: number) => Promise<T | undefined>
  goneMessage: string
}>) {
  const { showError } = useToast()
  const [baseline, setBaseline] = useState<Baseline | null>(null)
  const [conflict, setConflict] = useState(false)

  useEffect(() => {
    if (!show) {
      setBaseline(null)
      return
    }
    const id = row?.id ?? 0
    if (baseline?.id === id) return

    apply(row)
    setBaseline({ id, rowVersion: row?.rowVersion ?? 0 })
    setConflict(false)
  }, [show, row, baseline, apply])

  const reloadFromServer = async () => {
    if (!baseline) return
    try {
      const fresh = await reload(baseline.id)
      if (!fresh) {
        showError(goneMessage)
        return
      }
      apply(fresh)
      setBaseline({ id: fresh.id, rowVersion: fresh.rowVersion })
      setConflict(false)
    } catch (err) {
      showError(problemMessage(parseProblemDetails(err)))
    }
  }

  return {
    /** The version the open form was seeded with — what an update must send. */
    rowVersion: baseline?.rowVersion ?? 0,
    conflict,
    markConflict: () => setConflict(true),
    reloadFromServer,
  }
}
