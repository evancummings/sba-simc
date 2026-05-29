# SBA SimC

A nightly CI pipeline that quantifies how much DPS players lose by using Blizzard's **Single Button Assistant (SBA)** — the "Assisted Combat" one-button APL — compared to playing the optimal community rotation for every World of Warcraft class, spec, and hero talent combination.

Results are published automatically to GitHub Pages each morning.

## How it works

### Overview

1. A GitHub Actions workflow runs every day at 06:00 UTC
2. It pulls the latest `simulationcraftorg/simc` Docker image
3. For every spec/hero-talent combo in `specs.json`, it runs two SimulationCraft simulations: one with the optimal community APL, one with `source=blizzard` (Blizzard's SBA)
4. It computes the DPS delta between the two and generates a static comparison website
5. The site is deployed to the `gh-pages` branch and served via GitHub Pages

### Pipeline detail

```
specs.json
    │
    ▼
SpecLoader          reads all (class, spec, hero talent) combinations
    │
    ▼
SimcRunner × N      spawns two `docker run` containers per spec (parallel, up to MaxParallelism)
  ├── optimal run   uses the community APL baked into the .simc profile
  └── blizzard run  adds `source=blizzard` to activate Blizzard's compiled-in APL
    │
    ▼
ResultParser        extracts `sim.players[0].collected_data.dps.mean` from each json2 output
    │
    ▼
SimulationResult    computes DeltaPercent = (optimal - blizzard) / optimal × 100
    │
    ▼
SiteGenerator       renders templates/index.html.sbn → output/index.html
    │
    ▼
GitHub Pages        serves the static site
```

### Severity buckets

| Label | DPS loss |
|---|---|
| Good | < 5% |
| Moderate | 5–15% |
| Poor | > 15% |

## Repository structure

```
.github/workflows/nightly.yml   → Cron workflow: pull image → run sims → deploy
src/SbaSimc/
  Program.cs                    → Entry point; wires config, fans out with Parallel.ForEachAsync
  AppConfig.cs                  → SimcConfig and OutputConfig POCOs
  specs.json                    → All (class, spec, hero talent) combos to simulate
  Models/
    WowSpec.cs                  → Record for one simulatable combination
    SimulationResult.cs         → Holds DPS values + DeltaPercent/Severity helpers
  Services/
    SpecLoader.cs               → Deserialises specs.json
    SimcRunner.cs               → Shells out to `docker run simulationcraftorg/simc`
    ResultParser.cs             → Extracts DPS mean from simc json2 output
    SiteGenerator.cs            → Renders output/index.html via Scriban template
templates/
  index.html.sbn                → Single-page app: class filters, sortable columns, delta bars
output/                         → Generated site (gitignored; deployed to gh-pages)
```

## Running locally

Prerequisites: .NET 8 SDK and Docker.

```bash
# Restore packages
dotnet restore src/SbaSimc/SbaSimc.csproj

# Run the full pipeline (pulls simc image if not cached, then simulates everything)
dotnet run --project src/SbaSimc/SbaSimc.csproj

# Speed it up for local testing — lower iterations = less accurate but much faster
SIMC__Iterations=100 dotnet run --project src/SbaSimc/SbaSimc.csproj
```

The generated site lands in `output/index.html`.

## Configuration

Settings are in `src/SbaSimc/appsettings.json` and overridable via environment variables (double-underscore as the section separator):

| Setting | Env var | Default |
|---|---|---|
| `SimC.Iterations` | `SIMC__Iterations` | `1000` |
| `SimC.MaxParallelism` | `SIMC__MaxParallelism` | `4` |
| `SimC.DockerImage` | `SIMC__DockerImage` | `simulationcraftorg/simc:latest` |
| `SimC.ContainerProfilesPath` | `SIMC__ContainerProfilesPath` | `/app/SimulationCraft/profiles/MID1` |
| `Output.Directory` | `OUTPUT__Directory` | `./output` |

In GitHub Actions, set `SIMC_ITERATIONS` as a repository variable to control the nightly iteration count, or pass it as a `workflow_dispatch` input for a one-off run.

## Updating for a new patch or expansion

When SimC's Docker image changes after a WoW patch:

1. Probe the new image for profile paths:
   ```bash
   docker run --rm --entrypoint /bin/sh simulationcraftorg/simc:latest -c "ls /app/SimulationCraft/profiles/"
   ```
2. If the tier prefix changed (e.g. `MID1` → `MID2`), update `ContainerProfilesPath` in `appsettings.json` and all `simcProfileName` values in `specs.json`
3. Check for new or removed specs/hero talents by listing the profiles directory
4. Verify `source=blizzard` still sets `profile_source: "blizzard"` in the json2 output
5. Trigger a manual run via `workflow_dispatch` to validate

## GitHub Pages setup

Enable GitHub Pages in the repository settings with source set to the `gh-pages` branch. The workflow creates that branch automatically on first deploy.
