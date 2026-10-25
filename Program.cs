using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZipFilenameSearch {
    static class Program {
        [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new SearchForm()); }
    }
    sealed class SearchForm : Form {
        readonly TextBox source = new TextBox { Dock = DockStyle.Fill };
        readonly TextBox pattern = new TextBox { Text = "*.pdf", Dock = DockStyle.Fill };
        readonly CheckBox recursive = new CheckBox { Text = "Include subfolders", AutoSize = true, Checked = true };
        readonly Button zip = new Button { Text = "Choose ZIP…", AutoSize = true };
        readonly Button folder = new Button { Text = "Choose folder…", AutoSize = true };
        readonly Button search = new Button { Text = "Search", AutoSize = true };
        readonly Button cancel = new Button { Text = "Cancel", Enabled = false, AutoSize = true };
        readonly Button copy = new Button { Text = "Copy selected rows", AutoSize = true };
        readonly Label status = new Label { Text = "Choose a ZIP file or a folder to begin.", Dock = DockStyle.Fill, AutoSize = true };
        readonly TextBox errors = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText };
        readonly BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
        bool closing;
        readonly CheckBox regexMode = new CheckBox { Text = "Use regular expressions", AutoSize = true };
        readonly CheckBox fullPathMode = new CheckBox { Text = "Search full internal path", AutoSize = true };
        bool opening;
        sealed class SearchUpdate { public List<MatchResult> Matches; public string Error; public string Status; }
        public SearchForm() {
            Text = "ZIP Filename Search"; Size = new Size(1050, 720); MinimumSize = new Size(700, 500); StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            var picker = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 }; picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); picker.Controls.Add(source); picker.Controls.Add(zip); picker.Controls.Add(folder);
            layout.Controls.Add(new Label { Text = "ZIP file or folder", AutoSize = true }); layout.Controls.Add(picker);
            var options = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5 }; options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360)); options.Controls.Add(pattern); options.Controls.Add(recursive); options.Controls.Add(search); options.Controls.Add(cancel); options.Controls.Add(copy); layout.Controls.Add(options);
            var helpPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var patternHelp = new Label { AutoSize = true, Padding = new Padding(0, 4, 0, 8) };
            Action updateHelp = () => { patternHelp.Text = regexMode.Checked
                ? @"Regex: report.*\.pdf$ or ^photo_[0-9]{2,4}\.jpg$. Use | for alternatives; commas are literal. Matches part of a filename, ignoring case."
                : "Filenames / patterns, separated by commas: *.pdf, report*.docx, photo.jpg. Matches any pattern; * = any characters, ? = one character."; };
            regexMode.CheckedChanged += delegate { updateHelp(); }; updateHelp();
            helpPanel.Controls.Add(regexMode); helpPanel.Controls.Add(fullPathMode); helpPanel.Controls.Add(patternHelp); layout.Controls.Add(helpPanel);
            var pathHelp = new Label { AutoSize = true };
            Action updatePathHelp = () => { pathHelp.Text = fullPathMode.Checked ? "Searching folders + filename inside each ZIP. Use / for folders; e.g. *example* (wildcard) or example (regex)." : "Searching filenames only. Check Search full internal path to match folder names too."; };
            fullPathMode.CheckedChanged += delegate { updatePathHelp(); }; updatePathHelp(); helpPanel.Controls.Add(pathHelp);
            grid.Columns.Add("Archive", "Archive"); grid.Columns.Add(new DataGridViewLinkColumn { Name = "InternalPath", HeaderText = "Internal path (click to open)", TrackVisitedState = false }); layout.Controls.Add(grid); layout.Controls.Add(status); layout.Controls.Add(errors); Controls.Add(layout);
            var resultMenu = new ContextMenuStrip();
            var extractItem = resultMenu.Items.Add("Extract to folder…");
            MatchResult contextMatch = null;
            grid.CellMouseDown += delegate(object s, DataGridViewCellMouseEventArgs e) {
                if (e.Button != MouseButtons.Right) return;
                contextMatch = null;
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                grid.ClearSelection(); grid.Rows[e.RowIndex].Selected = true;
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                contextMatch = grid.Rows[e.RowIndex].Tag as MatchResult;
            };
            grid.ContextMenuStrip = resultMenu;
            resultMenu.Opening += delegate(object s, CancelEventArgs e) { e.Cancel = contextMatch == null || opening; };
            extractItem.Click += async delegate {
                var match = contextMatch;
                if (match == null || opening) return;
                using (var dialog = new FolderBrowserDialog { Description = "Choose where to extract this file", ShowNewFolderButton = true }) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    opening = true; UseWaitCursor = true;
                    try {
                        string destination = SearchEngine.ExtractionPath(match, dialog.SelectedPath);
                        bool overwrite = File.Exists(destination);
                        if (overwrite && MessageBox.Show(this, "Replace the existing file?\n\n" + destination, "Confirm replacement", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                        string directory = dialog.SelectedPath;
                        string saved = await Task.Run(() => SearchEngine.ExtractToDirectory(match, directory, overwrite));
                        if (!IsDisposed && !closing) MessageBox.Show(this, "File extracted to:\n\n" + saved, "Extraction complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    } catch (Exception ex) {
                        if (!IsDisposed && !closing) MessageBox.Show(this, "Could not extract this file.\n\n" + ex.Message, "Extract file", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    } finally { opening = false; if (!IsDisposed) UseWaitCursor = false; }
                }
            };
            grid.CellContentClick += async delegate(object s, DataGridViewCellEventArgs e) {
                if (e.RowIndex < 0 || e.ColumnIndex != 1 || opening) return;
                var match = grid.Rows[e.RowIndex].Tag as MatchResult;
                if (match == null) return;
                opening = true; UseWaitCursor = true;
                try {
                    string path = await Task.Run(() => SearchEngine.ExtractForOpening(match, Path.Combine(Path.GetTempPath(), "ZipFilenameSearch")));
                    if (!IsDisposed && !closing) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" });
                } catch (Exception ex) {
                    if (!IsDisposed && !closing) MessageBox.Show(this, "Could not open this file. Check that its default app is installed and the ZIP is accessible.\n\n" + ex.Message, "Open file", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } finally { opening = false; if (!IsDisposed) UseWaitCursor = false; }
            };
            zip.Click += delegate { using (var dialog = new OpenFileDialog { Filter = "ZIP archives (*.zip)|*.zip|All files (*.*)|*.*", Title = "Select ZIP archive" }) if (dialog.ShowDialog(this) == DialogResult.OK) source.Text = dialog.FileName; };
            folder.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "Select a folder containing ZIP archives" }) if (dialog.ShowDialog(this) == DialogResult.OK) source.Text = dialog.SelectedPath; };
            search.Click += StartSearch; cancel.Click += delegate { worker.CancelAsync(); status.Text = "Cancelling…"; };
            copy.Click += delegate { var data = grid.GetClipboardContent(); if (data != null) { try { Clipboard.SetDataObject(data); } catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "Clipboard is busy. Please try again."); } } };
            worker.DoWork += RunSearch;
            worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs e) { var u = (SearchUpdate)e.UserState; if (u.Matches != null) foreach (var m in u.Matches) { int row = grid.Rows.Add(m.Archive, m.InternalPath); grid.Rows[row].Tag = m; } if (u.Error != null) errors.AppendText(u.Error + Environment.NewLine); if (u.Status != null) status.Text = u.Status; };
            worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e) { SetBusy(false); status.Text = e.Error != null ? "Search failed: " + e.Error.Message : (string)e.Result; if (closing) Close(); };
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (worker.IsBusy) { closing = true; worker.CancelAsync(); e.Cancel = true; status.Text = "Cancelling before closing…"; } };
            AcceptButton = search;
        }
        void SetBusy(bool busy) { source.Enabled = pattern.Enabled = regexMode.Enabled = fullPathMode.Enabled = recursive.Enabled = zip.Enabled = folder.Enabled = search.Enabled = !busy; cancel.Enabled = busy; }
        void StartSearch(object sender, EventArgs e) {
            try { SearchEngine.Pattern(pattern.Text, regexMode.Checked); if (!File.Exists(source.Text) && !Directory.Exists(source.Text)) throw new ArgumentException("Choose an existing ZIP file or folder."); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Check search", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            grid.Rows.Clear(); errors.Clear(); SetBusy(true); status.Text = "Searching…"; worker.RunWorkerAsync(new object[] { source.Text, pattern.Text, recursive.Checked, regexMode.Checked, fullPathMode.Checked });
        }
        void RunSearch(object sender, DoWorkEventArgs e) {
            var args = (object[])e.Argument; var regex = SearchEngine.Pattern((string)args[1], (bool)args[3]); int archives = 0, matches = 0, failures = 0; bool limit = false;
            var batch = new List<MatchResult>();
            Func<bool> stopped = () => worker.CancellationPending || limit;
            Action<string> error = message => { failures++; worker.ReportProgress(0, new SearchUpdate { Error = message }); };
            foreach (string archive in SearchEngine.Archives((string)args[0], (bool)args[2], error, stopped)) {
                if (stopped()) break;
                worker.ReportProgress(0, new SearchUpdate { Status = "Searching " + archive }); archives++;
                SearchEngine.SearchArchiveSafely(archive, regex, match => { batch.Add(match); matches++; if (batch.Count >= 200) { worker.ReportProgress(0, new SearchUpdate { Matches = batch }); batch = new List<MatchResult>(); } if (matches >= 100000) limit = true; }, stopped, error, (bool)args[4]);
            }
            if (batch.Count > 0) worker.ReportProgress(0, new SearchUpdate { Matches = batch });
            e.Result = (limit ? "Result limit reached; narrow your pattern. " : worker.CancellationPending ? "Cancelled. " : "Complete. ") + matches + " matches; " + archives + " archives checked; " + failures + " errors.";
        }
    }
}
