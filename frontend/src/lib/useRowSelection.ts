import { useCallback, useEffect, useMemo, useState } from 'react'

/** Ticked rows of a table. Ids that are no longer listed (deleted, filtered out) drop out of the selection. */
export function useRowSelection(allIds: string[]) {
  const [selected, setSelected] = useState<Set<string>>(() => new Set())
  const key = allIds.join('|')

  useEffect(() => {
    const listed = new Set(key ? key.split('|') : [])
    setSelected((prev) => {
      const next = new Set([...prev].filter((id) => listed.has(id)))
      return next.size === prev.size ? prev : next
    })
  }, [key])

  const toggle = useCallback((id: string) => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }, [])

  /** Header checkbox: ticks every row on the page, or unticks them all when they are already ticked. */
  const togglePage = useCallback((pageIds: string[]) => {
    setSelected((prev) => {
      const next = new Set(prev)
      const allTicked = pageIds.length > 0 && pageIds.every((id) => next.has(id))
      for (const id of pageIds) {
        if (allTicked) next.delete(id)
        else next.add(id)
      }
      return next
    })
  }, [])

  const clear = useCallback(() => setSelected(new Set()), [])
  const ids = useMemo(() => [...selected], [selected])

  return { selected, ids, toggle, togglePage, clear }
}
