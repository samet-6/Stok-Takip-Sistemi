// Runs once per test file, before the tests in it.
//
// Adds jest-dom's DOM matchers (toBeInTheDocument, toHaveTextContent, …) to vitest's
// expect. Importing the `/vitest` entry point rather than the bare package is what wires
// the TypeScript augmentation as well as the runtime matchers.
import '@testing-library/jest-dom/vitest'
