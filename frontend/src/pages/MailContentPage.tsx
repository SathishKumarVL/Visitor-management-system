import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiErrorMessage, mastersApi } from '../lib/api'
import type { MailContentTemplateDto, MailContentUpsertRequest, MasterItemDto } from '../types/api'
import { Alert, Spinner } from '../components/ui/Panel'
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
  '{{MeetingHall}}',
  '{{Date}}',
  '{{Time}}',
]

/** Only the minutes of meeting email fills these. */
const MINUTES_PLACEHOLDERS = ['{{Discussion}}', '{{Decisions}}', '{{ActionItems}}', '{{NextMeetingDate}}']

const MINUTES_KIND = 5

/** Only the appointment cancellation emails fill this. */
const CANCELLATION_PLACEHOLDERS = ['{{Reason}}']
const CANCELLATION_KINDS = [6, 7]

const KIND_LABELS: Record<number, string> = {
  1: 'Check-in (to employee)',
  2: 'Check-out (to visitor and employee)',
  3: 'Appointment (to employee)',
  4: 'Appointment (to visitor)',
  5: 'Minutes of meeting (to visitor and employee)',
  6: 'Appointment cancelled (to employee)',
  7: 'Appointment cancelled (to visitor)',
  8: 'Visitor invitation (to visitor)',
}

/** The invitation goes out before the visitor's details exist; {{Date}} {{Time}} is when the link expires. */
const INVITATION_KIND = 8
const INVITATION_PLACEHOLDERS = ['{{VisitingPerson}}', '{{Location}}', '{{Department}}', '{{Date}}', '{{Time}}', '{{InviteLink}}']

const placeholdersFor = (kind: number) =>
  kind === INVITATION_KIND
    ? INVITATION_PLACEHOLDERS
    : kind === MINUTES_KIND
    ? [...PLACEHOLDERS, ...MINUTES_PLACEHOLDERS]
    : CANCELLATION_KINDS.includes(kind)
      ? [...PLACEHOLDERS, ...CANCELLATION_PLACEHOLDERS]
      : PLACEHOLDERS

const kindLabel = (item: MailContentTemplateDto) => KIND_LABELS[item.kind] ?? item.kindLabel

/** Settings → Mail content: email templates, tenant default or per location. */
export function MailContentPanel({ lockedLocationId = '' }: { lockedLocationId?: string }) {
  const [items, setItems] = useState<MailContentTemplateDto[]>([])
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const [locationId, setLocationId] = useState(lockedLocationId)
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

  function closeEditor() {
    setEditing(null)
    setForm(null)
  }

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
      await mastersApi.upsertMailContent({ ...form, locationId: locationId || null })
      setMessage('Mail content saved.')
      closeEditor()
      await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="space-y-4">
      {saving ? <LoadingMask label="Saving…" /> : null}
      <div>
        <h2 className="text-sm font-semibold uppercase tracking-[0.08em] text-ink-muted">Mail content</h2>
        <p className="mt-1 text-xs text-ink-muted">
          Subject and body of the check-in, check-out, appointment, cancellation and minutes emails. An appointment
          email re-sent after an edit gets &quot;Updated:&quot; in front of its subject.
        </p>
      </div>
      {error ? <Alert tone="error">{error}</Alert> : null}
      {message ? <Alert tone="success">{message}</Alert> : null}

      <div>
        <FieldLabel htmlFor="mailLocation">Location</FieldLabel>
        <TextSelect
          id="mailLocation"
          value={locationId}
          disabled={!!lockedLocationId}
          onChange={(e) => {
            setLocationId(e.target.value)
            closeEditor()
          }}
        >
          {lockedLocationId ? null : <option value="">Tenant default (all locations)</option>}
          {locations.map((loc) => (
            <option key={loc.id} value={loc.id}>
              {loc.name}
            </option>
          ))}
        </TextSelect>
        <p className="mt-1 text-xs text-ink-muted">
          Visit emails use the visit location’s template when configured; otherwise the tenant default.
        </p>
      </div>

      {loading ? <Spinner label="Loading mail content…" /> : null}
      {!loading && items.length === 0 ? <p className="text-sm text-ink-muted">No mail templates.</p> : null}
      {!loading && items.length > 0 ? (
        <div className="overflow-x-auto rounded-md border border-gray-200">
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
                  <td className="max-w-xs truncate px-4 py-3">{item.subject}</td>
                  <td className="px-4 py-3">{item.isActive ? 'Yes' : 'No'}</td>
                  <td className="px-4 py-3 text-right">
                    <Button size="sm" variant="secondary" onClick={() => startEdit(item)}>Edit</Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination {...pagination} />
        </div>
      ) : null}

      {form && editing ? (
        <form onSubmit={save} className="space-y-3 rounded-md border border-gray-200 p-4">
          <h3 className="font-semibold text-steel">Edit — {kindLabel(editing)}</h3>
          <div>
            <FieldLabel>Subject *</FieldLabel>
            <TextInput required value={form.subject} onChange={(e) => setForm({ ...form, subject: e.target.value })} />
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
            <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
            Active
          </label>
          <p className="text-xs text-ink-muted">
            Placeholders:{' '}
            {placeholdersFor(form.kind).join(', ')}
          </p>
          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
            <Button type="button" variant="secondary" onClick={closeEditor}>Cancel</Button>
          </div>
        </form>
      ) : null}
    </div>
  )
}
