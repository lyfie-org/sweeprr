import { useEffect, useRef, useState } from 'react'
import { MagnifyingGlass, Tag, X } from '@phosphor-icons/react'
import { rulesApi, type TagDto } from '../../api/rules'
import { Spinner } from '../ui'

interface TagMultiselectProps {
  /** ID of the Radarr/Sonarr connection to fetch tags from. null = no connection chosen. */
  connectionId: number | null
  /** Selected tag labels (comma-joined is how they're stored in Rule.Value). */
  value: string[]
  onChange: (tags: string[]) => void
  disabled?: boolean
}

export function TagMultiselect({ connectionId, value, onChange, disabled }: TagMultiselectProps) {
  const [tags, setTags] = useState<TagDto[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const containerRef = useRef<HTMLDivElement>(null)

  // Fetch tags whenever connectionId changes
  useEffect(() => {
    if (connectionId === null) {
      setTags([])
      setError(null)
      return
    }
    setLoading(true)
    setError(null)
    rulesApi
      .getTags(connectionId)
      .then(res => setTags(res.tags))
      .catch(() => setError('Could not load tags'))
      .finally(() => setLoading(false))
  }, [connectionId])

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

  function toggleTag(label: string) {
    if (value.includes(label)) {
      onChange(value.filter(v => v !== label))
    } else {
      onChange([...value, label])
    }
  }

  function removeTag(label: string) {
    onChange(value.filter(v => v !== label))
  }

  const filteredTags = tags.filter(t =>
    t.label.toLowerCase().includes(search.toLowerCase()),
  )

  if (connectionId === null) {
    return (
      <div className="tag-multiselect tag-multiselect--no-conn">
        <Tag size={14} weight="duotone" />
        <span>Select a Radarr/Sonarr connection first</span>
      </div>
    )
  }

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
          <span className="tag-multiselect__placeholder">Select tags…</span>
        )}
        {value.map(label => (
          <span key={label} className="tag-multiselect__pill">
            {label}
            {!disabled && (
              <button
                type="button"
                className="tag-multiselect__pill-remove"
                onClick={e => { e.stopPropagation(); removeTag(label) }}
                aria-label={`Remove tag ${label}`}
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
              placeholder="Search tags…"
              className="tag-multiselect__search-input"
              autoFocus
            />
          </div>
          {error && <div className="tag-multiselect__error">{error}</div>}
          {!loading && filteredTags.length === 0 && !error && (
            <div className="tag-multiselect__empty">No tags found</div>
          )}
          {filteredTags.map(tag => {
            const selected = value.includes(tag.label)
            return (
              <button
                key={tag.id}
                type="button"
                role="option"
                aria-checked={selected}
                className={`tag-multiselect__option${selected ? ' tag-multiselect__option--selected' : ''}`}
                onClick={() => toggleTag(tag.label)}
              >
                <span className="tag-multiselect__option-check">{selected ? '✓' : ''}</span>
                {tag.label}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
