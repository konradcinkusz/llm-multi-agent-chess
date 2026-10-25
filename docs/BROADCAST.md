# Broadcast mode: running the 24-hour channel

Broadcast mode plans a day of games in advance, plays them one at a time with pauses so viewers can follow, and gives OBS one
clean page to capture. The games are ordinary rated games, so the ranking on the Results page keeps growing with the channel.

**The app does not stream video.** OBS captures the `/broadcast` page and streams to YouTube. The app never sees YouTube or
the stream key. Design: [ADR-011](architecture/05-DECISIONS.md) and [03 §9](architecture/03-TARGET-ARCHITECTURE.md).

## Before the first day

- The app runs on the machine that will stream, with the Copilot login it already uses. Check `http://localhost:5080/api/status`:
  `"copilot"` must be `"Ready"`. The states and what to do about each are in the README's
  [troubleshooting section](../README.md#troubleshooting-the-health-states).
- Docker is running (the app starts Postgres in a container).
- OBS is installed. The machine does not sleep (see [Keep the machine awake](#keep-the-machine-awake-and-the-app-running-24-h)).

## Start the app

In a terminal multiplexer such as `tmux`, from the repository root:

```bash
dotnet run --project src/Chess.AppHost
```

The AppHost starts Postgres, the API on `http://localhost:5080` and the web app on `http://localhost:5070`. Leave the terminal
running; detach with `Ctrl-b d` in tmux and reattach with `tmux attach`.

## 1. Plan a day

### In the app: `/broadcast/plan`

1. Open `http://localhost:5070/broadcast/plan`.
2. Pick the models. Each model shows its premium multiplier. `Random` is always available and costs nothing.
3. Set the start time and the duration (24 h by default), the pacing (move delay 4000 ms and game gap 20 s by default), and the seed.
4. Press **Generate preview**. The timeline of every slot and the premium-request estimate per model appear **before anything is saved**.
5. Edit if needed: **Up**, **Down** and **Delete** change the order of the slots.
6. Press **Save draft**. Nothing runs yet.
7. Press **Start**. The day plays from the start time; the button is offered only for the saved draft with no unsaved edits.

Only one day plays at a time. A second start while one is playing or paused is refused.

### Through the API

Model ids come from `GET /api/models`. A model may be a variant with a reasoning effort, `model@effort`, as in games and
benchmarks, and each effort is ranked as its own player. The effort must be one the model offers: `fake-a@ultra` is refused
with a `400` that says `fake-a does not offer reasoning effort 'ultra'. It offers: low, medium, high.`. A custom model
(see [BENCHMARK.md section 4](BENCHMARK.md#4-your-own-model)) is planned by its `Id` like any other.

```http
POST /api/broadcast/plan
Content-Type: application/json

{
  "name": "LLM Chess 24h",
  "models": ["random", "<model id>", "<model id>"],
  "startsAt": "2026-10-25T08:00:00Z",
  "durationHours": 24,
  "pacing": { "moveDelayMs": 4000, "gameGapSeconds": 20 },
  "seed": 1,
  "assumedPlies": 80,
  "assumedMoveLatencyMs": 40000
}
```

The answer (201) is the schedule and its slots, saved as a draft. Then:

| Step | Call | Answer |
|---|---|---|
| Edit the slots of a draft or scheduled day | `PUT /api/broadcast/schedules/{id}/slots` (the array order is the play order) | 200 |
| Start (Draft or Scheduled → Running) | `POST /api/broadcast/schedules/{id}/start` | 200, 404 or 409 |
| Pause after the current move | `POST /api/broadcast/schedules/{id}/pause` | 200, 404 or 409 |
| Resume | `POST /api/broadcast/schedules/{id}/resume` | 200, 404 or 409 |
| Stop (cancels the day) | `POST /api/broadcast/schedules/{id}/stop` | 200, 404 or 409 |
| What is on air now | `GET /api/broadcast/now` | 200 |
| All schedules | `GET /api/broadcast/schedules` | 200 |

A 409 means the schedule is not in a state the command can leave, for example starting a second day while one is playing.

## 2. Cost

Copilot bills the arena's models by tokens, in **AI credits**: each move costs its input and output tokens at the model's token
prices. The app shows credits and an estimate in US dollars, at `Arena:Cost:UsdPerAiCredit` (default 0.01 per credit). The
planner's defaults (80 plies per game, 40 s average move latency, 4 s move delay, 20 s gap) give:

- **Game length.** 80 plies × (40 s + 4 s) = 58 min 40 s per game, plus the 20 s gap: 59 min per slot.
- **Games per day.** A 24-hour window holds **24 games** (the soak test checks this count with these defaults).
- **Moves per side.** The planner assumes 80 plies ÷ 2 = **40 moves per side** per game.
- **Tokens per move.** A model with no stored moves is assumed to use **700 input and 300 output tokens** per move. A model
  with stored moves uses its own average over them. The preview's *Tokens per move* column says which basis it used.
- **Cost per model.** Its games × 40 moves per side × (700 × input price + 300 × output price) ÷ 1 000 000, with prices in US
  dollars per million tokens from the model's listing. The estimate charges every input token at the full input price, so
  cache hits make the real cost lower.
- **A day with random players** costs less: a side playing `random` costs nothing.
- **Unknown prices.** A model whose listing has no prices shows an unknown cost, and the day's total leaves it out. Such a
  total is an undercount until those prices are listed.
- **Premium requests.** Copilot does not bill token-priced models by premium request. A model that still reports a premium
  multiplier also gets a premium-request figure in the preview; a model without one shows that figure as unknown.

The estimate is the normal case, not a ceiling:

- A game that reaches the 200-ply cap plays up to 100 moves per side, 2.5 times the estimate.
- A move the model answers illegally is retried, and each attempt is billed. The estimate assumes one reply per move.
- The conversion from the runtime's reported units to AI credits assumes 1 AI unit = 1 AI credit. The owner's live run has
  not confirmed that yet.

## 3. OBS: Browser Source

The page is a full-screen 1920×1080 layout with no app chrome. Add it as a source:

1. In OBS, select or create a scene for the channel. Set the canvas to **1920×1080** (Settings → Video) and **30 FPS**
   (Settings → Video → Common FPS Values).
2. **Sources → + → Browser**. Name it "LLM Chess 24h" and press **OK**.
3. **URL:** `http://localhost:5070/broadcast`
4. **Width:** `1920`, **Height:** `1080`, **FPS:** `30`.
5. **Custom CSS:** leave empty.
6. Tick **Refresh browser when scene becomes active**.
7. Leave **Control audio via OBS** unticked. The page makes no sound.
8. Press **OK**. The board, the moves, the leaderboard and the "Up next" list appear. If the page is empty, check that the app
   is running and the URL is correct.

## 4. YouTube Live

The app does not stream. Set up the stream in OBS and YouTube:

1. In YouTube Studio, create a live stream and copy its **stream key** from the stream settings.
2. In OBS, **Settings → Stream**: Service **YouTube - RTMPS**, and paste the stream key.
3. Start streaming from OBS. Start the day in the app shortly before the stream goes live, or let it run from its start time.

## Keep the machine awake and the app running 24 h

- **Disable sleep and hibernation.** On Windows, in Settings → System → Power, set sleep and hibernate to **Never** for plugged-in
  use. On macOS, in System Settings → Energy (or Battery and Power), turn off sleep when the display is off; the built-in
  `caffeinate` tool keeps the machine awake while it runs. Keep the display from sleeping as well, since a sleeping display can freeze the capture.
- **Keep the terminal running** with `dotnet run --project src/Chess.AppHost` in a terminal multiplexer, so it survives closing the window.
- **Keep Docker running** so Postgres is available.
- **Plug in the machine.** A laptop on battery is put to sleep by the OS regardless of the setting above.

## 5. Restarts and Copilot outages

**A restart of the app** (a crash, an update, a reboot): a day that was running resumes at its next unfinished slot.

- The slot in play is skipped with the note "interrupted by a restart". Its game is closed as interrupted and is **not rated**.
- Slots whose start time passed during the downtime are played at once, one after another with the game gap, so the channel catches up.
- A day that was paused stays paused until you press **Resume**.

**A Copilot outage or rate limit** (the model gives no reply):

- The game is aborted, not rated. The slot is retried after a **30 s** pause; if it fails again, after **60 s**.
- While it waits, `/broadcast` shows a "technical break" card with the time to the next try.
- After the third failed attempt the slot is **skipped** (note: "skipped after 3 attempts: the model gave no reply") and the next slot
  starts after the game gap. The stream never ends because of one game.

**Stop and pause:**

- **Pause** lets the game in play finish its current move, then holds. **Resume** continues the same game.
- **Stop** aborts the game in play. Its slot is skipped with the note "stopped by the operator", and the day is cancelled. A
  cancelled day cannot be resumed; plan a new one.

## 6. Read the Results page during the stream

Open `http://localhost:5070/results` on the streaming machine or in a browser on the side. It is the same ranking as the mini
leaderboard on the channel.

- Only **finished, decided** games count. The game on air is not in the ranking until it ends, and an interrupted or aborted game
  is never in it.
- Ratings include the rating uncertainty; a model with few games is shown as provisional. Read the ranking as a trend, not as a
  final verdict, until the model has played many games.
- The Games page (`/games`) lists the games, including the one on air.

## 7. Smoke test before the first day

`scripts/broadcast-smoke.sh` checks the whole path on the fake profile (no Copilot, no Docker):

```bash
./scripts/broadcast-smoke.sh
```

It starts the app with the fake profile, plans a one-hour schedule with 50 ms moves and 1 s gaps, starts it, prints
`/api/broadcast/now` until the schedule is finished (or fails after 120 s), prints the standings and stops the app it started. It
needs the .NET 10 SDK, `curl` and `jq`, and port 5080 must be free. The fake profile does not play Copilot; use it to check the
plumbing, and check the live models from the planner page before the first real day.
