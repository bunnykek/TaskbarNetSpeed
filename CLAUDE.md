# TaskbarNetSpeed

Single-file C# (.NET Framework 4.x, C# 5 syntax only: no `$""`, `?.`, `=>` members) taskbar speed overlay. Build with `build.bat`; output goes to `bin/`.

## Versioning (required for every change)

The version lives only in `VERSION` (MAJOR.MINOR.PATCH). `build.bat` stamps it into the exes.

With every change to the app:
1. Bump `VERSION`:
   - PATCH: bug fixes and small tweaks with no new behaviour.
   - MINOR: new features or options, backwards compatible.
   - MAJOR: breaking changes, such as settings that no longer carry over.
2. Add an entry at the top of `CHANGELOG.md` with the new version, today's date, and the changes under Added / Changed / Fixed / Removed.
3. Rebuild with `build.bat`.

Docs-only changes (README, CHANGELOG wording) don't bump the version.
