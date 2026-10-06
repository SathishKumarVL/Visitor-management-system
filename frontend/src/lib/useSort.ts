import { useMemo, useState } from 'react'
import type { SortDir } from '../types/api'

export interface SortState {
  key: string
  dir: SortDir
}

/** Clicking the active column flips direction; a new column starts ascending. */
export function nextSort(current: SortState | null, key: string): SortState {
  if (current?.key === key) return { key, dir: current.dir === 'asc' ? 'desc' : 'asc' }
  return { key, dir: 'asc' }
}

function compareValues(a: unknown, b: unknown): number {
  const emptyA = a === null || a === undefined || a === ''
  const emptyB = b === null || b === undefined || b === ''
  if (emptyA || emptyB) return emptyA === emptyB ? 0 : emptyA ? 1 : -1
  if (typeof a === 'number' && typeof b === 'number') return a - b
  if (typeof a === 'boolean' && typeof b === 'boolean') return Number(a) - Number(b)
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' })
}

/**
 * Client-side sorting for lists that are already fully loaded.
 * `accessors` maps a column key to the value it sorts by; empty values always go last.
 */
export function useSort<T>(
  items: T[],
  accessors: Record<string, (item: T) => unknown>,
  initial: SortState | null = null,
) {
  const [sort, setSort] = useState<SortState | null>(initial)

  const sorted = useMemo(() => {
    const accessor = sort ? accessors[sort.key] : undefined
    if (!sort || !accessor) return items
    const factor = sort.dir === 'asc' ? 1 : -1
    return [...items].sort((x, y) => {
      const a = accessor(x)
      const b = accessor(y)
      const emptyA = a === null || a === undefined || a === ''
      const emptyB = b === null || b === undefined || b === ''
      if (emptyA || emptyB) return compareValues(a, b)
      return compareValues(a, b) * factor
    })
  }, [items, sort])

  return {
    sorted,
    sort,
    onSort: (key: string) => setSort((current) => nextSort(current, key)),
  }
}
