# Release checklist (Mac developer)

You never need a local Windows PC to **build**. You only need Windows hardware to **test** USB.

## Build a new `.exe` (from your Mac)

1. Push to GitHub, **or** open **Actions → CI → Run workflow** (manual trigger).
2. Wait for the green **windows-latest** job (~3–5 min).
3. Open the job → **Artifacts** → download `15CEFlasher-Win-0.1.0-100.exe` (CDN filename is stable; bump `APP_BUILD` / `FileVersion` for testers).

The job summary lists **SHA-256** for the machii-labs page.

## Publish for testers

1. Upload the artifact to **downloads.machiilabs.com** (same bucket as Mac DMGs).
2. In **machii-labs** repo:
   - `src/lib/downloads.ts` → `PRODUCT_DOWNLOADS.winflasher` URL
   - `src/app/winflasher/page.tsx` → version, build, filename, SHA-256
3. Deploy machii-labs (Vercel).
4. Send testers: **https://machiilabs.com/winflasher**

## Bump version

Edit `.github/workflows/ci.yml` `APP_VERSION` / `APP_BUILD` and `src/FifteenCEFlasher/FifteenCEFlasher.csproj` `Version` / `FileVersion` together. Leave `CDN_FILENAME` as `15CEFlasher-Win-0.1.0-100.exe` during beta so R2 overwrites the same object.

## Local dev on Mac

Core library + unit tests only:

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"
dotnet test tests/FifteenCEFlasherCore.Tests/FifteenCEFlasherCore.Tests.csproj -c Release
```

WPF app requires Windows (CI or VM).
