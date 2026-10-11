# AdvancedInstaller.com build output

Standard output of `AdvancedInstaller.com` (Advanced Installer 24.0), captured byte for byte from a redirected
process with no console, on a scratch Professional project made with `/newproject` and `/edit /AddFile`. The
scratch folder was replaced with `C:\Work`. The parser tests in ADR 0002 read these files.

| File | Command | Exit code |
|---|---|---|
| `success.txt` | `/build Sample.aip` (one build) | `0` |
| `up-to-date.txt` | `/build Sample.aip` again, nothing changed | `0` |
| `missing-resource.txt` | `/build` after the added file was renamed (`/rebuild` prints the same) | `0xE0010064` |
| `unknown-build.txt` | `/build Sample.aip -buildslist NoSuchBuild` | `0xE0010064` |
| `two-builds-ai-ice.txt` | `/build` with a second build row and a custom action only in `InstallUISequence` | `0` |
| `one-of-two-up-to-date.txt` | `/build` after deleting only the second build's package; nothing is printed for the first | `0` |
| `non-ascii-path.txt` | `/rebuild "Çıktı Ş\Örnek.aip"`, output is UTF-8 | `0` |
| `project-not-found.txt` | `/build NoSuch.aip` | `0xFFFFFFFF` (undocumented) |
