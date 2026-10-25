using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using ZipFilenameSearch;
class Tests {
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
    static void Make(string path, params string[] names) { using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) foreach (var name in names) zip.CreateEntry(name); }
    static void Main() {
        string root = Path.Combine(Path.GetTempPath(), "ZipSearchTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            string a = Path.Combine(root, "one.zip"); Make(a, "docs/Report.PDF", "other/report.pdf", "photo_01.jpg", "folder/", "資料/明細.pdf", "a[1].txt", "windows\\note.txt");
            Directory.CreateDirectory(Path.Combine(root, "sub")); Make(Path.Combine(root, "sub", "two.ZIP"), "second.pdf"); File.WriteAllText(Path.Combine(root, "ignore.txt"), "x");
            var matches = new List<MatchResult>(); SearchEngine.SearchArchive(a, SearchEngine.Pattern("*.pdf"), matches.Add, () => false);
            Check(matches.Count == 3, "Wildcard, mixed case and Unicode"); Check(matches[0].Archive == a && matches[0].InternalPath == "docs/Report.PDF", "Archive and internal path preserved");
            Check(SearchEngine.Pattern("photo_??.jpg").IsMatch("photo_01.jpg") && !SearchEngine.Pattern("photo_??.jpg").IsMatch("photo_1.jpg"), "Question mark matches one character");
            Check(SearchEngine.Pattern("a[1].txt").IsMatch("a[1].txt") && !SearchEngine.Pattern("a[1].txt").IsMatch("a1.txt"), "Regex symbols treated literally");
            Check(!SearchEngine.Pattern("report").IsMatch("report.pdf"), "Full filename matching");
            matches.Clear(); SearchEngine.SearchArchive(a, SearchEngine.Pattern("*"), matches.Add, () => false); Check(matches.Count == 6, "Directory entries excluded");
            matches.Clear(); SearchEngine.SearchArchive(a, SearchEngine.Pattern("note.txt"), matches.Add, () => false); Check(matches.Count == 1, "Backslash entry separator");
            Check(SearchEngine.Archives(root, false, x => {}, () => false).Count() == 1, "Nonrecursive ZIP discovery"); Check(SearchEngine.Archives(root, true, x => {}, () => false).Count() == 2, "Recursive ZIP discovery and uppercase extension");
            Check(SearchEngine.Archives(a, false, x => {}, () => false).Single() == a, "Single archive selection");
            matches.Clear(); SearchEngine.SearchArchive(a, SearchEngine.Pattern("*"), matches.Add, () => true); Check(matches.Count == 0, "Entry cancellation");
            Check(!SearchEngine.Archives(root, true, x => {}, () => true).Any(), "Discovery cancellation");
            string bad = Path.Combine(root, "bad.zip"); File.WriteAllText(bad, "not a ZIP"); bool rejected = false; try { SearchEngine.SearchArchive(bad, SearchEngine.Pattern("*"), matches.Add, () => false); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Corrupt ZIP reported");
            bool empty = false; try { SearchEngine.Pattern(" "); } catch (ArgumentException) { empty = true; } Check(empty, "Blank pattern rejected");
            string openZip = Path.Combine(root, "open.zip");
            using (var zip = ZipFile.Open(openZip, ZipArchiveMode.Create)) {
                int entryNumber = 0; foreach (string name in new[] { "docs/example.txt", "docs/example.txt", "../../escape.txt", "資料/明細.txt", "bad:name.txt" })
                    using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("entry " + (++entryNumber));
            }
            matches.Clear(); SearchEngine.SearchArchive(openZip, SearchEngine.Pattern("*"), matches.Add, () => false);
            string temp = Path.Combine(root, "extracted");
            string first = SearchEngine.ExtractForOpening(matches[0], temp);
            Check(File.ReadAllText(first) == "entry 1" && Path.GetFileName(first) == "example.txt", "Extract contents and preserve extension");
            string second = SearchEngine.ExtractForOpening(matches[1], temp);
            Check(first != second && File.ReadAllText(second) == "entry 2", "Duplicate paths open the selected entry without overwriting");
            string traversal = SearchEngine.ExtractForOpening(matches[2], temp);
            Check(traversal.StartsWith(temp + Path.DirectorySeparatorChar) && Path.GetFileName(traversal) == "escape.txt", "Archive traversal stays inside temporary directory");
            Check(Path.GetFileName(SearchEngine.ExtractForOpening(matches[3], temp)) == "明細.txt", "Unicode extraction");
            bool invalid = false; try { SearchEngine.ExtractForOpening(matches[4], temp); } catch (IOException) { invalid = true; } Check(invalid, "Invalid Windows filename rejected");
            matches[0].InternalPath = "missing.txt";
            bool changed = false; try { SearchEngine.ExtractForOpening(matches[0], temp); } catch (IOException) { changed = true; } Check(changed, "Stale result rejected");
            var searchErrors = new List<string>(); matches.Clear();
            foreach (string archive in new[] { "bad\0name.zip", bad, a })
                SearchEngine.SearchArchiveSafely(archive, SearchEngine.Pattern("*.pdf"), matches.Add, () => false, searchErrors.Add);
            Check(searchErrors.Count == 2 && matches.Count == 3, "Invalid archive name and corrupt ZIP do not stop later archives");
            Check(searchErrors[0].Contains("ArgumentException") && searchErrors[1].Contains("InvalidDataException"), "Search errors include diagnostic exception type");
            string unusual = Path.Combine(root, "unusual.zip");
            Make(unusual, "bad:name.txt", "what?.txt", "CON.txt", "trailing. ", new string('x', 300) + ".txt", "normal.txt");
            matches.Clear(); searchErrors.Clear();
            SearchEngine.SearchArchiveSafely(unusual, SearchEngine.Pattern("*"), matches.Add, () => false, searchErrors.Add);
            Check(matches.Count == 6 && searchErrors.Count == 0, "Unusual ZIP entry names remain searchable without Windows path validation");
            string malformed = Path.Combine(root, "malformed.zip"); Make(malformed, "null_name.txt");
            byte[] bytes = File.ReadAllBytes(malformed);
            byte[] marker = System.Text.Encoding.ASCII.GetBytes("null_name.txt");
            for (int i = 0; i <= bytes.Length - marker.Length; i++) {
                bool equal = true; for (int j = 0; j < marker.Length; j++) if (bytes[i + j] != marker[j]) equal = false;
                if (equal) bytes[i + 4] = 0;
            }
            File.WriteAllBytes(malformed, bytes); matches.Clear(); searchErrors.Clear();
            foreach (string archive in new[] { malformed, a }) SearchEngine.SearchArchiveSafely(archive, SearchEngine.Pattern("*.pdf"), matches.Add, () => false, searchErrors.Add);
            Check(searchErrors.Count == 1 && matches.Count == 3, "Malformed entry name is reported and next archive is searched");
            Check(SearchEngine.IsReadError(new System.Text.DecoderFallbackException()) && SearchEngine.IsReadError(new System.Security.SecurityException()), "Encoding and access failures are recoverable");
            matches.Clear(); SearchEngine.SearchArchive(a, SearchEngine.Pattern(" *.PDF, photo_??.jpg, note.txt "), matches.Add, () => false);
            Check(matches.Count == 5, "Comma-separated wildcards and exact names match any term");
            matches.Clear(); SearchEngine.SearchArchive(a, SearchEngine.Pattern("*.pdf, Report.PDF, *.pdf"), matches.Add, () => false);
            Check(matches.Count == 3, "Overlapping patterns do not duplicate results");
            var multiple = SearchEngine.Pattern(", report.pdf, , note.txt, ");
            Check(multiple.IsMatch("report.pdf") && multiple.IsMatch("note.txt") && !multiple.IsMatch("other.txt") && !multiple.IsMatch("myreport.pdf") && !multiple.IsMatch("note.txt.bak"), "Blank terms ignored and each alternative matches the whole name");
            Check(SearchEngine.Pattern("a[1].txt, 明細.pdf").IsMatch("a[1].txt") && SearchEngine.Pattern("a[1].txt, 明細.pdf").IsMatch("明細.pdf"), "Multiple patterns preserve literals and Unicode");
            bool commasOnly = false; try { SearchEngine.Pattern(" , , "); } catch (ArgumentException) { commasOnly = true; } Check(commasOnly, "Comma-only input rejected");
            matches.Clear(); SearchEngine.SearchArchive(openZip, SearchEngine.Pattern("*"), matches.Add, () => false);
            string chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            string saved = SearchEngine.ExtractToDirectory(matches[0], chosen, false);
            Check(saved == Path.Combine(chosen, "example.txt") && File.ReadAllText(saved) == "entry 1", "Extract selected file directly to chosen folder");
            bool collision = false; try { SearchEngine.ExtractToDirectory(matches[1], chosen, false); } catch (IOException) { collision = true; }
            Check(collision && File.ReadAllText(saved) == "entry 1", "Unapproved overwrite preserves existing file");
            SearchEngine.ExtractToDirectory(matches[1], chosen, true);
            Check(File.ReadAllText(saved) == "entry 2" && Directory.GetDirectories(chosen).Length == 0, "Approved overwrite and staging cleanup");
            matches[0].InternalPath = "missing.txt"; File.WriteAllText(Path.Combine(chosen, "missing.txt"), "keep");
            bool failed = false; try { SearchEngine.ExtractToDirectory(matches[0], chosen, true); } catch (IOException) { failed = true; }
            Check(failed && File.ReadAllText(Path.Combine(chosen, "missing.txt")) == "keep", "Failed extraction preserves destination");
            Check(SearchEngine.ExtractToDirectory(matches[2], chosen, false) == Path.Combine(chosen, "escape.txt"), "Extraction ignores archive traversal directories");
            bool sourceProtected = false; try { SearchEngine.ExtractionPath(new MatchResult { Archive = openZip, InternalPath = "open.zip" }, root); } catch (IOException) { sourceProtected = true; }
            Check(sourceProtected, "Source archive cannot be overwritten");
            matches.Clear(); SearchEngine.SearchArchive(a, SearchEngine.Pattern(@"\.pdf$|^photo_[0-9]{2,4}\.jpg$", true), matches.Add, () => false);
            Check(matches.Count == 4, "Regex alternation and comma-containing quantifiers search ZIP entries");
            Check(SearchEngine.Pattern("report", true).IsMatch("MyReport.PDF") && !SearchEngine.Pattern("^report$", true).IsMatch("MyReport.PDF"), "Regex substring matching, anchors and case-insensitivity");
            Check(SearchEngine.Pattern("one,two", true).IsMatch("one,two.txt") && !SearchEngine.Pattern("one,two", true).IsMatch("one.txt"), "Regex commas are not pattern separators");
            bool invalidRegex = false; try { SearchEngine.Pattern("[", true); } catch (ArgumentException) { invalidRegex = true; } Check(invalidRegex, "Invalid regex rejected before searching");
            bool blankRegex = false; try { SearchEngine.Pattern(" ", true); } catch (ArgumentException) { blankRegex = true; } Check(blankRegex, "Blank regex rejected");
            bool timedOut = false; try { SearchEngine.Pattern("^(a+)+$", true).IsMatch(new string('a', 10000) + "!"); } catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { timedOut = true; } Check(timedOut, "Expensive regex interrupted by timeout");
            string folderZip = Path.Combine(root, "folders.zip"); Make(folderZip, "inbox/example-contact/photo.png", "inbox/example-contact/", "inbox/other/photo.png", "windows\\example\\note.txt");
            matches.Clear(); SearchEngine.SearchArchive(folderZip, SearchEngine.Pattern("example", true), matches.Add, () => false);
            Check(matches.Count == 0, "Filename-only mode does not match folder names");
            SearchEngine.SearchArchive(folderZip, SearchEngine.Pattern("example", true), matches.Add, () => false, true);
            Check(matches.Count == 2, "Full-path regex finds files in matching folders and excludes directory entries");
            matches.Clear(); SearchEngine.SearchArchive(folderZip, SearchEngine.Pattern("*example*"), matches.Add, () => false, true);
            Check(matches.Count == 2, "Full-path wildcard finds files in matching folders");
            matches.Clear(); SearchEngine.SearchArchiveSafely(folderZip, SearchEngine.Pattern(@"^windows/example/.*\.txt$", true), matches.Add, () => false, x => { throw new Exception(x); }, true);
            Check(matches.Count == 1 && matches[0].InternalPath == "windows\\example\\note.txt", "Full-path wrapper normalizes separators while preserving original result path");
            matches.Clear(); SearchEngine.SearchArchive(folderZip, SearchEngine.Pattern("inbox/other/*.png, windows/example/*.txt"), matches.Add, () => false, true);
            Check(matches.Count == 2, "Comma-separated full-path patterns");
            Console.WriteLine("All " + count + " tests passed.");
        } finally { Directory.Delete(root, true); }
    }
}
