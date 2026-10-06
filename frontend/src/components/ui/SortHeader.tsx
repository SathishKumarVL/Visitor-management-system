import type { ReactNode } from 'react'
import type { SortState } from '../../lib/useSort'
import { cn } from '../../lib/utils'

interface SortHeaderProps {
  sortKey: string
  sort: SortState | null
  onSort: (key: string) => void
  children: ReactNode
  className?: string
  align?: 'left' | 'right'
}

/** Table header cell that toggles sorting on its column. */
export function SortHeader({ sortKey, sort, onSort, children, className, align = 'left' }: SortHeaderProps) {
  const active = sort?.key === sortKey
  const dir = active ? sort.dir : null
  return (
    <th
      className={cn('px-4 py-3 font-medium', className)}
      aria-sort={dir === 'asc' ? 'ascending' : dir === 'desc' ? 'descending' : 'none'}
    >
      <button
        type="button"
        onClick={() => onSort(sortKey)}
        className={cn(
          'inline-flex items-center gap-1 rounded hover:text-ink focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40',
          align === 'right' && 'flex-row-reverse',
          active && 'text-ink',
        )}
      >
        {children}
        <span aria-hidden className={active ? 'text-primary' : 'text-gray-300'}>
          {dir === 'asc' ? '▲' : dir === 'desc' ? '▼' : '↕'}
        </span>
      </button>
    </th>
  )
}
