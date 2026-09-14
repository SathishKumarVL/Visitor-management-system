/**
 * Location + notes step for visitor registration.
 */
import type { MasterItemDto, VisitorWizardDraft } from '../../types/api'
import { FieldLabel, TextArea, TextInput, SelectChip } from '../ui/Field'

function toggleId(list: string[], id: string): string[] {
  return list.includes(id) ? list.filter((x) => x !== id) : [...list, id]
}

export function validateVisitorLocationStep(
  draft: Pick<VisitorWizardDraft, 'locationIds' | 'plantNumber' | 'otherLocationText'>,
  locations: MasterItemDto[],
): string | null {
  if (draft.locationIds.length === 0) return 'Select at least one location.'
  const selected = locations.filter((l) => draft.locationIds.includes(l.id))
  if (selected.some((l) => l.requiresPlantNumber) && !draft.plantNumber.trim()) {
    return 'Plant number is required.'
  }
  if (selected.some((l) => l.requiresOtherText) && !draft.otherLocationText.trim()) {
    return 'Other location text is required.'
  }
  return null
}

export function VisitorLocationStep({
  draft,
  locations,
  onPatch,
}: {
  draft: Pick<
    VisitorWizardDraft,
    'locationIds' | 'plantNumber' | 'otherLocationText' | 'notes'
  >
  locations: MasterItemDto[]
  onPatch: (partial: Partial<VisitorWizardDraft>) => void
}) {
  const selected = locations.filter((l) => draft.locationIds.includes(l.id))
  const needsPlant = selected.some((l) => l.requiresPlantNumber)
  const needsOther = selected.some((l) => l.requiresOtherText)

  return (
    <div className="space-y-4">
      <p className="text-sm text-ink-muted">Choose where the visitor will go on site.</p>
      <div className="grid gap-2 sm:grid-cols-2">
        {locations.map((l) => (
          <SelectChip
            key={l.id}
            selected={draft.locationIds.includes(l.id)}
            onToggle={() => onPatch({ locationIds: toggleId(draft.locationIds, l.id) })}
          >
            {l.name}
          </SelectChip>
        ))}
      </div>
      {needsPlant ? (
        <div>
          <FieldLabel htmlFor="plantNumber">Plant number *</FieldLabel>
          <TextInput
            id="plantNumber"
            value={draft.plantNumber}
            onChange={(e) => onPatch({ plantNumber: e.target.value })}
          />
        </div>
      ) : null}
      {needsOther ? (
        <div>
          <FieldLabel htmlFor="otherLocationText">Other location *</FieldLabel>
          <TextInput
            id="otherLocationText"
            value={draft.otherLocationText}
            onChange={(e) => onPatch({ otherLocationText: e.target.value })}
          />
        </div>
      ) : null}
      <div>
        <FieldLabel htmlFor="notes">Notes</FieldLabel>
        <TextArea
          id="notes"
          value={draft.notes}
          onChange={(e) => onPatch({ notes: e.target.value })}
          placeholder="Optional visit notes"
        />
      </div>
    </div>
  )
}
