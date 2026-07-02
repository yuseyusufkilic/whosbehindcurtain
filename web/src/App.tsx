import { useEffect, useState } from 'react'
import type { CSSProperties, FormEvent } from 'react'
import './App.css'

type Clue = {
  id: string
  label: string
  cost: number
  effectiveCost: number
  icon: string
  isFreeEligible: boolean
  isRevealed: boolean
  value: string | null
}

type Result = {
  playerName: string
  season: string
  photoUrl: string
  clubLogoUrl: string
  solved: boolean
}

type PlayerStats = {
  gamesPlayed: number
  averageScore: number
  solvedCount: number
  lastFiveScores: number[]
}

type ArchiveItem = {
  puzzleId: string
  number: string
  publishDate: string
  isToday: boolean
  isComplete: boolean
}

type Puzzle = {
  puzzleId: string
  number: string
  publishDate: string
  score: number
  attemptsLeft: number
  isComplete: boolean
  isSolved: boolean
  freeClueAvailable: boolean
  guesses: string[]
  clues: Clue[]
  result: Result | null
  stats: PlayerStats | null
}

const iconMap: Record<string, string> = {
  field: '⌖', age: '◷', shirt: '№', ball: '●', assist: '↗',
  flag: '⚑', calendar: '▦', trophy: '♜', badge: '◆',
}

