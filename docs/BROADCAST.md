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
2. Optionally set the name and the pacing (move delay 4000 ms and game gap 30 min by default; the gap is typed in minutes). There is no start time or duration.
3. Pick a White player and a Black player (each model also appears with each reasoning effort it offers, and in each ticked prompt style) and press **Add game**. Repeat for every game, one line per game. **Up**, **Down** and **Delete** change the list.
4. Under the list, **Estimated cost of the broadcast** shows the total premium requests, AI credits and USD of the games added, and each model's share, before anything is sent.
5. Press **Start**. The app sends the games in one call (`POST /api/broadcast/schedules`, below), which checks them, saves the
   schedule and starts it. The games play in list order. Each game starts after the previous one ends plus the game gap, and
   the next start time shown on the page is recalculated from the games already played. Start is disabled while another
   broadcast is playing or paused. If the API refuses, the page shows why, nothing is saved, and the games stay on the page.
6. Under **Broadcasts**, each row has **Start** (a draft or scheduled day), **Pause**, **Resume**, **Stop** and, for a draft,
   **Delete**. **Stop** asks for a second click ("Yes, stop"), because it cancels the day and aborts the game in play. While a
   day is running or paused the list reloads every 5 seconds, so Start frees itself when the day ends.

**Timing model (one rule).** The planned start and end of each game are estimates, not appointments. The runner never waits for
a clock time: it plays the first game when you press **Start**, and every next game one **game gap** after the previous game left
*running* (finished, skipped after three failed attempts, or cut off by a restart). Whenever a game leaves *running*, the app
re-times the games still to play from that moment (the next one at the end plus the gap, the rest back to back with their planned
lengths) and moves the schedule's end to the end of its last game. The "Up next" times and the end on `/broadcast` therefore
follow what really happened: a game that fails and is skipped does not push the next game out to its original time. The
planner's lengths (80 plies at 44 s) only decide the first estimate.

The random player is free: the estimate shows it as "free". A schedule holds at most 500 games, and its window is the span of
the games, so a long list is limited to 168 hours. Only one day plays at a time. A second start while one is playing or
paused is refused.

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
  "pacing": { "moveDelayMs": 4000, "gameGapSeconds": 1800 },
  "seed": 1,
  "assumedPlies": 80,
  "assumedMoveLatencyMs": 40000
}
```

The answer (201) is the schedule and its slots, saved as a draft. The plan is capped at 500 games, and `assumedPlies` and
`assumedMoveLatencyMs` must be finite (plies positive, latency not negative): a request that breaks a rule is a `400`. A
`startsAt` with an offset is stored as UTC.

To start a day from games you pick yourself (what the page does), one call validates, saves and optionally starts it, in
order, with the times laid out by the API:

```http
POST /api/broadcast/schedules
Content-Type: application/json

{
  "name": "LLM Chess 24h",
  "pacing": { "moveDelayMs": 4000, "gameGapSeconds": 1800 },
  "games": [
    { "white": { "kind": "llm", "modelId": "<model id>" }, "black": { "kind": "random", "modelId": "random" } }
  ],
  "start": true
}
```

`startsAt` (default: now, to the whole second), `assumedPlies` (80), `assumedMoveLatencyMs` (40000) and `seed` (1) are optional.
The random player is `{"kind":"random","modelId":"random"}`; any other player is kind `llm`. The answer (201) is the schedule;
with `"start": true` its status is `running`. A refusal (400 for a bad game, 409 when another broadcast is playing or paused)
saves nothing: a start the broadcast runner refuses removes the schedule that was just saved, so no draft is left behind.

Then:

| Step | Call | Answer |
|---|---|---|
| Delete a draft | `DELETE /api/broadcast/schedules/{id}` | 204, 404, or 409 when it is not a draft |
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
planner's defaults (80 plies per game, 40 s average move latency, 4 s move delay, 30 min gap) give:

- **Game length.** 80 plies × (40 s + 4 s) = 58 min 40 s per game, plus the 30 min gap: about 1 h 29 min per slot.
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

The page is a full-screen 1920×1080 layout with no app chrome: the board on the full height, and beside it one column that
takes the rest of the width, with the status line, the moves (which fill the column) and the up-next games, and a link to the
repository in the header. Each player's model and reasoning effort (`model · effort`) show on its side of the board, and the
same `model · effort` names are used in the up-next list and the intermission card. Everything fits on the one screen;
nothing scrolls. The row text is 36 px at 1080p (panel headings 30 px, the floor is 28 px), so the stream stays readable on a
phone. The same layout fits any landscape window, and on a phone held upright `/broadcast` switches to a two-column grid
(moves and up next, under the board). To force that phone layout on any screen, open `/broadcast?view=mobile`;
`/broadcast?theme=light` gives a light background. Add the page as a source:

1. In OBS, select or create a scene for the channel. Set the canvas to **1920×1080** (Settings → Video) and **30 FPS**
   (Settings → Video → Common FPS Values).
2. **Sources → + → Browser**. Name it "LLM Chess 24h" and press **OK**.
3. **URL:** `http://localhost:5070/broadcast`
4. **Width:** `1920`, **Height:** `1080`, **FPS:** `30`.
5. **Custom CSS:** leave empty.
6. Tick **Refresh browser when scene becomes active**.
7. Leave **Control audio via OBS** unticked. The page makes no sound.
8. Press **OK**. The board, the moves and the "Up next" list appear. If the page is empty, check that the app
   is running and the URL is correct.

