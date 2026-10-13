# Unofficial English patch for Xiaomi PC Manager

> **Unofficial.** This project is not affiliated with, endorsed by or supported by Xiaomi.
> "Xiaomi" and "Mi" are trademarks of Xiaomi Inc. No Xiaomi software is distributed here:
> the tool modifies the copy of Xiaomi PC Manager that is already installed on your PC.

`XpmTranslator.exe` translates **Xiaomi PC Manager 5.8.1.130** (Chinese only) into English and fixes
a few bugs that show up on non-Chinese Windows. It patches the files of your own installation; nothing is
downloaded and Xiaomi's servers/updates keep working.

## Download

Get `XpmTranslator.exe` from [Releases](../../releases). It is built from this repository by GitHub Actions;
the SHA-256 of every build is listed in the release notes.

## Use
1. Run `XpmTranslator.exe` (it asks for administrator rights — it writes to `C:\Program Files\MI\XiaomiPCManager`).
2. Keep the components you want ticked and press **Apply**. PC Manager is closed and started again.
3. **Restore originals** puts every original file back. Unticking a component and pressing Apply restores just that one.

Originals are backed up to `%ProgramData%\XpmTranslator\<version>\` before anything is replaced, together with
a manifest of what was written. The tool only patches files whose SHA-256 matches the stock 5.8.1.130 files;
if a file was changed earlier (e.g. by a manual translation attempt) it looks for the original in its backup,
in `<file>.bak*` next to it, and in a `backup\` folder next to the tool.

Command line (for scripts): `XpmTranslator.exe status | apply [all|ids…] | restore | build <dir> [ids…]`,
options `--app <dir>`, `--originals <dir>`, `--store <dir>`, `--no-restart`.

### Before you use it
- Use at your own risk. Modifying PC Manager may be against its license terms.
- Patched `.dll` files lose Xiaomi's digital signature; some antivirus programs may complain about them
  or about the patcher itself.
- Updating PC Manager replaces the patched files — run the tool again. Files of other versions than 5.8.1.130
  don't match the known hashes and are not patched; a new version needs its dictionaries checked first
  (see [Source](#source)).

## Components
| id | What | Files |
|---|---|---|
| `web` | Main window: all 831 texts of the web UI, plus PC Cleanup group/item names (they come from the native cleaner at run time, `Data\cleanup.json`) | `dist\static\js\main.js` |
| `pri` | Control Center (tray), Mi File Transfer, pop-ups, update/exit dialogs, subtitles window — 540 strings | `resources.pri` |
| `asm` | Texts built into code: transfer notifications, earphone hint, subtitle defaults | `PcControlCenter.dll`, `SubtitleTranscriptor.dll` |
| `osd` | Fn-key on-screen pop-ups (captions are drawn into the images) | 476 × `res\Image\*.png` |
| `skin` | "Non-original charger" warning window | `skin\xml\LowPowerAlertWnd.xml`, `skin\image\NoLongerRemind*.png` |
| `privacy` | Privacy policy shown on first start and the "withdraw consent" notice (unofficial translation, marked as such; the Chinese original remains the binding version) | `Assets\privacy_content\privacy.txt`, `cancel_privacy.txt` |
| `layout` | Layout fixes so English fits: cards, sidebar, header, drop-downs, buttons | `dist\index.html`, new `dist\static\css\custom.css` |
| `mui` | **Crash fix.** WinUI loads its own strings from `<lang>\Microsoft.ui.xaml.dll.mui`; only `zh-CN` is shipped, so on other Windows languages every WinUI dialog (e.g. "new version available") throws `0x80073B01` and the app closes. Copies the genuine en-US file from the Windows App Runtime 1.4 installed on your PC after checking its MUI checksums match. | new `en-US\Microsoft.ui.xaml.dll.mui` |
| `otalog` | **"????" in the update window.** Xiaomi's native code converts the release notes through the ANSI code page, so Chinese text arrives as `?`. Such unreadable notes are replaced with "Bug fixes and improvements."; readable text is shown as is. | `XiaomiPcManager.dll` |

Not covered: the AI File Search window (`Search\`), the clipboard window (`PcClipboard\`), a few strings inside
the signed native `OSDUtility.exe`.

## Source

`src` — C# (.NET 10, WinForms, Mono.Cecil). Build:

```
dotnet publish src -c Release -o publish
```

- `Patches\WebBundle.cs` — finds every `\uXXXX`-escaped Chinese literal in the minified bundle by expanding
  to the enclosing quotes, skips logging calls / the weather city table / Lottie layer names, replaces whole
  literals from `Data\web.json`.
- `Patches\PriFile.cs` — rewrites `[mrm_dataitem]` sections of the PRI file (UTF-8/UTF-16 strings, also short
  strings stored as blobs) and recomputes the TOC; resource maps reference items by index.
- `Patches\Assembly.cs` — string literal replacement and the injected `__SanitizeUnreadable` filter.
- `Patches\Mui.cs` — MUI checksum check and framework lookup.
- `Data\` — embedded dictionaries, CSS, pre-rendered images, `originals.json` (hashes of stock files).

`tools` — how the data was made:
- `translations\` — source lists (`js_todo.json` + `js_en.txt`, `pri_todo.json` + `pri_en.txt`, assembly maps).
  In the `*_en.txt` files a trailing `§` means a trailing space.
- `scripts\make_data.py` regenerates `Data\`; `extract_js.py`/`filter_js.py`/`pri_todo.py` list strings of a new
  version; `osd_render.py`/`skin_render.py` redraw captions with MiSans/Segoe UI.
- `harness\shot.js` — renders the web UI in headless Edge with a stubbed WebView2 bridge and reports text that
  overflows its box (used to tune `custom.css`).

The scripts work on files taken from your own PC Manager installation; those files are not part of this
repository and must not be committed.

## Releasing

Push a tag `vX.Y.Z` — the [release workflow](.github/workflows/release.yml) builds `XpmTranslator.exe` on
Windows with version X.Y.Z and publishes it as a GitHub release. Pushes and pull requests to `master` only build.

## License

MIT for the code and translations of this project — see [LICENSE](LICENSE). It grants no rights to Xiaomi's
software or materials.
