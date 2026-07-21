import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import type { ProblemDetails } from '../types/api'

/**
 * Maps ASP.NET ProblemDetails.errors (keys are PascalCase property names) onto
 * react-hook-form fields, case-insensitively. Returns true if anything matched.
 */
export function applyServerFieldErrors<T extends FieldValues>(
  problem: ProblemDetails,
  setError: UseFormSetError<T>,
  fields: readonly Path<T>[],
): boolean {
  if (!problem.errors) return false
  let matched = false
  for (const [key, messages] of Object.entries(problem.errors)) {
    const field = fields.find((f) => f.toLowerCase() === key.toLowerCase())
    if (field && messages.length > 0) {
      setError(field, { type: 'server', message: messages.join(' ') })
      matched = true
    }
  }
  return matched
}
