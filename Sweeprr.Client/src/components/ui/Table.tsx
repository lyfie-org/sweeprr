import './Table.css'

interface TableProps {
  children: React.ReactNode
  className?: string
}

export function Table({ children, className = '' }: TableProps) {
  return (
    <div className={`table-container ${className}`}>
      <table className="table">{children}</table>
    </div>
  )
}

export function TableHead({ children }: { children: React.ReactNode }) {
  return <thead className="table__head">{children}</thead>
}

export function TableBody({ children }: { children: React.ReactNode }) {
  return <tbody>{children}</tbody>
}

export function TableRow({
  children,
  className = '',
}: {
  children: React.ReactNode
  className?: string
}) {
  return <tr className={`table__row ${className}`}>{children}</tr>
}

export function TableCell({
  children,
  className = '',
}: {
  children: React.ReactNode
  className?: string
}) {
  return <td className={`table__cell ${className}`}>{children}</td>
}

export function TableHeaderCell({
  children,
  className = '',
}: {
  children: React.ReactNode
  className?: string
}) {
  return <th className={`table__header-cell ${className}`}>{children}</th>
}

export function TableEmpty({ children = 'No items to display.' }: { children?: React.ReactNode }) {
  return (
    <tr>
      <td colSpan={9999} className="table__empty">
        {children}
      </td>
    </tr>
  )
}
