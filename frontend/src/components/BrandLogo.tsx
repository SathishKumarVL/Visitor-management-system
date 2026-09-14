import { useSettingsStore } from '../store/settingsStore'
import { cn } from '../lib/utils'

export function BrandLogo({ className, light = false }: { className?: string; light?: boolean }) {
  const logoSrc = useSettingsStore((s) => s.logoSrc)
  const company = useSettingsStore((s) => s.settings.companyName)

  return (
    <img
      src={logoSrc}
      alt={company}
      className={cn(
        'h-10 w-auto max-w-[220px] object-contain object-left',
        light && 'rounded-md bg-white px-2 py-1',
        className,
      )}
      onError={(e) => {
        const el = e.currentTarget as HTMLImageElement
        if (!el.src.endsWith('/branding/tiaano-logo.png')) {
          el.src = '/branding/tiaano-logo.png'
        }
      }}
    />
  )
}
