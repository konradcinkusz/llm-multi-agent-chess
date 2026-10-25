# 07 — UI style contract

The visual design of `Chess.Web`, frozen by the supervisor on 2026-10-25 so that the UI polish tasks (U1–U5 in
[06-TASKS](06-TASKS.md)) can run in parallel. Each task styles its own files and uses only the names below. A task that
needs a token or a shared class that is not listed here stops and asks under `## Blocked`. It does not invent a new name.

The owner's request was: "popraw wygląd UI" (make the UI look good). Before the polish, the pages used browser defaults: blue
underlined links, unstyled buttons and inputs, forms misaligned, content stuck to the left edge, an oversized head-to-head
heatmap, a scatter plot whose x-axis read "0 0 0 0 0", and a leaderboard table that scrolled the whole page at phone width.

## 1. Rules

1. **No new dependencies.** Plain CSS in `wwwroot/app.css` and in each component's scoped `.razor.css`. No JavaScript, no
   component library, no CSS framework, no web fonts (the app must work offline). Use the system font stack in §2.
2. **Colour only through tokens.** No hex, `rgb()` or named colour outside the token blocks in `app.css` and
   `Components/Charts/ChartFrame.razor.css`. The `/broadcast` OBS page keeps its own palette (it is out of scope).
3. **Both themes.** Light is the default. Dark comes from `@media (prefers-color-scheme: dark)`, which redefines the tokens and
   nothing else. There is no theme toggle.
4. **Phone width works.** At 390 px wide there is no horizontal page scroll (`document.documentElement.scrollWidth <=
   window.innerWidth`). Wide tables scroll inside a `.table-wrap`, not the page. The page gutter is 16 px.
5. **Accessible.** Body text and secondary text reach WCAG AA contrast (4.5:1) against their surface in both themes. Every
   interactive element shows `:focus-visible` (2 px accent outline, 2 px offset). Transitions are disabled under
   `prefers-reduced-motion: reduce`.
6. **Behaviour and test hooks do not change.** Keep every `data-testid`, every ARIA role and accessible name, every
   `data-section`, route, and every sentence that a bUnit or E2E test asserts. If a test asserts markup that the new design
   must change, change the test in the same PR and say why in the PR body. Never delete a test to make room.
7. **The Results page order is fixed** (03 §6.1). The Big Picture stays the first section and visible without scrolling at
   1366×768.
8. **Numbers line up.** Numeric table cells are right-aligned and use `font-variant-numeric: tabular-nums` (class `.num`).

## 2. Tokens (`:root` in `wwwroot/app.css`, owned by U1)

| Token | Light | Dark | Use |
|---|---|---|---|
| `--bg` | `#f5f6f8` | `#0f1115` | page background |
| `--surface` | `#ffffff` | `#171a20` | cards, header, tables |
| `--surface-2` | `#eef0f3` | `#1f232b` | table header, subtle fills, hover |
| `--border` | `#dde1e7` | `#2a2f38` | hairlines, card borders |
| `--border-strong` | `#c3cad3` | `#3a414c` | input borders |
| `--text` | `#14181f` | `#e8eaee` | body text, headings |
| `--text-2` | `#4a5462` | `#b4bac4` | secondary text, labels |
| `--text-3` | `#646d79` | `#8e95a0` | muted text, captions |
| `--accent` | `#1d64c2` | `#5b9cf0` | links, primary buttons, active nav, focus ring |
| `--accent-hover` | `#17519e` | `#7db1f5` | hover of the above |
| `--accent-contrast` | `#ffffff` | `#0b1220` | text on an accent fill |
| `--accent-soft` | `#e7effa` | `#1a2a40` | active nav pill, selected chip |
| `--success` | `#157a4f` | `#3fbf87` | healthy, win |
| `--success-soft` | `#e3f4ec` | `#13301f` | success badge fill |
| `--warning` | `#8f5d00` | `#e0a83a` | degraded, provisional |
| `--warning-soft` | `#fcf1d8` | `#33270e` | warning badge fill |
| `--danger` | `#c0362c` | `#f07067` | error, loss, forfeit |
| `--danger-soft` | `#fbe7e5` | `#3a1a18` | danger badge fill |
| `--shadow-1` | `0 1px 2px rgb(16 24 40 / 6%), 0 1px 3px rgb(16 24 40 / 8%)` | `none` | cards |

