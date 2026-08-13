# Running Plan -> Intervals.icu (.NET 10)

This project converts a 12-week running plan in YAML into Intervals.icu calendar events and syncs them through the Intervals API.

## What is included

- .NET 10 CLI project
- YAML plan schema
- Ready 12-week plan from your chat
- Intervals.icu API client (HTTP Basic auth, upsert by uid)
- Intervals Workout Builder syntax generation for all sessions (steps/repeats/HR targets)
- Commands:
  - `validate` to verify plan structure
  - `sync` to push workouts to Intervals.icu
  - `verify` to check that planned workouts exist in Intervals.icu calendar
  - `cleanup` to remove already-uploaded plan events in the plan date range by `plan_name`
  - `sync --dry-run` to preview payloads without sending
  - `sync --apply-plan` to upload the full block via `events/apply-plan`
  - `sync --create-plan-on-missing` to create a plan and retry apply-plan on `404 Plan not found`
  - `sync --plan-name` to control the auto-created plan name
  - `sync --start-time-local` to set planned workout local start time (default `00:00`)
  - `sync --cleanup-plan-before-apply` to delete matching planned events before sync (both apply-plan and per-event modes)
  - `sync --no-verify` to skip post-sync verification call
  - `sync --json` to emit machine-readable sync output for CI/automation
    - includes `CleanupDeletedCount` (number of events removed by `--cleanup-plan-before-apply`)
    - includes `CleanupDuplicateSignaturesBefore` and `CleanupDuplicateSignaturesAfter`
      - in sync mode, pre-cleanup removes all matching planned signatures first, so `CleanupDuplicateSignaturesAfter` is expected to be `0` before apply-plan inserts the fresh set
  - `verify --json` to emit machine-readable verification output for CI/automation
  - `cleanup --json` to emit machine-readable cleanup output for CI/automation
    - includes `DuplicateSignaturesBefore` and `DuplicateSignaturesAfter`

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

## .env support

The CLI auto-loads `.env` from the current directory (without overriding already-set shell env vars).

1. Copy `.env.example` to `.env`
2. Fill `INTERVALS_ATHLETE_ID` and `INTERVALS_API_KEY`
3. Run commands without passing secrets in args

## Build

```bash
dotnet build RunningPlan.slnx
```

## Validate plan

```bash
dotnet run --project src/RunningPlan.Cli -- validate plans/plan-12-weeks.yaml
```

## YAML start time

You can set default planned workout time directly in YAML:

```yaml
meta:
  start_date: 2026-08-11
  timezone: "Asia/Almaty"
  start_time_local: "00:00"
```

- `meta.start_time_local` is used by default for `sync` and `cleanup`.
- `--start-time-local HH:mm` overrides YAML for a specific run.

## Sync (preview only)

```bash
dotnet run --project src/RunningPlan.Cli -- sync plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY \
  --dry-run

# upload as one apply-plan request (optionally set destination folder)
dotnet run --project src/RunningPlan.Cli -- sync plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY \
  --dry-run \
  --start-time-local 00:00 \
  --apply-plan \
  --folder-id 0

# if folder/plan is missing, create one and retry apply-plan
dotnet run --project src/RunningPlan.Cli -- sync plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY \
  --apply-plan \
  --folder-id 0 \
  --start-time-local 00:00 \
  --create-plan-on-missing \
  --cleanup-plan-before-apply \
  --plan-name "My 12-week running plan"

dotnet run --project src/RunningPlan.Cli -- sync plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY \
  --dry-run \
  --json
```

## Sync (real)

```bash
dotnet run --project src/RunningPlan.Cli -- sync plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY

# run real sync without post-sync verification
dotnet run --project src/RunningPlan.Cli -- sync plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY \
  --no-verify
```

## Verify only

```bash
dotnet run --project src/RunningPlan.Cli -- verify plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY

dotnet run --project src/RunningPlan.Cli -- verify plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY \
  --json
```

## Cleanup only (no sync)

```bash
# preview how many events would be removed
dotnet run --project src/RunningPlan.Cli -- cleanup plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY \
  --plan-name "My 12-week running plan" \
  --dry-run \
  --json

# execute cleanup
dotnet run --project src/RunningPlan.Cli -- cleanup plans/plan-12-weeks.yaml \
  --athlete-id YOUR_ATHLETE_ID \
  --api-key YOUR_API_KEY \
  --plan-name "My 12-week running plan"
```

You can also use env vars:

- `INTERVALS_ATHLETE_ID`
- `INTERVALS_API_KEY`
- `INTERVALS_BASE_URL` (optional, default `https://intervals.icu`)

Example with `.env` loaded automatically:

```bash
dotnet run --project src/RunningPlan.Cli -- sync plans/plan-12-weeks.yaml \
  --apply-plan \
  --folder-id 0 \
  --start-time-local 00:00 \
  --create-plan-on-missing \
  --cleanup-plan-before-apply \
  --plan-name "My 12-week running plan" \
  --json
```

## Notes

- The CLI posts to `POST /api/v1/athlete/{id}/events?upsertOnUid=true`.
- With `--apply-plan`, the CLI posts to `POST /api/v1/athlete/{id}/events/apply-plan` using one request containing `extra_workouts`.
- Planned events are created with local start time from `--start-time-local` (default `00:00`).
- If CLI flag is omitted, local start time comes from `meta.start_time_local` in plan YAML.
- After non-dry sync, the CLI verifies uploaded workouts using `GET /api/v1/athlete/{id}/events` across the plan date range.
- Verification mode depends on sync mode:
  - per-event sync: checks `external_id` (+ date consistency)
  - apply-plan sync: checks by `date + name` (and `plan_name` when provided), because some accounts return `external_id = null` for apply-plan-created events
- If verification finds missing or mismatched workouts, sync exits with an error and prints a compact mismatch report.
- Events are created as `category=WORKOUT` and `type=Run`.
- Every workout is sent through `description` using Intervals Workout Builder syntax, including distance/time, repeat blocks, cues, and HR targets.
- `distance_km` is the total workout distance; `steps` describe how to execute the workout and may include time-based segments.
- Workouts with time-based steps must include a final distance step when needed to reach their declared distance; the plan loader validates this invariant.
- Warmup, active, recovery, and cooldown steps include matching `intensity` annotations where configured.
- YAML is strict: unknown keys are treated as errors so typos (for example `target_hrr`) fail fast.
- HR ranges are validated in runtime across workout targets, default targets, and zone profile with `min <= max`.
- Intervals.icu parses the description into its native structured workout representation; the CLI does not send a custom `workout_doc`.
- In Intervals settings, enable Garmin sync (`Upload planned workouts`) so upcoming workouts are sent to Garmin Connect.
