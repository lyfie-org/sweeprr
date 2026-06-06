import type { SparklinePoint } from '../../api/dashboard'

interface SparklineProps {
  points: SparklinePoint[]
  width?: number
  height?: number
}

export function Sparkline({ points, width = 600, height = 80 }: SparklineProps) {
  if (points.length === 0) return null

  const values = points.map(p => p.gbRecovered)
  const max = Math.max(...values, 0.001)
  const pad = { top: 8, bottom: 4, left: 0, right: 0 }
  const chartH = height - pad.top - pad.bottom

  const stepX = points.length > 1 ? width / (points.length - 1) : width

  const toY = (v: number) => pad.top + chartH - (v / max) * chartH

  const pts = points.map((p, i) => ({
    x: i * stepX,
    y: toY(p.gbRecovered),
  }))

  const polyline = pts.map(p => `${p.x},${p.y}`).join(' ')

  // Area fill path: line + close down
  const fillPath = [
    `M${pts[0].x},${pts[0].y}`,
    ...pts.slice(1).map(p => `L${p.x},${p.y}`),
    `L${pts[pts.length - 1].x},${height}`,
    `L${pts[0].x},${height}`,
    'Z',
  ].join(' ')

  const hasData = values.some(v => v > 0)

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      style={{ width: '100%', height: `${height}px`, display: 'block', overflow: 'visible' }}
      aria-hidden="true"
    >
      <defs>
        <linearGradient id="spark-fill" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="var(--accent)" stopOpacity="0.25" />
          <stop offset="100%" stopColor="var(--accent)" stopOpacity="0.02" />
        </linearGradient>
      </defs>

      {hasData && (
        <>
          <path d={fillPath} fill="url(#spark-fill)" />
          <polyline
            points={polyline}
            fill="none"
            stroke="var(--accent)"
            strokeWidth="2"
            strokeLinejoin="round"
            strokeLinecap="round"
          />
          {pts.map((p, i) =>
            points[i].gbRecovered > 0 ? (
              <circle
                key={i}
                cx={p.x}
                cy={p.y}
                r="3"
                fill="var(--accent)"
                opacity="0.9"
              />
            ) : null
          )}
        </>
      )}

      {!hasData && (
        <line
          x1="0"
          y1={height - pad.bottom}
          x2={width}
          y2={height - pad.bottom}
          stroke="var(--glass-border)"
          strokeWidth="1"
          strokeDasharray="4 4"
        />
      )}
    </svg>
  )
}
