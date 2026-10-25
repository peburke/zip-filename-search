using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ZipFilenameSearch {
    public sealed class MatchResult {
        public string Archive { get; set; }
        public string InternalPath { get; set; }
        public int EntryIndex { get; set; }
    }
    public static class SearchEngine {
        public static string ExtractionPath(MatchResult match, string directory) {
            string normalized = match.InternalPath.Replace('\\', '/');
            string name = normalized.Substring(normalized.LastIndexOf('/') + 1);
            if (String.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.EndsWith(" "))
                throw new IOException("This entry does not have a valid Windows filename.");
            string path = Path.Combine(Path.GetFullPath(directory), name);
            if (String.Equals(path, Path.GetFullPath(match.Archive), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose another folder: extraction would replace the source ZIP.");
            return path;
        }
        public static string ExtractToDirectory(MatchResult match, string directory, bool overwrite) {
            string destination = ExtractionPath(match, directory);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The selected folder no longer exists.");
            if (!overwrite && File.Exists(destination)) throw new IOException("A file with this name already exists.");
            // Stage beside the destination so failed extraction leaves existing files intact.
            string staged = ExtractForOpening(match, directory);
            try {
                if (overwrite && File.Exists(destination)) File.Replace(staged, destination, null);
                else File.Move(staged, destination);
                return destination;
            } finally {
                if (File.Exists(staged)) File.Delete(staged);
                Directory.Delete(Path.GetDirectoryName(staged));
            }
        }
        public static bool IsReadError(Exception ex) {
            return ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException ||
                ex is NotSupportedException || ex is ArgumentException || ex is System.Security.SecurityException;
        }
        public static void SearchArchiveSafely(string archive, Regex pattern, Action<MatchResult> found, Func<bool> cancelled, Action<string> error, bool fullPath = false) {
            try { SearchArchive(archive, pattern, found, cancelled, fullPath); }
            catch (Exception ex) {
                if (!IsReadError(ex)) throw;
                error(archive + " [" + ex.GetType().Name + "]: " + ex.Message);
            }
        }
        public static string ExtractForOpening(MatchResult match, string temporaryRoot) {
            using (var stream = new FileStream(match.Archive, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                if (match.EntryIndex < 0 || match.EntryIndex >= zip.Entries.Count || zip.Entries[match.EntryIndex].FullName != match.InternalPath)
                    throw new IOException("The archive has changed. Search again before opening this file.");
                var entry = zip.Entries[match.EntryIndex];
                string normalized = entry.FullName.Replace('\\', '/');
                string name = normalized.Substring(normalized.LastIndexOf('/') + 1);
                if (String.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.EndsWith(" "))
                    throw new IOException("This entry does not have a valid Windows filename.");
                // Only the basename is used; archive paths can never escape the unique folder.
                string folder = Path.Combine(Path.GetFullPath(temporaryRoot), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string destination = Path.Combine(folder, name);
                try {
                    using (var input = entry.Open())
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
                    return destination;
                } catch { if (File.Exists(destination)) File.Delete(destination); Directory.Delete(folder); throw; }
            }
        }
        public static Regex Pattern(string pattern, bool useRegex = false) {
            if (useRegex) {
                if (String.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("Enter a regular expression.");
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            }
            var alternatives = new List<string>();
            foreach (string part in (pattern ?? "").Split(',')) {
                string term = part.Trim();
                if (term.Length > 0) alternatives.Add(Regex.Escape(term).Replace("\\*", ".*").Replace("\\?", "."));
            }
            if (alternatives.Count == 0) throw new ArgumentException("Enter at least one filename or wildcard pattern, separated by commas.");
            return new Regex("\\A(?:" + String.Join("|", alternatives) + ")\\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }
        public static IEnumerable<string> Archives(string source, bool recursive, Action<string> error, Func<bool> cancelled) {
            if (File.Exists(source)) { yield return Path.GetFullPath(source); yield break; }
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Select an existing ZIP file or folder.");
            var pending = new Stack<string>(); pending.Push(Path.GetFullPath(source));
            while (pending.Count > 0 && !cancelled()) {
                string folder = pending.Pop(); string[] files = new string[0], dirs = new string[0];
                try { files = Directory.GetFiles(folder); if (recursive) dirs = Directory.GetDirectories(folder); }
                catch (Exception ex) { if (!IsReadError(ex)) throw; error(folder + " [" + ex.GetType().Name + "]: " + ex.Message); }
                foreach (string file in files) { if (cancelled()) yield break; if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) yield return file; }
                foreach (string dir in dirs) {
                    if (cancelled()) yield break;
                    try { if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0) pending.Push(dir); }
                    catch (Exception ex) { if (!IsReadError(ex)) throw; error(dir + " [" + ex.GetType().Name + "]: " + ex.Message); }
                }
            }
        }
        public static void SearchArchive(string archive, Regex pattern, Action<MatchResult> found, Func<bool> cancelled, bool fullPath = false) {
            using (var stream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                for (int index = 0; index < zip.Entries.Count; index++) {
                    var entry = zip.Entries[index];
                    if (cancelled()) return;
                    string normalized = entry.FullName.Replace('\\', '/');
                    string name = normalized.Substring(normalized.LastIndexOf('/') + 1);
                    if (name.Length > 0 && pattern.IsMatch(fullPath ? normalized : name)) found(new MatchResult { Archive = Path.GetFullPath(archive), InternalPath = entry.FullName, EntryIndex = index });
                }
            }
        }
    }
}
