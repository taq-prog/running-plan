# Running Plan -> Intervals.icu (.NET 10)

This project converts a 12-week running plan in YAML into Intervals.icu calendar events and syncs them through the Intervals API.

## What is included

- .NET 10 CLI project
- YAML plan schema
- Ready 12-week plan from your chat
- Intervals.icu API client (HTTP Basic auth, upsert by uid)
- `workout_doc` generation for structured sessions (steps/repeats/HR targets)
- Commands:
  - `validate` to verify plan structure
  - `sync` to push workouts to Intervals.icu
  - `sync --dry-run` to preview payloads without sending
  - `sync --structured-only` to include `workout_doc` only for workouts with explicit steps

## Project layout

- `RunningPlan.slnx`
- `src/RunningPlan.Cli/`
- `plans/plan-12-weeks.yaml`
- `schema/training-plan.schema.yaml`

## Intervals API auth

According to Intervals API docs (`/api-docs.html`), HTTP Basic auth is supported.

- Username: `API_KEY`
- Password: your API key from Intervals settings

This CLI sends: `Authorization: Basic base64(API_KEY:<your-api-key>)`.

## Build

```bash
dotnet build RunningPlan.slnx
```

## Validate plan

```bash
dotnet run --project src/RunningPlan.Cli -- validate plans/plan-12-weeks.yaml
```

## Sync (preview only)

```bash
dotnet run --project src/RunningPlan.Cli -- sync plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY \
  --dry-run

# only tempo/progressive/step-based workouts include workout_doc
dotnet run --project src/RunningPlan.Cli -- sync plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY \
  --dry-run \
  --structured-only
```

## Sync (real)

```bash
dotnet run --project src/RunningPlan.Cli -- sync plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY
```

You can also use env vars:

- `INTERVALS_ATHLETE_ID`
- `INTERVALS_API_KEY`
- `INTERVALS_BASE_URL` (optional, default `https://intervals.icu`)

## Notes

- The CLI posts to `POST /api/v1/athlete/{id}/events?upsertOnUid=true`.
- Events are created as `category=WORKOUT` and `type=Run`.
- For structured sessions, step details are included in both `description` and `workout_doc`.
- `workout_doc` contains a nested step tree (including repeat blocks and HR ranges) so the payload remains structured end-to-end.
- With `--structured-only`, simple easy/long workouts are sent without `workout_doc`, while workouts with `steps` keep the structured payload.
- In Intervals settings, enable Garmin sync (`Upload planned workouts`) so upcoming workouts are sent to Garmin Connect.
