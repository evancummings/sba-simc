# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with this repository.

## What this project does

`sba-simc` is a nightly CI pipeline that:
1. Pulls the community `simulationcraftorg/simc` Docker image
2. Runs SimulationCraft for every WoW class/spec/hero-talent combination using both the **optimal community APL** and **Blizzard's Assisted Combat (Single Button Assistant) APL**
3. Computes the DPS delta between the two
4. Generates a static comparison website deployed to GitHub Pages

The point is to let players quickly see how much performance Blizzard's Single Button Assistant leaves on the table for their spec.

## Commands

```bash
# Restore NuGet packages
dotnet restore src/SbaSimc/SbaSimc.csproj

# Run the full pipeline locally (requires Docker)
dotnet run --project src/SbaSimc/SbaSimc.csproj

# Run with a custom iteration count (env var override)
SIMC__Iterations=100 dotnet run --project src/SbaSimc/SbaSimc.csproj

# Build release binary
dotnet build src/SbaSimc/SbaSimc.csproj --configuration Release

# Probe the SimC container's file layout after an image update
docker run --rm --entrypoint /bin/sh simulationcraftorg/simc:latest -c "ls /app/SimulationCraft/profiles/"
docker run --rm --entrypoint /bin/sh simulationcraftorg/simc:latest -c "ls /app/SimulationCraft/profiles/MID1/"

# Confirm Blizzard APL is active by checking profile_source in the JSON output
docker run --rm -v "C:\Temp\simc-test:/output" simulationcraftorg/simc:latest \
  /app/SimulationCraft/profiles/MID1/MID1_Warrior_Arms.simc \
  iterations=10 source=blizzard json2=/output/test.json
```

## Architecture

```
.github/workflows/nightly.yml   → Cron job: pull image → run sims → deploy
src/SbaSimc/
  Program.cs                    → Entry point; wires config, runs pipeline
  AppConfig.cs                  → SimcConfig and OutputConfig POCOs
  specs.json                    → All (class, spec, hero talent) combos to simulate
  Models/
    WowSpec.cs                  → Record representing one simulatable combination
    SimulationResult.cs         → Result + DeltaPercent/Severity helpers
  Services/
    SpecLoader.cs               → Deserialises specs.json
    SimcRunner.cs               → Shells out to `docker run` to invoke simc
    ResultParser.cs             → Extracts DPS mean from simc json2 output
    SiteGenerator.cs            → Renders output/index.html via Scriban template
templates/
  index.html.sbn                → Scriban template for the static site
output/                         → Generated site (gitignored; deployed to gh-pages)
```

### Data flow

1. `SpecLoader` reads `specs.json` → list of `WowSpec` records
2. `Program.cs` fans out with `Parallel.ForEachAsync` (bounded by `SimcConfig.MaxParallelism`)
3. For each spec, `SimcRunner` runs two `docker run --rm` containers: one with the default APL, one with `source=blizzard`
4. `ResultParser` extracts `sim.players[0].collected_data.dps.mean` from the `json2` output
5. `SiteGenerator` renders `templates/index.html.sbn` with Scriban and writes `output/index.html`
6. GitHub Actions deploys `output/` to the `gh-pages` branch

### Configuration

Settings live in `src/SbaSimc/appsettings.json` and are overridable via environment variables using double-underscore as the section separator:

| Setting | Env var | Default | Notes |
|---|---|---|---|
| `SimC.Iterations` | `SIMC__Iterations` | `1000` | Lower = faster; higher = more accurate. Set in GitHub repo variables as `SIMC_ITERATIONS`. |
| `SimC.MaxParallelism` | `SIMC__MaxParallelism` | `4` | Each unit spawns a Docker container. |
| `SimC.DockerImage` | `SIMC__DockerImage` | `simulationcraftorg/simc:latest` | |
| `SimC.ContainerProfilesPath` | `SIMC__ContainerProfilesPath` | `/app/SimulationCraft/profiles/MID1` | Update after image changes (see below). |
| `Output.Directory` | `OUTPUT__Directory` | `./output` | |

## SimC container internals (verified 2026-05-29, SimC 1205-01, WoW 12.0.5.67823)

- **Binary:** `/app/SimulationCraft/simc`
- **Profiles:** `/app/SimulationCraft/profiles/MID1/` — prefix `MID1_` (Midnight Season 1)
- **No assist_combat files on disk** — Blizzard's APL is compiled into the binary per-class
- **Blizzard APL flag:** `source=blizzard` — confirmed working; sets `profile_source: "blizzard"` in json2 output
- **JSON output path in each result:** `sim.players[0].collected_data.dps.mean`
- **Hero talent per profile:** Each `.simc` file's first line contains the character name which includes the hero talent (e.g. `warrior="MID1_Warrior_Arms_Slayer"`). Profiles without a hero talent suffix in their character name handle multiple hero talents via talent checks in the APL, or represent the single viable build.

## specs.json — hero talent source of truth

Each entry maps a display label to a real profile filename. The `heroTalent` field is a display-only label derived from the character name embedded in the `.simc` file. Entries labelled `"Default"` mean the profile's internal talent string determines the hero talent (check the first line of the `.simc` file).

**Current expansion:** Midnight (MID1). As of WoW 12.0.5, Demon Hunter has a third spec: **Devourer**.

## Updating for new patches

When a new WoW patch or expansion drops and the SimC image is updated:

1. Probe the new image: `docker run --rm --entrypoint /bin/sh simulationcraftorg/simc:latest -c "ls /app/SimulationCraft/profiles/"`
2. If the tier prefix changed (e.g. `MID1` → `MID2`), update `ContainerProfilesPath` in `appsettings.json` and all `simcProfileName` values in `specs.json`
3. Check for new/removed specs or hero talents by listing the new profiles directory
4. Verify `source=blizzard` still works: check that `profile_source` in the json2 output reads `"blizzard"`
5. Trigger the workflow manually via `workflow_dispatch` to validate

## One remaining TODO

Enable GitHub Pages in the repository settings (source: `gh-pages` branch). The workflow will create it on first deploy.

## Site template

`templates/index.html.sbn` is a self-contained single-page app (vanilla JS, no framework). It includes:
- Class filter buttons
- Sortable columns
- Color-coded delta bars (green < 5% loss, yellow 5–15%, red > 15%)
- Summary pills with counts per severity bucket

The template receives these variables from `SiteGenerator.cs`: `results`, `classes`, `generated_at`, `simc_version`, `total_specs`, `good_count`, `moderate_count`, `poor_count`.
