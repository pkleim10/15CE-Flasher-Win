# 15CE Flasher — Windows

Windows port of **15CE Flasher** for the HP 15c Collector's Edition. Separate repo from the Mac [HP15C-Flasher](https://github.com/mach-ii-labs/HP15C-Flasher) project.

## For testers

**Download:** https://machiilabs.com/winflasher — one self-contained `.exe`, no .NET install.

See [docs/TESTERS.md](docs/TESTERS.md).

## For developers (Mac)

| Task | Where |
|------|--------|
| Edit protocol / flash logic | Mac — `FifteenCEFlasherCore` + unit tests |
| Build shippable `.exe` | **GitHub Actions** (`windows-latest`) — push or **Run workflow** |
| Test USB / cable | Borrowed Windows PC + hardware |

See [docs/RELEASE.md](docs/RELEASE.md).

## Features

- **Connection Probe** — autodetect Atmel COM (`03EB:6124`), ignore FTDI (`0403:6015`), SAM-BA CHIPID
- **FLASH wizard** — 7-step guided flash (backup, firmware pick, write, verify)
- **BATCH mode** — sequential flashing for shop workflows
- **DEMO mode** — in-app simulator, no hardware

## Requirements

- **Testers:** Windows 10/11 x64, programming cable — see [docs/TESTERS.md](docs/TESTERS.md)
- **Mac dev:** .NET 8 SDK (`brew install dotnet@8`) for core/tests only

## Download (beta)

https://machiilabs.com/winflasher

## Protocol

SAM-BA 2.16 monitor + official `applet-flash-sam4l4.bin`. Application flash at `0x4000`, 112 KB (`0x1C000`). Ported from Mac `HP15CFlasherCore`.

## License

Copyright © Mach II Labs. SAM-BA applet: Atmel Corporation (see `THIRD_PARTY.md`).
