import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage, mastersApi } from '../lib/api'
import type { MailContentTemplateDto, MailContentUpsertRequest, MasterItemDto } from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextSelect } from '../components/ui/Field'
import { Pagination } from '../components/ui/Pagination'
import { usePagination } from '../lib/usePagination'

const PLACEHOLDERS = [
  '{{Salutation}}',
  '{{VisitorName}}',
  '{{Company}}',
  '{{VisitingPerson}}',
  '{{VisitorNumber}}',
  '{{VisitNumber}}',
  '{{Location}}',
  '{{Department}}',
  '{{Purpose}}',
  '{{MeetingMinutes}}',
  '{{Date}}',
  '{{Time}}',
]

const KIND_LABELS: Record<number, string> = {
  1: 'Check-in (to employee)',
  2: 'Check-out (to visitor and employee)',
  3: 'Appointment (to employee)',
  4: 'Appointment (to visitor)',
}

const kindLabel = (item: MailContentTemplateDto) => KIND_LABELS[item.kind] ?? item.kindLabel

export function MailContentPage() {
  const [items, setItems] = useState<MailContentTemplateDto[]>([])
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const [locationId, setLocationId] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<MailContentTemplateDto | null>(null)
  const [form, setForm] = useState<MailContentUpsertRequest | null>(null)
  const [saving, setSaving] = useState(false)
  const { pageItems, pagination } = usePagination(items, 10, locationId)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await mastersApi.mailContents(locationId || undefined))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [locationId])

  useEffect(() => {
    void mastersApi.locations(true).then(setLocations).catch(() => setLocations([]))
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  function startEdit(item: MailContentTemplateDto) {
    setEditing(item)
    setForm({
      kind: item.kind,
      subject: item.subject,
      body: item.body,
      isActive: item.isActive,
      locationId: locationId || null,
    })
    setMessage(null)
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    if (!form) return
    setSaving(true)
    setMessage(null)
    setError(null)
    try {
      await mastersApi.upsertMailContent({
        ...form,
        locationId: locationId || null,
      })
      setMessage('Mail content saved.')
      setEditing(null)
      setForm(null)
      await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      {saving ? <LoadingMask label="Saving…" /> : null}
      {loading ? <Spinner label="Loading mail content…" /> : null}
      <PageHeader
        title="Mail Content"
        subtitle="Templates for check-in, check-out, and appointment emails — tenant default or per location"
      />
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      <Panel className="mb-4">
        <FieldLabel htmlFor="mailLocation">Location</FieldLabel>
        <TextSelect
          id="mailLocation"
          value={locationId}
          onChange={(e) => {
            setLocationId(e.target.value)
            setEditing(null)
            setForm(null)
          }}
        >
          <option value="">Tenant default (all locations)</option>
          {locations.map((loc) => (
            <option key={loc.id} value={loc.id}>
              {loc.name}
            </option>
          ))}
        </TextSelect>
        <p className="mt-1 text-xs text-ink-muted">
          Visit emails use the visit location’s template when configured; otherwise the tenant default.
        </p>
      </Panel>

      <div className="grid gap-4 xl:grid-cols-[1fr_420px]">
        <Panel className="overflow-x-auto p-0">
          {!loading && items.length === 0 ? (
            <div className="p-4"><EmptyState title="No mail templates" /></div>
          ) : null}
          {!loading && items.length > 0 ? (
            <table className="min-w-full text-left text-sm">
              <thead className="bg-gray-50 text-gray-500">
                <tr>
                  <th className="px-4 py-3">Type</th>
                  <th className="px-4 py-3">Subject</th>
                  <th className="px-4 py-3">Active</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {pageItems.map((item) => (
                  <tr key={item.id} className="border-t border-gray-100">
                    <td className="px-4 py-3 font-medium">{kindLabel(item)}</td>
                    <td className="px-4 py-3 max-w-xs truncate">{item.subject}</td>
                    <td className="px-4 py-3">{item.isActive ? 'Yes' : 'No'}</td>
                    <td className="px-4 py-3 text-right">
                      <Button size="sm" variant="secondary" onClick={() => startEdit(item)}>Edit</Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
          {!loading ? <Pagination {...pagination} /> : null}
        </Panel>

        <Panel>
          {form && editing ? (
            <>
              <h2 className="mb-3 font-semibold text-steel">Edit — {kindLabel(editing)}</h2>
              <form onSubmit={save} className="space-y-3">
                <div>
                  <FieldLabel>Subject *</FieldLabel>
                  <TextInput
                    required
                    value={form.subject}
                    onChange={(e) => setForm({ ...form, subject: e.target.value })}
                  />
                </div>
                <div>
                  <FieldLabel>Body *</FieldLabel>
                  <textarea
                    required
                    rows={12}
                    className="w-full rounded-md border border-gray-200 px-3 py-2 text-sm shadow-sm focus:border-steel focus:outline-none focus:ring-1 focus:ring-steel"
                    value={form.body}
                    onChange={(e) => setForm({ ...form, body: e.target.value })}
                  />
                </div>
                <label className="flex min-h-11 items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={form.isActive}
                    onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                  />
                  Active
                </label>
                <p className="text-xs text-ink-muted">
                  Placeholders:{' '}
                  {PLACEHOLDERS.join(', ')}
                </p>
                <div className="flex flex-wrap gap-2">
                  <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => {
                      setEditing(null)
                      setForm(null)
                    }}
                  >
                    Cancel
                  </Button>
                </div>
              </form>
            </>
          ) : (
            <p className="text-sm text-ink-muted">Select a template to edit its subject and body.</p>
          )}
        </Panel>
      </div>
    </div>
  )
}