## 4. YouTube Live

The app does not stream. Set up the stream in OBS and YouTube:

1. In YouTube Studio, create a live stream and copy its **stream key** from the stream settings.
2. In OBS, **Settings → Stream**: Service **YouTube - RTMPS**, and paste the stream key.
3. Start streaming from OBS. Press **Start** in the planner shortly before the stream goes live: the first game starts when you press it (there is no start time to wait for).

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
- A slot or request game that was saved but had not started is closed as interrupted too, so it is never played unpaced after the restart. Only games that no tournament, benchmark, slot or request owns are queued again.
- The games still to play are re-timed from the restart, and the next one starts at once; after it the game gap applies as usual.
- A day that was paused stays paused until you press **Resume**.

**A Copilot outage or rate limit** (the model gives no reply):

- The game is aborted, not rated. The slot is retried after a **30 s** pause; if it fails again, after **60 s**.
- While it waits, `/broadcast` shows a "technical break" card with the time to the next try.
- After the third failed attempt the slot is **skipped** (note: "skipped after 3 attempts: the model gave no reply") and the next slot
  starts after the game gap. The stream never ends because of one game.

**Stop and pause:**

- **Pause** lets the game in play finish its current move, then holds. **Resume** continues the same game.
- **Stop** aborts the game in play. Its slot is skipped with the note "stopped by the operator", and the day is cancelled. A
  cancelled day cannot be resumed; plan a new one. A stop also ends the pause between games, so a new day started after it
  plays its first game at once and does not wait out the old gap. A command that cannot be saved (the database is down) changes
  nothing: the pause flag and the game in play stay as they were.

**An error inside the runner** (for example the database refuses a write after a game started): a game that is marked *running*
with nothing playing it is settled at the next step, so `/api/broadcast/now` does not keep showing a dead game. A game that
finished counts as finished; an aborted one is tried again (up to three attempts); one left unfinished is closed as
interrupted and its slot is skipped with the note "interrupted by an error".

## 6. Read the Results page during the stream

Open `http://localhost:5070/results` on the streaming machine or in a browser on the side. The stage itself shows no ranking.

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
needs the .NET 10 SDK, `curl`, `jq` and a `date` that can subtract an hour (GNU `-d`, or BSD `-v`; the script stops with a message
otherwise), and port 5080 must be free. The fake profile does not play Copilot; use it to check the
plumbing, and check the live models from the planner page before the first real day.

## 8. Viewer-paid requests on the stream

Viewers can pay a YouTube Super Chat to ask for a game between two models (ADR-016, [03 §15](architecture/03-TARGET-ARCHITECTURE.md)).
The owner's rules, prices and keys are in [docs/REQUESTS.md](REQUESTS.md). This section is what the broadcast does with them.

**Requests go before slots.** The runner checks the request queue before the next planned slot, at every step:

- A queued request is played only **while a schedule is running**. With no schedule running (none started, or the day finished) the
  request stays queued and no game starts. A running schedule's pace applies to it, like a slot.
- Otherwise a request's game uses the running schedule's pace, like a slot.
- **Pause** holds requests as well as slots. A paused day plays no request until **Resume**.
- A request that is playing is never interrupted by a request that arrives after it. The queue is first in, first out.
- After a request game the runner holds the game gap, re-times the games still to play from it like after a slot, and a stop of
  the day ends that hold.
- A request game is an ordinary rated game. Only the games the broadcast runner plays are paced; nothing else changes.

**Intermission and idle replays.** Between two scheduled games the `/broadcast` page shows the **intermission card** over the
board, which is already set up for the next game: the next start time, the two players and a countdown ("Starts in 12:34", or
"Starts in 1:29:00" from an hour on, "Starts in a moment" once the start time has passed). No replay plays during that gap. A
replay plays only when all of these hold:

- the broadcast is **running** (not paused or off air),
- no game is on the schedule's current slot,
- **no further game is scheduled** (the "Up next" list is empty), and
- **nothing is queued or playing** in the request queue.

Then the page replays a stored game instead of the intermission card:

- The game is the best-rated recent finished rated game, with the two models' ratings added. It is not the game just shown.
- The pace is the schedule's move delay, or one move every 3 s when the schedule sends none. The final position stays for 10 s,
  then the next stored game starts. Replays cost nothing.
- A request that arrives during a replay, or a game that gets scheduled, stops the replay at the page's next read, every 5 s,
  and the card returns; a request plays next.
- With no stored game to replay, the card stays ("The schedule has no further games.").

**What the stage shows of a request.** The status line names the payer while the request's game is on the board
("Requested by Ana · Move 12 · GPT is thinking…"). The stage shows no request queue, menu or rejections, and a viewer's
message text is never shown on the stream.

**The page keeps running.** Every read of the poll loop and every game load (the broadcast, the request queue, the schedule, a game) is cut after 20 s,
and a read that fails or times out is logged and retried on the next poll, so one bad answer or a machine that slept does not
freeze the stage. While the broadcast cannot be read the header shows "Reconnecting to the arena…".

**Admin.** `/admin/requests` lists what plays, what is queued and what finished recently, with cancel for a queued request. It is
not in the navigation: open it by its address, `http://localhost:5070/admin/requests`. It has a simulate form in Mock mode only.
