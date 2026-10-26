# Viewer-paid games: the owner's setup

Viewers pay a YouTube Super Chat, and the broadcast plays the game they ask for. This page is for you, the owner. It covers
the keys, the spend limits, the YouTube sign-in, the prices, what viewers are told, a recorded run on the fake profile, and
what to check before going live. The design is [03 §15](architecture/03-TARGET-ARCHITECTURE.md) and
[ADR-016](architecture/05-DECISIONS.md). The broadcast side is [BROADCAST.md §8](BROADCAST.md#8-viewer-paid-requests-on-the-stream).

**Read this first.** A paid game costs real money on your provider accounts. Set a spend limit on each key (section 2) before you
turn the intake on. The app's own budget stop is the second line of defence, not the first.

## How a payment becomes a game

1. A viewer sends a Super Chat whose message is a command, for example `!g ca vs oa think`.
2. The YouTube intake reads the live chat by outbound polling. Nothing is exposed to the internet.
3. The Super Chat is recorded as **credit** for the viewer's channel, in US dollars, once, by its message id.
4. The command is priced. If the credit covers the price, the request is **queued** and the price is spent. If not, the request
   is **rejected** and the credit stays.
5. The broadcast runner plays queued requests one at a time, oldest first, before the planned slots. A game that is playing is
   never interrupted.
6. A game that spends its budget ends with the termination `budget` and is rated like a game that reaches the move cap.

## 1. API keys

The paid games use the provider keys through the custom-model settings. The keys are never written to a file.

There are three providers: Anthropic (`ProviderType` `anthropic`), and OpenAI and DeepInfra (both `ProviderType` `openai`, an
OpenAI-compatible endpoint). Each one is one `Arena:CustomModels` entry. Its plain fields are configuration. Its key is a secret
parameter named `custom-model-<Id>-key`.

Put the plain fields in the **AppHost user secrets** (`src/Chess.AppHost`), at the index of each entry. The AppHost forwards them to
the API:

```bash
dotnet user-secrets set --project src/Chess.AppHost "Arena:CustomModels:0:Id" anthropic-main
dotnet user-secrets set --project src/Chess.AppHost "Arena:CustomModels:0:ProviderType" anthropic
dotnet user-secrets set --project src/Chess.AppHost "Arena:CustomModels:0:BaseUrl" "https://api.anthropic.com"
dotnet user-secrets set --project src/Chess.AppHost "Arena:CustomModels:0:Model" "<anthropic-model-name>"
dotnet user-secrets set --project src/Chess.AppHost "Arena:CustomModels:0:UsdPerMillionInput" "1.00"
dotnet user-secrets set --project src/Chess.AppHost "Arena:CustomModels:0:UsdPerMillionOutput" "5.00"
dotnet user-secrets set --project src/Chess.AppHost "Parameters:custom-model-anthropic-main-key" "<your-anthropic-key>"
```

Repeat with index `1` for OpenAI (`ProviderType` `openai`, the base URL and model name from OpenAI's documentation) and index `2`
for DeepInfra (`ProviderType` `openai`, its base URL from DeepInfra's documentation). Use your own ids; the examples are placeholders.

Rules the code enforces:

- The **key** must come from the `Parameters:custom-model-<Id>-key` secret. A key written as a plain `ApiKey` value stops the
  AppHost with a message that names the parameter to use, and never prints the key.
- `Id` has no `@` or spaces, is unique, and is not the id of a Copilot model.
- A model priced in **neither** `UsdPerMillionInput` / `UsdPerMillionOutput` **nor** `CreditsPerMillionInput` /
  `CreditsPerMillionOutput` cannot be quoted, so it is left out of the menu.
- With `Arena:Requests:RequireApiKeyModels` on (the default), every menu model must be one of these entries. Startup fails
  otherwise, and the request never runs on the Copilot subscription.

### The menu, tiers and rates: the API's configuration

The AppHost forwards only `Arena:Requests:Mode` and the three YouTube secrets (section 3). It does **not** forward the menu, the
tiers, the fee share or the rates. The API reads those from its own configuration, so set them in `src/Chess.Api/appsettings.json`
(it holds no secrets), inside the existing `Arena` section, then restart the AppHost:

```json
"Requests": {
  "Menu": [
    { "Code": "an", "PlayerId": "anthropic-main", "Label": "Anthropic model" },
    { "Code": "oa", "PlayerId": "openai-main", "Label": "OpenAI model" },
    { "Code": "di", "PlayerId": "deepinfra-main", "Label": "DeepInfra model" }
  ],
  "Tiers": [ 1, 2, 5, 10, 20, 50 ],
  "FeeShare": 0.30,
  "SafetyFactor": 1.5
}
```

These codes and ids are placeholders. A viewer types the code: `!g an vs oa think`. `PlayerId` is a custom model's `Id`, optionally with
`@effort`. A side may add `~style`; a command can end with one style for both sides. The `Code` may not contain spaces, `@` or `~`, and
codes are unique.

Environment variables work too, for example `Arena__Requests__Menu__0__Code`. The fake profile sets its menu that way.

## 2. Spend limits

Set a **monthly spend limit** on each provider account, in that provider's own billing or usage settings, before the intake is on.
The limit is the second line of defence: the app's budget stop (section 4) is the first. The app cannot read the provider's
dashboard, so a limit you set there is the only one that holds if the app's own estimate is wrong. Keep each limit at the most you
are willing to lose in a month on that provider.

## 3. YouTube

### Sign-in (OAuth)

The intake signs in as your channel with an OAuth client and a refresh token. None of these values is stored by the app.

1. In the Google Cloud console, create a project and enable the **YouTube Data API v3** for it.
2. Configure the OAuth consent screen. While the app is in testing, add your own Google account as a test user.
3. Create an OAuth client ID. Note the **client id** and the **client secret**.
4. Obtain a **refresh token** for your channel with the authorization-code flow, asking for offline access. The OAuth 2.0
   Playground can do this with your own client credentials.
5. Store the three values as AppHost secret parameters:

```bash
dotnet user-secrets set --project src/Chess.AppHost Parameters:youtube-client-id <id>
dotnet user-secrets set --project src/Chess.AppHost Parameters:youtube-client-secret <secret>
dotnet user-secrets set --project src/Chess.AppHost Parameters:youtube-refresh-token <token>
```

6. Turn the intake on, in the same user secrets: `dotnet user-secrets set --project src/Chess.AppHost Arena:Requests:Mode YouTube`.

The AppHost passes the three values to the API only when they are set. If the mode is `YouTube` and one is missing, the API stops at
startup and names the missing setting, never its value.

### Scopes

The code sends no scope when it refreshes the token; the scope is the one the refresh token was granted in step 4. The intake makes
two read calls: `liveBroadcasts.list` (`mine=true`, `broadcastStatus=active`) to find the live chat, and `liveChatMessages.list` to
read it. It never writes to YouTube. Grant **`https://www.googleapis.com/auth/youtube.readonly`**. A broader scope also works, but
the intake does not need one.

### Polling and quota

The chat is read with outbound polling, never with a webhook. The gap between reads is YouTube's `pollingIntervalMillis`, and never
less than `Arena:Requests:YouTube:MinPollSeconds` (default **45**). The daily Data API quota is 10 000 units by default, and a 24-hour
stream must stay inside it. Keep the minimum at 45 s or raise it. When the quota is spent, the intake waits
`QuotaBackoffSeconds` (default 3600) before it reads again.

### Smoke test

`scripts/youtube-smoke.sh` reads your live chat once and prints the live chat id and the last five events the intake would map. It
needs a live stream, and it reads your account, so it refuses to run under CI. Its own usage text, which you can print with `--help`,
lists the three `dotnet user-secrets set` lines in section 3.

It was **not run** for this document: there is no live stream here. The refusal under CI, which was run here, is:

```text
$ CI=true scripts/youtube-smoke.sh
refusing to run under CI: the smoke test reads the owner's live YouTube chat (see docs/REQUESTS.md)
exit=2
```

Run it yourself during a live stream before the first paid game.

## 4. Pricing

### Tiers, fee share and safety factor

A game's **expected cost** is the cost of each side, estimated for 40 moves a side: the tokens a move takes, from the player's stored
history, then the model's, then the prompt style's assumption (classic 700 in and 300 out, board 1 600 in and 300 out, think 2 000
in and 900 out, per move), priced at the model's token prices.

```text
needed = (white + black) × SafetyFactor / (1 − FeeShare)
price  = the smallest tier ≥ needed
```

- **Tiers** (`Arena:Requests:Tiers`, default `[1, 2, 5, 10, 20, 50]` USD). A matchup above the largest tier is not offered.
- **FeeShare** (`Arena:Requests:FeeShare`, default `0.30`): YouTube's share of a Super Chat. It is kept by YouTube, so the game's
  budget is the price less this share.
- **SafetyFactor** (`Arena:Requests:SafetyFactor`, default `1.5`): headroom on the estimate, so a game that runs longer still fits its
  tier.

From the recorded run on the fake profile: `fake-a` vs `fake-b` estimates `0.00076` USD a side, so `0.00152` for the matchup. Then
`needed = 0.00152 × 1.5 / 0.7 = 0.00326`, and the smallest tier that covers it is **$1**.

### The budget stop

A request's game stops when the cost of its moves reaches **price × (1 − FeeShare)**, the net the owner receives. For a $1 request
the budget is **$0.70**. The check runs after every move. The game ends with the termination `budget`, it is adjudicated as a game at
the move cap is (Stockfish if it is installed, otherwise by material), and it stays rated ([RATING.md §2](RATING.md#2-outcome-of-a-game)).

The budget counts the cost the app estimates from the replies' tokens and the model's prices. A model with no prices has no cost, so
it adds nothing to the budget. That is why section 2 matters.

### Currency table

A Super Chat carries its amount and currency. `Arena:Requests:UsdPerUnit` converts it to US dollars. The defaults are placeholder
rates in the code:

```json
"UsdPerUnit": {
  "USD": 1.0, "EUR": 1.10, "GBP": 1.30, "CAD": 0.73, "AUD": 0.66, "NZD": 0.60, "BRL": 0.19,
  "MXN": 0.055, "INR": 0.012, "JPY": 0.0067, "KRW": 0.00074, "PLN": 0.26, "SEK": 0.095
}
```

Update the rates in `src/Chess.Api/appsettings.json` with the rate you want, then restart. A currency that is not in the table is
recorded at **0 USD** with the reason "currency not configured". The API has no endpoint that credits it by hand, so until you add the
rate, that payment stays unvalued in the `payer_ledger` table. The rates are configuration, not live data.

## 5. What viewers are told

Put this on the stream, in the chat, or in the stream description. It is what the broadcast shows the viewer, in words the app uses.

> **Pay to choose a game.** Send a Super Chat whose message is `!g <white> vs <black> [style]`, for example `!g an vs oa think`.
> Each side is a code from the price menu. The style is optional: `classic`, `board` or `think`.
>
> **Prices** are set by the matchup. They are the tiers of the menu (by default $1 to $50), and each one is shown beside its code. A matchup
> above the largest tier is not offered.
>
> **Credit.** A Super Chat that covers the price starts the game. A Super Chat that does not cover it is kept as credit on your
> channel. A later plain chat message `!g ...` spends your credit.
>
> **No refunds.** YouTube does not refund a Super Chat. A payment is either played or kept as credit. A request that cannot be played
> is credited back to you.
>
> **Budget.** A game stops when the money it has used reaches the share of your payment that the owner receives. The game then ends
> as a decided game, and it counts in the ranking.
>
> **What is shown.** Only the command you sent, parsed into the two codes and the style, and your display name are shown on the
> stream. The text of your message is never shown.

## 6. A recorded run on the fake profile

This run uses the fake profile: no keys, no Copilot, no YouTube. It goes through the same intake as YouTube, and the broadcast runner
plays the games. It was run on the build of this branch, with the fake profile's menu (`fa`, `fb`, `fah`). The output below is copied
from that run.

Start the app and wait for it:

```bash
dotnet run --project src/Chess.AppHost --launch-profile fake
```

The UI is on <http://localhost:5070> and the API on <http://127.0.0.1:5080>.

### 1. The menu

```bash
curl -sS http://127.0.0.1:5080/api/requests/menu
```

```json
{"items":[{"code":"fa","playerId":"fake-a","label":"fake-a","estimatedUsdPerSide":0.00076},{"code":"fb","playerId":"fake-b","label":"fake-b","estimatedUsdPerSide":0.00076},{"code":"fah","playerId":"fake-a@high","label":"fake-a@high","estimatedUsdPerSide":0.00076}],"styles":["classic","board","think"],"tiersUsd":[1,2,5,10,20,50],"feeShare":0.3,"commandHelp":"!g <white> vs <black> [style], for example !g c1 vs d3 think"}
```

### 2. The quote

```bash
curl -sS "http://127.0.0.1:5080/api/requests/quote?white=fa&black=fb"
```

```json
{"white":"fake-a","black":"fake-b","estimatedUsd":0.00152,"priceUsd":1}
```

### 3. Three chat messages

**A $1 Super Chat with a command.** It is credited and queued:

```bash
curl -sS -X POST http://127.0.0.1:5080/api/requests/simulate -H 'Content-Type: application/json' \
  -d '{"payerId":"viewer-ana","payerDisplay":"Ana","amount":1,"currency":"USD","text":"!g fa vs fb"}'
```

```json
{"duplicate":false,"depositUsd":1,"request":{"id":"14d4f51e-1b25-4a4a-b475-553cbef28189","source":"mock","payerDisplay":"Ana","white":"fake-a","black":"fake-b","priceUsd":1,"status":"queued","reason":null,"gameId":null,"receivedAt":"2026-10-26T16:04:08.6027039+00:00","queuePosition":1}}
```

**A PLN Super Chat with a bad code.** `zz` is not a menu code, so the request is rejected. The 5 PLN is kept as credit, at $1.30:

```bash
curl -sS -X POST http://127.0.0.1:5080/api/requests/simulate -H 'Content-Type: application/json' \
  -d '{"payerId":"viewer-ben","payerDisplay":"Ben","amount":5,"currency":"PLN","text":"!g zz vs fb"}'
```

```json
{"duplicate":false,"depositUsd":1.3,"request":{"id":"bd41fc04-9c49-4efd-be63-b7786364ae9d","source":"mock","payerDisplay":"Ben","white":"","black":"","priceUsd":0,"status":"rejected","reason":"Not a menu code.","gameId":null,"receivedAt":"2026-10-26T16:04:08.8636837+00:00","queuePosition":null}}
```

**A plain `!g` that spends the credit.** It has no amount, so it is not a deposit, and it is paid from Ben's $1.30:

```bash
curl -sS -X POST http://127.0.0.1:5080/api/requests/simulate -H 'Content-Type: application/json' \
  -d '{"payerId":"viewer-ben","payerDisplay":"Ben","text":"!g fa vs fb"}'
```

```json
{"duplicate":false,"depositUsd":null,"request":{"id":"4bef7149-471d-4650-8c80-aa24424c76cc","source":"mock","payerDisplay":"Ben","white":"fake-a","black":"fake-b","priceUsd":1,"status":"queued","reason":null,"gameId":null,"receivedAt":"2026-10-26T16:04:08.8787138+00:00","queuePosition":2}}
```

A third request, from `viewer-cy`, was cancelled to show the owner's cancel. Cancelling a queued request credits its price back, and
cancelling it again is refused:

```bash
curl -sS -X POST http://127.0.0.1:5080/api/requests/5f58fb3f-7189-45ff-87d5-108b64dd3e4f/cancel
```

```json
{"id":"5f58fb3f-7189-45ff-87d5-108b64dd3e4f","source":"mock","payerDisplay":"Cy","white":"fake-a","black":"fake-b","priceUsd":1,"status":"cancelled","reason":"cancelled by the owner","gameId":null,"receivedAt":"2026-10-26T16:11:06.6767299+00:00","queuePosition":null}
```

The same call again answers 409 with the detail `The request is cancelled; only a queued request can be cancelled.`

### 4. The queue, as the runner plays it

The runner takes the oldest queued request as soon as it is free, with no schedule running. Reading the queue shows each request's
status change:

```bash
curl -sS http://127.0.0.1:5080/api/requests/queue
```

Queued (first read, about 5 s after the simulate calls):

```text
"playing":null,"queued":[{"id":"14d4f51e-…","payerDisplay":"Ana","status":"queued","queuePosition":1},{"id":"4bef7149-…","payerDisplay":"Ben","status":"queued","queuePosition":2}]
```

Playing (about 10 s after; the request now has its game):

```text
"playing":{"id":"14d4f51e-…","payerDisplay":"Ana","white":"fake-a","black":"fake-b","status":"playing","gameId":"dc13822c-b9e9-48db-b01c-599059fabb4e"}
```

The game `dc13822c-b9e9-48db-b01c-599059fabb4e` is an ordinary rated game. Its moves are at `GET /api/games/<gameId>`. While it plays, the
broadcast paces it at 4 s a move (the default when no schedule runs). The game was still running at ply 171 when this section was
written; its result and the `played` state are added below once it ends.

## 7. Before going live

- **YouTube Super Chat policy.** Check YouTube's current Super Chat and live chat policies and community guidelines for your channel
  against a 24-hour stream where viewers pay to pick games. Do this before the first paid game, not after.
- **Tax on the income.** Check how Super Chat income is taxed where you live, with an accountant if you have one. The app records
  the gross amount in US dollars and the currency it came in. It does not compute tax.
- **The rates.** Update `UsdPerUnit` (section 4) to the rates you use. A stale rate gives a wrong price.
- **The spend limits.** Confirm the limit on each provider account (section 2).
- **The sign-in.** Run `scripts/youtube-smoke.sh` during a live stream and check that it prints the live chat id (section 3).

## 8. Known limits

- **The simulate form is visible until the API refuses it.** The form on `/admin/requests` shows at first. The API answers 403 outside
  Mock mode (`Arena:Requests:Mode` is `Off` or `YouTube`), and the form then goes away.
- **`/admin/requests` has no navigation link.** Open it by its address, `http://localhost:5070/admin/requests`.
- **There is no requeue endpoint.** `POST /api/requests/{id}/cancel` exists, and it works only on a queued request. A request that fails
  is requeued once by the app. A request left playing by a restart is requeued or closed at startup.
- **A payer's balance is not shown anywhere.** There is no balance endpoint and no balance on the admin page. A balance is the sum of
  the payer's rows in the `payer_ledger` table. The rejection reason names it when a command is refused for lack of credit.
- **Exchange rates are configuration, not live data.** They are the table in section 4, and they go stale until you update them.
- **No hand-credit endpoint.** A payment in a currency with no rate is credited at 0 USD, and the API has no call to credit it by hand.
- **A request game is long.** At the default pace of 4 s a move, a game that runs to the 200-ply cap takes about 15 minutes. The
  budget stops a game sooner once its money is spent.