Shape, spacing and type are the same in both themes:

| Token | Value |
|---|---|
| `--radius-sm` / `--radius` / `--radius-lg` | `6px` / `10px` / `14px` |
| `--space-1` … `--space-7` | `4px` `8px` `12px` `16px` `24px` `32px` `48px` |
| `--gutter` | `16px` |
| `--content-max` | `1200px` |
| `--font-sans` | `system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif` |
| `--font-mono` | `ui-monospace, "SF Mono", Menlo, Consolas, monospace` |
| `--text-xs` / `--text-sm` / `--text-base` / `--text-lg` / `--text-xl` / `--text-2xl` / `--text-3xl` | `0.75rem` `0.875rem` `1rem` `1.125rem` `1.375rem` `1.75rem` `2.25rem` |

Charts keep their own `--chart-*` tokens in `ChartFrame.razor.css` (03 §6.1 palette, pinned by `ChartStylesheetTests`). U2 may
point the chart surface, ink and grid tokens at the global tokens above; the eight series colours do not change.

**Outcome colours** (results breakdown, game results): win = `--success` / `--chart-series-2`, draw = `--text-3` /
`--chart-neutral`, loss = `--danger` / `--chart-series-7`, forfeit loss = the loss colour with a hatch pattern. The legend lists
them in the order they are stacked: wins, draws, losses, forfeit losses.

## 3. Base element styles (owned by U1)

Element styles are scoped under `.app-shell` (the root element of `MainLayout`), so the chrome-less `BroadcastLayout` is not
touched. Inside `.app-shell`: `body`-level font and colours, headings (`h1` = `--text-2xl`, `h2` = `--text-xl`, `h3` =
`--text-lg`, weight 650, tight letter-spacing), links (accent, underline on hover only), `button`, `input`, `select`,
`textarea`, `fieldset`, `legend`, `label`, `table`, `th`, `td`, `details`/`summary`, `code`, `progress`. An unclassed `button`
looks like `.btn` (secondary); disabled controls are visibly disabled (`opacity: .55; cursor: not-allowed`). Inputs and selects
are 36 px tall with `--radius-sm` corners and `--border-strong` borders.

## 4. Shared classes (defined in `app.css` by U1, used by U3–U5)

| Class | What it is |
|---|---|
| `.page-header` | top of every page: `h1`, optional `.page-lede` (one sentence, `--text-2`), optional `.page-actions` (right on desktop, below on phone) |
| `.card` | `--surface`, 1 px `--border`, `--radius-lg`, `--shadow-1`, padding `--space-5` (`--space-4` under 600 px). `.card-header` (flex, space-between), `.card-title` (`--text-lg`, weight 600) |
| `.stack` / `.stack-lg` | vertical flow with `--space-4` / `--space-6` gaps |
| `.cluster` | horizontal flex, wraps, `--space-3` gap, items centred |
| `.grid-2` | two columns ≥ 900 px, one column below |
| `.btn` | secondary button: `--surface`, `--border-strong` border, `--text` |
| `.btn-primary` | accent fill, `--accent-contrast` text |
| `.btn-danger` | danger text and border, `--danger-soft` on hover |
| `.btn-sm` | 30 px tall, `--text-sm` |
| `.field` | a label above its control (`--text-sm`, `--text-2`, weight 500), `--space-1` gap; `.field-hint` below it in `--text-3` |
| `.field-row` | responsive grid of `.field`s: `repeat(auto-fit, minmax(180px, 1fr))`, `--space-4` gap |
| `.chips` / `.chip` | a checkbox or radio list shown as pills; `.chip` is a `label` wrapping its `input`; checked → `--accent-soft` fill and `--accent` border (`:has(input:checked)`) |
| `.table-wrap` | `overflow-x: auto` wrapper with `--border`, `--radius`, so wide tables scroll inside it |
| `.table-compact` | 6 px cell padding instead of 10 px |
| `.num` | right-aligned, tabular figures |
| `.badge` | small pill, `--text-xs`, weight 600, `--surface-2` fill. Variants `.badge-success`, `.badge-warning`, `.badge-danger`, `.badge-accent`, `.badge-neutral` (soft fill + strong text) |
| `.dot` | 8 px status dot before a label; `.dot-success`, `.dot-warning`, `.dot-danger`, `.dot-neutral`, `.dot-accent` |
| `.callout` | bordered message block with a left accent bar; `.callout-info`, `.callout-warning`, `.callout-danger` |
| `.empty-state` | centred muted message with an optional action link |
| `.muted` | `--text-3` |
| `.error-text` | `--danger` (the existing `.status-error` stays as an alias) |
| `.progress` | 8 px track (`--surface-2`) with an `.progress-bar` fill (`--accent`; `--success` when finished) |

