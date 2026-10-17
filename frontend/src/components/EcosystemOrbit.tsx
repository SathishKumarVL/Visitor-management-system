import { cn } from '../lib/utils'

const COMPANIES = [
  { code: 'Ti', name: 'Ti Anode', color: '#0F5FA8' },
  { code: 'Aq', name: 'Aquafil', color: '#0E8FA0' },
  { code: 'Fb', name: 'Fibtec', color: '#148A4B' },
  { code: 'Sx', name: 'ScaleX', color: '#12A6E0' },
  { code: 'Vy', name: 'Vidyashrm', color: '#1E4FD8' },
  { code: 'Fn', name: 'Foundation', color: '#B4540A' },
  { code: 'Ny', name: 'Nayagi', color: '#D2232A' },
]

const SIZE = 560
const CENTER = SIZE / 2
const ORBIT = 200
const TILE = 64

/** Tiaano Smart CCU with the group companies around it. Transparent background; labels suit a dark panel. */
export function EcosystemOrbit({ className }: { className?: string }) {
  return (
    <svg
      viewBox={`0 0 ${SIZE} ${SIZE}`}
      className={cn('h-auto w-full', className)}
      role="img"
      aria-label={`Tiaano Smart CCU connecting ${COMPANIES.map((c) => c.name).join(', ')}`}
    >
      <defs>
        <filter id="orbit-shadow" x="-30%" y="-30%" width="160%" height="160%">
          <feDropShadow dx="0" dy="8" stdDeviation="10" floodColor="#00262b" floodOpacity="0.28" />
        </filter>
      </defs>

      {[150, 200, 250].map((r) => (
        <circle key={r} cx={CENTER} cy={CENTER} r={r} fill="none" stroke="rgba(255,255,255,0.22)" strokeWidth="1.2" />
      ))}

      {COMPANIES.map((c, i) => {
        const angle = (-90 + (360 / COMPANIES.length) * i) * (Math.PI / 180)
        const x = CENTER + ORBIT * Math.cos(angle)
        const y = CENTER + ORBIT * Math.sin(angle)
        return (
          <g key={c.code}>
            <rect
              x={x - TILE / 2}
              y={y - TILE / 2 - 10}
              width={TILE}
              height={TILE}
              rx="14"
              fill={c.color}
              filter="url(#orbit-shadow)"
            />
            <text
              x={x}
              y={y - 10}
              textAnchor="middle"
              dominantBaseline="central"
              fill="#fff"
              fontSize="24"
              fontWeight="700"
            >
              {c.code}
            </text>
            <text x={x} y={y + TILE / 2 + 8} textAnchor="middle" fill="rgba(255,255,255,0.9)" fontSize="14" fontWeight="600">
              {c.name}
            </text>
          </g>
        )
      })}

      <g filter="url(#orbit-shadow)">
        <rect x={CENTER - 88} y={CENTER - 88} width="176" height="176" rx="24" fill="#fff" stroke="#F8B4B8" strokeWidth="3" />
      </g>
      <polygon
        points={[
          [CENTER + 22, CENTER - 62],
          [CENTER - 14, CENTER - 22],
          [CENTER + 2, CENTER - 22],
          [CENTER - 30, CENTER + 8],
          [CENTER + 30, CENTER - 34],
          [CENTER + 12, CENTER - 34],
          [CENTER + 34, CENTER - 62],
        ]
          .map((p) => p.join(','))
          .join(' ')}
        fill="#E11D2A"
      />
      <text x={CENTER} y={CENTER + 36} textAnchor="middle" fill="#0B1F33" fontSize="24" fontWeight="700">
        Tiaano
      </text>
      <text x={CENTER} y={CENTER + 62} textAnchor="middle" fill="#7C8CA3" fontSize="13" letterSpacing="3">
        SMART · CCU
      </text>
    </svg>
  )
}
