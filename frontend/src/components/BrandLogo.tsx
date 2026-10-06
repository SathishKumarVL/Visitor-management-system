import { useSettingsStore } from '../store/settingsStore'
import { cn } from '../lib/utils'

const DEFAULT_LOGO = '/branding/tiaano-logo.png'
const FALLBACK_LOGO = '/branding/tiaano-logo.svg'

export function BrandLogo({
  className,
  light = false,
  src,
  alt,
}: {
  className?: string
  light?: boolean
  /** Overrides the store logo (e.g. a pass printed for another location). */
  src?: string
  alt?: string
}) {
  const logoSrc = useSettingsStore((s) => s.logoSrc)
  const storeCompany = useSettingsStore((s) => s.companyName)
  const company = alt || storeCompany

  return (
    <img
      key={src || logoSrc}
      src={src || logoSrc || DEFAULT_LOGO}
      alt={company}
      className={cn(
        'h-10 w-auto max-w-[220px] object-contain object-left',
        light && 'rounded-md bg-white px-2 py-1',
        className,
      )}
      onError={(e) => {
        const el = e.currentTarget as HTMLImageElement
        // Walk the fallback chain once: configured path → default PNG → SVG shipped in public/.
        if (el.dataset.fallback === 'svg') return
        if (el.dataset.fallback === 'png' || el.src.endsWith(DEFAULT_LOGO)) {
          el.dataset.fallback = 'svg'
          el.src = FALLBACK_LOGO
          return
        }
        el.dataset.fallback = 'png'
        el.src = DEFAULT_LOGO
      }}
    />
  )
}
