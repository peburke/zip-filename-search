# ZIP Filename Search

[Download the Windows executable](https://github.com/peburke/zip-filename-search/releases/latest) from the latest release. Source, build scripts, and 47 automated checks are included in this repository.

Run **ZipFilenameSearch.exe** on Windows 11. No installation or administrator access required. This single executable uses Windows' built-in .NET Framework 4.8; it does not require Python or a separately installed .NET SDK. The executable is unsigned; Windows may show a publisher/reputation prompt.

1. Choose a ZIP file or a folder containing ZIPs.
2. For folder searches, choose whether to include subfolders.
3. Enter one or more filename patterns separated by commas and click **Search**. Example: `*.pdf, report*.docx, photo.jpg`.
4. Click a file's **Internal path** link to open it in its default Windows app. Click the Archive cell to select rows without opening them; use **Copy selected rows** (or Ctrl+C). Use Ctrl+A to select all results.
5. To save a found file, right-click its row, choose **Extract to folder…**, and select a destination folder. The clicked file is saved directly in that folder using its filename, without recreating the archive's internal folders. Existing files require confirmation before replacement. Extraction leaves the source ZIP unchanged and does not open the saved file.

Opening extracts only the clicked file into a unique folder under `%TEMP%\ZipFilenameSearch`, then asks Windows to open it. Temporary copies remain available after this utility closes so the other app can keep using them. You can remove that temporary folder when finished with the files. Edits to an opened copy do not update the original ZIP. Files that depend on companion files inside the archive may need manual extraction of those companions. If extraction fails or no default app is available, the utility displays an error.

Patterns match the entire filename, case-insensitively: `*.pdf`, `report*`, `*invoice*`, `photo_??.jpg`, or an exact name. `*` matches zero or more characters and `?` matches one character. Separate patterns with commas to match any of them. Spaces around each pattern and empty terms are ignored. Each matching archive entry appears once even if several patterns match it. Other symbols are literal. By default, patterns match the filename only. Check **Search full internal path** to match folders and filename inside the ZIP. Commas always separate patterns; to match a comma within a filename, use `?` in its place.

Searching reads ZIP directories without extracting files or reading their contents. Opening a result extracts that file. Duplicate filenames in different folders appear separately. Errors appear below the results, and other archives continue to be searched. Cancel retains results already found. Searches stop at 100,000 matches; narrow the pattern if the limit is reached. Closing during a search requests cancellation first. Opening a very large archive's directory can delay cancellation.

Supported scope: ZIP archives on accessible local or network paths. Nested ZIPs are not searched, and folder junctions/symbolic links are skipped during recursive discovery. Ordinary encrypted entry contents do not need decryption to list visible names; encrypted or unsupported archive directories may fail. This is a filename search tool, not a content search or archive repair tool.

## Build on Windows

Source is included. Open PowerShell in this folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
```

The build uses the Windows .NET Framework C# compiler and writes `ZipFilenameSearch.exe` beside the source. If the compiler is missing, install the .NET Framework 4.8 Developer Pack. The execution-policy override applies only to that command's process.

`SearchEngine.cs` contains search and discovery logic; `Program.cs` contains the Windows Forms GUI; `Tests.cs` tests actual temporary ZIP archives and wildcard behavior. No third-party packages or internet access are needed to build.

Search recovery: filename, encoding, access, and corrupt-archive errors are reported with their exception type, and the search continues to later archives. If the Windows ZIP reader cannot load an archive directory because of a malformed entry name, that archive is skipped; its other entries may not be searchable. Unusual names that the reader accepts remain searchable.

## Regular expression mode
Check **Use regular expressions** to interpret the entire search field as a .NET regular expression. Matching is case-insensitive and applies to the filename, or folders plus filename when **Search full internal path** is checked. Regex matches any part of the selected name or path; use ^ and $ to anchor the whole name. Example: `^report_[0-9]{2,4}\.(pdf|docx)$`. Use `|` for alternatives. Commas are part of the expression, so quantifiers such as `{2,4}` work. Uncheck the option to return to comma-separated wildcards. Invalid expressions are reported before searching. Regex matches have a 250 ms per-filename timeout; an expression that exceeds it stops the search with an error.

Full internal path example: enable **Search full internal path** and use `example` or `.*example.*` in regex mode, or `*example*` in wildcard mode, to find files beneath matching folders. Use `/` for folder separators in patterns. Results remain files only; directory entries are excluded. This option searches paths inside ZIPs, not the location or filename of the ZIP itself.