function App() {
  const [puzzle, setPuzzle] = useState<Puzzle | null>(null)
  const [guess, setGuess] = useState('')
  const [suggestions, setSuggestions] = useState<string[]>([])
  const [message, setMessage] = useState('')
  const [busyClue, setBusyClue] = useState<string | null>(null)
  const [isGuessing, setIsGuessing] = useState(false)
  const [showHelp, setShowHelp] = useState(false)
  const [streak, setStreak] = useState(() => Number(
    localStorage.getItem('hidden-star-streak') ?? localStorage.getItem('hidden-season-streak') ?? '0',
  ))
  const [archiveOpen, setArchiveOpen] = useState(false)
  const [archiveItems, setArchiveItems] = useState<ArchiveItem[]>([])
  const [activePuzzleId, setActivePuzzleId] = useState<string | null>(null)

  useEffect(() => {
    setPuzzle(null)
    setGuess('')
    setSuggestions([])
    setMessage('')
    const url = activePuzzleId ? `/api/puzzles/${activePuzzleId}` : '/api/puzzles/daily'
    fetch(url)
      .then(async response => {
        const data = await response.json()
        if (!response.ok) throw new Error(data.detail ?? data.message ?? 'Oyun yüklenemedi.')
        return data
      })
      .then(data => setPuzzle(data))
      .catch(() => setMessage('Oyun yüklenemedi. API bağlantısını kontrol et.'))
  }, [activePuzzleId])

  useEffect(() => {
    if (guess.trim().length < 2 || puzzle?.isComplete) {
      setSuggestions([])
      return
    }

    const timeout = window.setTimeout(() => {
      fetch(`/api/players/search?q=${encodeURIComponent(guess)}`)
        .then(response => response.json())
        .then((items: { name: string }[]) => setSuggestions(items.map(item => item.name)))
    }, 120)

    return () => window.clearTimeout(timeout)
  }, [guess, puzzle?.isComplete])

  useEffect(() => {
    if (!puzzle?.isComplete) return

    const today = new Date().toISOString().slice(0, 10)
    const lastPlayed = localStorage.getItem('hidden-star-last-played')
      ?? localStorage.getItem('hidden-season-last-played')
    if (lastPlayed === today) return

    const yesterday = new Date()
    yesterday.setUTCDate(yesterday.getUTCDate() - 1)
    const nextStreak = lastPlayed === yesterday.toISOString().slice(0, 10) ? streak + 1 : 1

    localStorage.setItem('hidden-star-last-played', today)
    localStorage.setItem('hidden-star-streak', String(nextStreak))
    setStreak(nextStreak)
  }, [puzzle?.isComplete, streak])

  async function reveal(clueId: string) {
    if (!puzzle || puzzle.isComplete || busyClue) return
    setBusyClue(clueId)
    setMessage('')

    try {
      const response = await fetch(`/api/puzzles/${puzzle.puzzleId}/reveal`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-HS-Request': 'game' },
        body: JSON.stringify({ clueId }),
      })
      const data = await response.json()
      if (!response.ok) {
        setMessage(data.message ?? 'İpucu açılamadı.')
        return
      }
      setPuzzle(current => current ? {
        ...current,
        score: data.score,
        freeClueAvailable: data.freeClueAvailable,
        isComplete: data.isComplete,
        isSolved: false,
        result: data.result,
        stats: data.stats,
        clues: current.clues.map(clue => clue.id === clueId
          ? { ...clue, isRevealed: true, value: data.value, effectiveCost: 0 }
          : {
              ...clue,
              effectiveCost: data.freeClueAvailable && clue.isFreeEligible ? 0 : clue.cost,
            }),
      } : current)
    } finally {
      setBusyClue(null)
    }
  }

  async function submitGuess(event: FormEvent) {
    event.preventDefault()
    if (!puzzle || !guess.trim() || puzzle.isComplete || isGuessing) return
    const selectedGuess = guess.trim()
    if (puzzle.guesses.some(previous => previous.localeCompare(selectedGuess, 'tr', { sensitivity: 'base' }) === 0)) {
      setMessage('Bu futbolcuyu zaten tahmin ettin.')
      return
    }
    setIsGuessing(true)
    setMessage('')

    try {
      const response = await fetch(`/api/puzzles/${puzzle.puzzleId}/guess`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-HS-Request': 'game' },
        body: JSON.stringify({ playerName: selectedGuess }),
      })
      const data = await response.json()
      if (!response.ok) {
        setMessage(data.message ?? 'Tahmin gönderilemedi.')
        return
      }
      if (data.duplicate) {
        setMessage('Bu futbolcuyu zaten tahmin ettin.')
        return
      }

      setPuzzle(current => current ? {
        ...current,
        score: data.score,
        attemptsLeft: data.attemptsLeft,
        isComplete: data.isComplete,
        isSolved: data.correct,
        guesses: [...current.guesses, selectedGuess],
        result: data.result,
        stats: data.stats,
      } : current)
      setMessage(data.correct ? 'BİLDİN. SOĞUKKANLI.' : data.isComplete ? 'Bugün olmadı.' : 'O değil. Bir daha düşün.')
      setGuess('')
      setSuggestions([])
    } finally {
      setIsGuessing(false)
    }
  }

  async function openArchive() {
    if (archiveItems.length === 0) {
      const items: ArchiveItem[] = await fetch('/api/puzzles/archive').then(r => r.json())
      setArchiveItems([...items].reverse())
    }
    setArchiveOpen(true)
  }

  function selectArchivePuzzle(item: ArchiveItem) {
    setActivePuzzleId(item.isToday ? null : item.puzzleId)
    setArchiveOpen(false)
  }

  async function shareResult() {
    if (!puzzle) return
    const blocks = puzzle.clues.map(clue => clue.isRevealed ? '🟩' : '⬛').join('')
    const average = puzzle.stats?.gamesPlayed ? `${puzzle.stats.averageScore}/100` : '—'
    const recent = puzzle.stats?.lastFiveScores.length
      ? puzzle.stats.lastFiveScores.join(' · ')
      : '—'
    const text = `HIDDEN STAR #${puzzle.number} · ${puzzle.publishDate}\n${puzzle.score}/100 · ${puzzle.guesses.length}/3 TAHMİN\n${blocks}\nORTALAMA ${average} · SON 5 ${recent}\nhiddenstar.app`

    if (navigator.share) {
      await navigator.share({ text })
    } else {
      await navigator.clipboard.writeText(text)
      setMessage('Sonuç panoya kopyalandı.')
    }
  }

  if (!puzzle) {
    return <main className="loading"><span className="spinner" />Maç verileri hazırlanıyor...</main>
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <a className="brand" href="#top" aria-label="Hidden Star ana sayfa">
          <span className="brand-mark">H</span>
          <span>HIDDEN<br />STAR</span>
        </a>
        <div className="top-stats">
            <div className="day-chip">{activePuzzleId ? 'ARŞİV' : 'DAILY'} <strong>#{puzzle.number}</strong></div>
          <div className="streak-chip">SERİ <strong>{streak}</strong></div>
        </div>
        <div className="topbar-actions">
          {activePuzzleId && (
            <button className="icon-button" aria-label="Bugüne dön" onClick={() => setActivePuzzleId(null)}>←</button>
          )}
          <button className="icon-button" aria-label="Arşiv" onClick={openArchive}>☰</button>
          <button className="icon-button" aria-label="Nasıl oynanır" onClick={() => setShowHelp(true)}>?</button>
        </div>
      </header>

      <main id="top" className="game">
        <section className="intro">
          <p className="eyebrow">TEK SEZON · TEK FUTBOLCU</p>
          <h1>
            <span className="headline-top"><span>PERDENİN</span><span>ARKASINDA</span></span>
            <em>KİM VAR?</em>
          </h1>
          <p className="subtitle">İpuçlarını satın al. Puanını koru. Futbolcuyu bul.</p>
        </section>

        <section className="scoreboard" aria-label="Oyun skoru">
          <div>
            <span className="score-label">SKOR</span>
            <strong className="score-value">{puzzle.score}</strong>
            <span className="score-total">/100</span>
          </div>
          <div className="attempts">
            <span>TAHMİN HAKKI</span>
            <div>{[0, 1, 2].map(index => <i key={index} className={index < puzzle.attemptsLeft ? 'active' : ''} />)}</div>
          </div>
        </section>

        {!puzzle.isComplete && (
          <section className="clue-section">
            <div className="section-heading">
              <span>01</span>
              <h2>İPUCUNU SEÇ</h2>
              <p>Her açılış skorundan düşer.</p>
            </div>
            {puzzle.freeClueAvailable && (
              <div className="free-clue-note"><strong>İLK PAS BİZDEN</strong> Yeşil işaretli düşük maliyetli ipuçlarından biri ücretsiz.</div>
            )}
            <div className="clue-grid">
              {puzzle.clues.map(clue => (
                <button
                  className={`clue-card ${clue.isRevealed ? 'revealed' : ''} ${clue.effectiveCost === 0 && !clue.isRevealed ? 'free' : ''}`}
                  key={clue.id}
                  onClick={() => reveal(clue.id)}
                  disabled={clue.isRevealed || busyClue !== null}
                >
                  <span className="clue-icon">{iconMap[clue.icon] ?? '•'}</span>
                  <span className="clue-copy">
                    <small>{clue.label}</small>
                    <strong>{clue.isRevealed ? clue.value : 'AÇMAK İÇİN DOKUN'}</strong>
                  </span>
                  <span className="clue-cost">{clue.isRevealed ? 'AÇILDI' : clue.effectiveCost === 0 ? 'ÜCRETSİZ' : `−${clue.effectiveCost}`}</span>
                </button>
              ))}
            </div>
          </section>
        )}

        {!puzzle.isComplete && (
          <section className="guess-section">
            <div className="section-heading">
              <span>02</span>
              <h2>TAHMİNİNİ YAP</h2>
            </div>
            <form className="guess-form" onSubmit={submitGuess}>
              <div className="search-wrap">
                <input
                  value={guess}
                  onChange={event => setGuess(event.target.value)}
                  placeholder="Futbolcu ara..."
                  autoComplete="off"
                  aria-label="Futbolcu adı"
                />
                {suggestions.length > 0 && (
                  <div className="suggestions">
                    {suggestions.map(name => (
                      <button type="button" key={name} onClick={() => { setGuess(name); setSuggestions([]) }}>{name}</button>
                    ))}
                  </div>
                )}
              </div>
              <button className="guess-button" disabled={!guess.trim() || isGuessing}>TAHMİN ET <span>→</span></button>
            </form>
            {puzzle.guesses.length > 0 && (
              <div className="guess-history">
                {puzzle.guesses.map(item => <span key={item}>× {item}</span>)}
              </div>
            )}
          </section>
        )}

        {puzzle.isComplete && puzzle.result && (
          <section className={`result-card ${puzzle.isSolved ? 'won' : 'lost'}`}>
            <div className="result-stripe">{puzzle.isSolved ? 'DOĞRU CEVAP' : 'GÜNÜN CEVABI'}</div>
            <div className="player-visual">
              <div className="player-number">{puzzle.score}</div>
              <div className="player-fallback" aria-hidden="true">{puzzle.result.playerName.split(' ').map(part => part[0]).join('').slice(0, 3)}</div>
              <img className="player-photo" src={puzzle.result.photoUrl} alt={puzzle.result.playerName} onError={event => { event.currentTarget.style.display = 'none' }} />
              <img className="club-logo" src={puzzle.result.clubLogoUrl} alt="Kulüp logosu" onError={event => { event.currentTarget.style.display = 'none' }} />
            </div>
            <div className="result-copy">
              <p>{puzzle.result.season} SEZONU</p>
              <h2>{puzzle.result.playerName}</h2>
              <div className="result-stats">
                <div><strong>{puzzle.score}</strong><span>PUAN</span></div>
                <div><strong>{puzzle.guesses.length}</strong><span>TAHMİN</span></div>
                <div><strong>{puzzle.clues.filter(clue => clue.isRevealed).length}</strong><span>İPUCU</span></div>
              </div>
              {puzzle.stats && (
                <div className="performance-panel">
                  <div className="performance-summary">
                    <span>GENEL ORTALAMA</span>
                    <strong>{puzzle.stats.averageScore}<small>/100</small></strong>
                    <em>{puzzle.stats.gamesPlayed} OYUN · {puzzle.stats.solvedCount} DOĞRU</em>
                  </div>
                  <div className="recent-performance">
                    <span>SON 5 PERFORMANS</span>
                    <div>
                      {puzzle.stats.lastFiveScores.map((score, index) => (
                        <i key={`${score}-${index}`} style={{ '--score': `${score}%` } as CSSProperties}>{score}</i>
                      ))}
                    </div>
                  </div>
                </div>
              )}
              <button className="share-button" onClick={shareResult}>SONUCU PAYLAŞ ↗</button>
            </div>
          </section>
        )}

        {message && <div className="toast" role="status">{message}</div>}
      </main>

      {archiveOpen && (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setArchiveOpen(false)}>
          <section className="archive-modal" role="dialog" aria-modal="true" aria-labelledby="archive-title" onMouseDown={event => event.stopPropagation()}>
            <button className="modal-close" aria-label="Kapat" onClick={() => setArchiveOpen(false)}>×</button>
            <p className="eyebrow">ARŞİV</p>
            <h2 id="archive-title">GEÇMİŞ<br />GÜNLER</h2>
            <div className="archive-list">
              {archiveItems.map(item => (
                <button
                  key={item.puzzleId}
                  className={`archive-item${item.isToday ? ' today' : ''}${item.isComplete ? ' complete' : ''}`}
                  onClick={() => selectArchivePuzzle(item)}
                >
                  <span className="archive-number">#{item.number}</span>
                  <span className="archive-date">{item.publishDate}</span>
                  <span className="archive-status">{item.isToday ? 'BUGÜN' : item.isComplete ? '✓ TAMAMLANDI' : '→'}</span>
                </button>
              ))}
            </div>
          </section>
        </div>
      )}

      {showHelp && (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setShowHelp(false)}>
          <section className="how-to-modal" role="dialog" aria-modal="true" aria-labelledby="how-to-title" onMouseDown={event => event.stopPropagation()}>
            <button className="modal-close" aria-label="Kapat" onClick={() => setShowHelp(false)}>×</button>
            <p className="eyebrow">OYUN KURALLARI</p>
            <h2 id="how-to-title">FUTBOLCUYU<br />EN YÜKSEK PUANLA BUL.</h2>
            <ol>
              <li><strong>Bir ipucu seç.</strong><span>Düşük maliyetli ipuçlarından seçtiğin ilki ücretsiz.</span></li>
              <li><strong>Puanını koru.</strong><span>Sonraki her ipucu, üzerinde yazan puanı düşürür.</span></li>
              <li><strong>Üç hakkın var.</strong><span>Her yanlış tahmin 10 puan götürür.</span></li>
            </ol>
            <button className="modal-primary" onClick={() => setShowHelp(false)}>SAHAYA ÇIK →</button>
          </section>
        </div>
      )}

      <footer><span>HIDDEN STAR</span><p>Futbol hafızana ne kadar güveniyorsun?</p><span>2026</span></footer>
    </div>
  )
}

export default App
