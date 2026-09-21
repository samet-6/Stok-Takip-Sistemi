// Runs once per test file, before the tests in it.
//
// Adds jest-dom's DOM matchers (toBeInTheDocument, toHaveTextContent, …) to vitest's
// expect. Importing the `/vitest` entry point rather than the bare package is what wires
// the TypeScript augmentation as well as the runtime matchers.
import '@testing-library/jest-dom/vitest'

// Node 25 installs its own Web Storage global, and without --localstorage-file that global is
// an inert object: `typeof localStorage === 'object'` but `localStorage.setItem === undefined`.
// It also shadows the working Storage jsdom would otherwise provide (window === globalThis
// here, and jsdom does not overwrite an existing global). Anything persisted through it —
// zustand's `auth-storage` in this app — fails with "storage.setItem is not a function".
// Restoring an in-memory Storage keeps the browser contract without writing a file to disk.
function createMemoryStorage(): Storage {
  const entries = new Map<string, string>()

  return {
    get length() {
      return entries.size
    },
    clear: () => entries.clear(),
    getItem: (key: string) => entries.get(key) ?? null,
    key: (index: number) => [...entries.keys()][index] ?? null,
    removeItem: (key: string) => {
      entries.delete(key)
    },
    setItem: (key: string, value: string) => {
      entries.set(key, String(value))
    },
  } as Storage
}

if (typeof globalThis.localStorage?.setItem !== 'function') {
  Object.defineProperty(globalThis, 'localStorage', {
    value: createMemoryStorage(),
    configurable: true,
    writable: true,
  })
}
