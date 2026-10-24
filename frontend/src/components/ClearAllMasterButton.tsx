import { useState } from 'react'
import { apiErrorMessage, mastersApi } from '../lib/api'
import type { MasterClearSummaryDto, MasterClearType } from '../types/api'
import { hasAnyRole } from '../lib/utils'
import type { useRowSelection } from '../lib/useRowSelection'
import { useAuthStore } from '../store/authStore'
import { Alert, Spinner } from './ui/Panel'
import { Button } from './ui/Button'
import { FieldLabel, TextInput } from './ui/Field'
import { Modal } from './ui/Modal'

const CONFIRM_PHRASE = 'DELETE'

type State =
  | { kind: 'closed' }
  | { kind: 'loading' }
  | { kind: 'confirm'; preview: MasterClearSummaryDto }
  | { kind: 'clearing'; preview: MasterClearSummaryDto }
  | { kind: 'done'; result: MasterClearSummaryDto }

function CountList({ summary }: { summary: MasterClearSummaryDto }) {
  return (
    <ul className="mt-2 space-y-1 text-sm">
      {summary.items.map((i) => (
        <li key={i.label} className="flex justify-between gap-4 rounded-lg bg-gray-50 px-3 py-1.5">
          <span className="capitalize">{i.label}</span>
          <span className="font-semibold tabular-nums">{i.count}</span>
        </li>
      ))}
    </ul>
  )
}

function useIsSuperAdmin() {
  return hasAnyRole(useAuthStore((s) => s.user?.roles) ?? [], ['SuperAdmin'])
}

/**
 * Super Admin only: deletes records of a master for all locations, with everything that depends on them
 * (visits, visitors, logins). Shows what will go first and needs DELETE typed to confirm.
 * Without <code>ids</code> the whole master is cleared.
 */
function MasterDeleteButton({
  type,
  label,
  ids,
  onDone,
}: {
  type: MasterClearType
  label: string
  ids?: string[]
  onDone: () => void
}) {
  const isSuperAdmin = useIsSuperAdmin()
  const [state, setState] = useState<State>({ kind: 'closed' })
  const [typed, setTyped] = useState('')
  const [error, setError] = useState<string | null>(null)

  if (!isSuperAdmin) return null

  const selected = ids !== undefined
  const what = label.toLowerCase()
  const title = selected ? `Delete ${ids.length} selected ${what}` : `Clear all ${what}`

  async function open() {
    setTyped('')
    setError(null)
    setState({ kind: 'loading' })
    try {
      setState({ kind: 'confirm', preview: await mastersApi.clearPreview(type, ids) })
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  }

  async function clear(preview: MasterClearSummaryDto) {
    setError(null)
    setState({ kind: 'clearing', preview })
    try {
      const result = await mastersApi.clearAll(type, typed.trim(), ids)
      setState({ kind: 'done', result })
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e))
      setState({ kind: 'confirm', preview })
    }
  }

  function close() {
    if (state.kind === 'clearing') return
    setState({ kind: 'closed' })
  }

  return (
    <>
      <Button variant="danger" disabled={selected && ids.length === 0} onClick={() => void open()}>
        {selected ? `Delete selected${ids.length ? ` (${ids.length})` : ''}` : 'Clear all'}
      </Button>
      <Modal
        open={state.kind !== 'closed'}
        title={title}
        subtitle="Deletes for every location. This cannot be undone."
        onClose={close}
      >
        {error ? <Alert tone="error">{error}</Alert> : null}
        {state.kind === 'loading' && !error ? <Spinner label="Checking what will be deleted…" /> : null}

        {state.kind === 'confirm' || state.kind === 'clearing' ? (
          state.preview.items.length === 0 ? (
            <p className="text-sm text-ink-muted">There is nothing to delete.</p>
          ) : (
            <div className="space-y-4">
              <div>
                <p className="text-sm font-medium text-ink">This permanently deletes:</p>
                <CountList summary={state.preview} />
                <p className="mt-2 text-xs text-ink-muted">
                  Visits, bookings and visitors that depend on these records are deleted with them, along with their
                  photos and face data. Logins tied to them are deleted too. Super Admin accounts and the built-in
                  &quot;Others&quot; purpose are kept.
                </p>
              </div>
              <Alert tone="warning" persistent>Take a database backup before you continue.</Alert>
              <div>
                <FieldLabel htmlFor="clear-all-confirm">Type {CONFIRM_PHRASE} to confirm</FieldLabel>
                <TextInput
                  id="clear-all-confirm"
                  autoComplete="off"
                  value={typed}
                  disabled={state.kind === 'clearing'}
                  onChange={(e) => setTyped(e.target.value)}
                />
              </div>
              <div className="flex justify-end gap-2">
                <Button variant="secondary" onClick={close} disabled={state.kind === 'clearing'}>
                  Cancel
                </Button>
                <Button
                  variant="danger"
                  disabled={typed.trim() !== CONFIRM_PHRASE || state.kind === 'clearing'}
                  onClick={() => void clear(state.preview)}
                >
                  {state.kind === 'clearing' ? 'Deleting…' : selected ? `Delete selected ${what}` : `Delete all ${what}`}
                </Button>
              </div>
            </div>
          )
        ) : null}

        {state.kind === 'done' ? (
          <div className="space-y-4">
            <p className="text-sm font-medium text-ink">Deleted:</p>
            {state.result.items.length > 0 ? <CountList summary={state.result} /> : <p className="text-sm">Nothing.</p>}
            <div className="flex justify-end">
              <Button onClick={close}>Close</Button>
            </div>
          </div>
        ) : null}
      </Modal>
    </>
  )
}

export function ClearAllMasterButton({
  type,
  label,
  onCleared,
}: {
  type: MasterClearType
  label: string
  onCleared: () => void
}) {
  return <MasterDeleteButton type={type} label={label} onDone={onCleared} />
}

export function DeleteSelectedMasterButton({
  type,
  label,
  ids,
  onDeleted,
}: {
  type: MasterClearType
  label: string
  ids: string[]
  onDeleted: () => void
}) {
  return <MasterDeleteButton type={type} label={label} ids={ids} onDone={onDeleted} />
}

type RowSelection = ReturnType<typeof useRowSelection>

/** Header cell that ticks or unticks every row on the current page. */
export function SelectPageHeader({ selection, pageIds }: { selection: RowSelection; pageIds: string[] }) {
  const ticked = pageIds.filter((id) => selection.selected.has(id)).length
  return (
    <th className="w-10 px-4 py-3">
      <RowCheckbox
        label="Select all on this page"
        checked={pageIds.length > 0 && ticked === pageIds.length}
        indeterminate={ticked > 0 && ticked < pageIds.length}
        onChange={() => selection.togglePage(pageIds)}
      />
    </th>
  )
}

export function SelectRowCell({ selection, id, label }: { selection: RowSelection; id: string; label: string }) {
  return (
    <td className="w-10 px-4 py-3">
      <RowCheckbox label={`Select ${label}`} checked={selection.selected.has(id)} onChange={() => selection.toggle(id)} />
    </td>
  )
}

function RowCheckbox({
  checked,
  indeterminate = false,
  onChange,
  label,
}: {
  checked: boolean
  indeterminate?: boolean
  onChange: () => void
  label: string
}) {
  return (
    <input
      type="checkbox"
      className="h-4 w-4 cursor-pointer accent-primary"
      aria-label={label}
      checked={checked}
      ref={(el) => {
        if (el) el.indeterminate = indeterminate
      }}
      onChange={onChange}
    />
  )
}
