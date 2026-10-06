import { useMemo, useState } from 'react'

/**
 * Client-side paging for lists that are already fully loaded.
 * Changing `resetKey` (e.g. a filter value) jumps back to page 1.
 */
export function usePagination<T>(items: T[], initialPageSize = 10, resetKey?: unknown) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSizeState] = useState(initialPageSize)
  const [lastResetKey, setLastResetKey] = useState(resetKey)

  if (!Object.is(lastResetKey, resetKey)) {
    setLastResetKey(resetKey)
    setPage(1)
  }

  const total = items.length
  const totalPages = Math.max(1, Math.ceil(total / pageSize))
  const current = Math.min(page, totalPages)

  const pageItems = useMemo(
    () => items.slice((current - 1) * pageSize, current * pageSize),
    [items, current, pageSize],
  )

  function setPageSize(next: number) {
    setPageSizeState(next)
    setPage(1)
  }

  return {
    pageItems,
    pagination: { page: current, pageSize, total, onPageChange: setPage, onPageSizeChange: setPageSize },
  }
}
