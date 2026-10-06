import { cn } from '../../lib/utils'
import { useI18n } from '../../i18n'

export const PAGE_SIZE_OPTIONS = [10, 20, 50, 100]

type PageToken = number | 'gap-start' | 'gap-end'

function pageTokens(page: number, totalPages: number): PageToken[] {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, i) => i + 1)
  const start = Math.max(2, page - 1)
  const end = Math.min(totalPages - 1, page + 1)
  const tokens: PageToken[] = [1]
  if (start > 2) tokens.push('gap-start')
  for (let p = start; p <= end; p++) tokens.push(p)
  if (end < totalPages - 1) tokens.push('gap-end')
  tokens.push(totalPages)
  return tokens
}

const pageButton =
  'inline-flex min-h-9 min-w-9 items-center justify-center rounded-lg border px-2.5 text-sm font-medium transition duration-200 disabled:cursor-not-allowed disabled:opacity-40'

export function Pagination({
  page,
  pageSize,
  total,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions = PAGE_SIZE_OPTIONS,
  bordered = true,
  className,
}: {
  page: number
  pageSize: number
  total: number
  onPageChange: (page: number) => void
  onPageSizeChange?: (pageSize: number) => void
  pageSizeOptions?: number[]
  /** Top divider, for use directly under a table inside the same panel. */
  bordered?: boolean
  /** Replaces the default horizontal padding (`px-4`). */
  className?: string
}) {
  const { t } = useI18n()
  if (total <= 0) return null

  const totalPages = Math.max(1, Math.ceil(total / pageSize))
  const current = Math.min(Math.max(1, page), totalPages)
  const from = (current - 1) * pageSize + 1
  const to = Math.min(total, current * pageSize)
  const showSizeSelect = !!onPageSizeChange && total > Math.min(...pageSizeOptions)

  return (
    <nav
      aria-label={t('pagination.label')}
      className={cn(
        'flex flex-col gap-3 py-3 text-sm sm:flex-row sm:items-center sm:justify-between',
        bordered && 'border-t border-gray-100',
        className ?? 'px-4',
      )}
    >
      <div className="flex flex-wrap items-center gap-3 text-ink-muted">
        <span>
          {t('pagination.showing')
            .replace('{from}', String(from))
            .replace('{to}', String(to))
            .replace('{total}', String(total))}
        </span>
        {showSizeSelect ? (
          <label className="flex items-center gap-2">
            <span>{t('pagination.rowsPerPage')}</span>
            <select
              className="min-h-9 rounded-lg border border-border bg-white px-2 text-sm text-ink focus:border-primary focus:ring-2 focus:ring-primary/20"
              value={pageSize}
              onChange={(e) => onPageSizeChange?.(Number(e.target.value))}
            >
              {pageSizeOptions.map((size) => (
                <option key={size} value={size}>{size}</option>
              ))}
            </select>
          </label>
        ) : null}
      </div>

      {totalPages > 1 ? (
        <div className="flex flex-wrap items-center gap-1.5">
          <button
            type="button"
            className={cn(pageButton, 'border-border bg-white text-ink hover:bg-aqua-light/60')}
            disabled={current <= 1}
            onClick={() => onPageChange(current - 1)}
          >
            {t('pagination.previous')}
          </button>
          {pageTokens(current, totalPages).map((token) =>
            typeof token === 'number' ? (
              <button
                key={token}
                type="button"
                aria-current={token === current ? 'page' : undefined}
                className={cn(
                  pageButton,
                  token === current
                    ? 'border-transparent bg-brand-gradient text-white shadow-sm'
                    : 'border-border bg-white text-ink hover:bg-aqua-light/60',
                )}
                onClick={() => onPageChange(token)}
              >
                {token}
              </button>
            ) : (
              <span key={token} className="px-1 text-ink-muted" aria-hidden>…</span>
            ),
          )}
          <button
            type="button"
            className={cn(pageButton, 'border-border bg-white text-ink hover:bg-aqua-light/60')}
            disabled={current >= totalPages}
            onClick={() => onPageChange(current + 1)}
          >
            {t('pagination.next')}
          </button>
        </div>
      ) : null}
    </nav>
  )
}
