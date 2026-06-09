import { useEffect, useRef, useState } from 'react'
import { MagnifyingGlass, X } from '@phosphor-icons/react'
import { rulesApi } from '../../api/rules'
import { Spinner } from '../ui'

interface GenreMultiselectProps {
  /** Selected genre labels (comma-joined is how they're stored in Rule.Value). */
  value: string[]
  onChange: (genres: string[]) => void
  disabled?: boolean
}

export function GenreMultiselect({ value, onChange, disabled }: GenreMultiselectProps) {
  const [genres, setGenres] = useState<string[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const containerRef = useRef<HTMLDivElement>(null)

  // Fetch genres on mount
  useEffect(() => {
    setLoading(true)
    setError(null)
    rulesApi
      .getGenres()
      .then(res => setGenres(res.genres))
      .catch(() => setError('Could not load genres'))
      .finally(() => setLoading(false))
  }, [])

  // Close dropdown on outside click
  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false)
        setSearch('')
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [])

  function toggleGenre(genre: string) {
    if (value.includes(genre)) {
      onChange(value.filter(v => v !== genre))
    } else {
      onChange([...value, genre])
    }
  }

  function removeGenre(genre: string) {
    onChange(value.filter(v => v !== genre))
  }

  const filteredGenres = genres.filter(g =>
    g.toLowerCase().includes(search.toLowerCase()),
  )

  return (
    <div className="tag-multiselect" ref={containerRef}>
      {/* Selected pills */}
      <div
        className={`tag-multiselect__pills${open ? ' tag-multiselect__pills--open' : ''}`}
        onClick={() => !disabled && setOpen(true)}
        role="combobox"
        aria-expanded={open}
        aria-haspopup="listbox"
      >
        {value.length === 0 && !open && (
          <span className="tag-multiselect__placeholder">Select genres…</span>
        )}
        {value.map(genre => (
          <span key={genre} className="tag-multiselect__pill">
            {genre}
            {!disabled && (
              <button
                type="button"
                className="tag-multiselect__pill-remove"
                onClick={e => { e.stopPropagation(); removeGenre(genre) }}
                aria-label={`Remove genre ${genre}`}
              >
                <X size={10} weight="bold" />
              </button>
            )}
          </span>
        ))}
        {loading && <Spinner size="sm" />}
      </div>

      {/* Dropdown */}
      {open && (
        <div className="tag-multiselect__dropdown" role="listbox" aria-multiselectable="true">
          <div className="tag-multiselect__search">
            <MagnifyingGlass size={13} />
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search genres…"
              className="tag-multiselect__search-input"
              autoFocus
            />
          </div>
          {error && <div className="tag-multiselect__error">{error}</div>}
          {!loading && filteredGenres.length === 0 && !error && (
            <div className="tag-multiselect__empty">No genres found</div>
          )}
          {filteredGenres.map(genre => {
            const selected = value.includes(genre)
            return (
              <button
                key={genre}
                type="button"
                role="option"
                aria-checked={selected}
                className={`tag-multiselect__option${selected ? ' tag-multiselect__option--selected' : ''}`}
                onClick={() => toggleGenre(genre)}
              >
                <span className="tag-multiselect__option-check">{selected ? '✓' : ''}</span>
                {genre}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