## 5. App shell (owned by U1)

- **Header:** sticky, `--surface`, bottom border. Left: a brand mark (the knight glyph ♞ in a 28 px accent square) and
  "LLM Chess Arena". Then the navigation as pill tabs: active = `--accent-soft` fill with `--accent` text. Results stays the
  first item. On a phone the nav scrolls horizontally inside the header instead of wrapping onto three lines.
- **Status strip:** one slim row under the header with a `.dot` per service. Copilot `Ready` → success, `Fake` → accent,
  `NotAuthenticated` → warning, `Unavailable` → danger. Database and Stockfish: healthy → success, degraded or absent →
  warning (absent Stockfish → neutral), unhealthy → danger. The detail text is a `title` tooltip on desktop and stays readable
  on a phone.
- **Main:** centred, `max-width: var(--content-max)`, gutter padding, `--space-6` top padding.

## 6. Per-area direction

- **Results (U3):** the Big Picture is a hero `.card`: leader name at `--text-3xl`, then the arena score, the rating ± interval
  and the provisional badge, then the verdict sentence as a `.callout`, then the ranked bar chart. Each later section is a
  `.card` with its heading. The leaderboard table sits in a `.table-wrap`, numbers in `.num`, status as `.badge`.
- **Charts (U2):** the frame is card-like (padding, radius, border) and no text touches the edge. The heatmap cells are capped
  (≤ 72 px) and the labels use chart text size. Bar charts cap bar thickness (≤ 48 px) and round the data end by 4 px. A
  degenerate axis (all values equal) shows one tick, not a row of identical ticks. Chart text stays legible at 390 px.
- **Live game, games, replay (U4):** the board sits in a card with the players above and below it, colour chips for White and
  Black, and the move list as a numbered two-column list. The start button is `.btn-primary`. The result is a `.badge`. The
  games list puts its filters in a `.field-row`, the results in a `.table-wrap`, and the result column as a badge.
- **Tournaments, broadcast planner, attempts (U5):** forms in a `.card` with `.field-row`s and model `.chips`; the estimate as
  a `.callout-info`; primary actions `.btn-primary`, destructive ones `.btn-danger`; lists in a `.table-wrap`; progress with
  `.progress`.

## 7. How a polish task proves its work

Every U task runs V-NET with `E2E_CHROMIUM_PATH` set (the E2E journeys find their elements by role and test id, so they catch
a broken hook). Then it starts the fake profile, plays a small tournament through the API, and takes Playwright screenshots of
its pages in light (1366×900), dark (1366×900, `colorScheme: 'dark'`) and phone (390×844) layouts. It also records the
phone-width scroll check from rule 4 for each page. The PR lists each page with the result of that check and what changed.
Screenshots are not committed.
