namespace WhisperSubtitleGenerator.App;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    private ListView _fileList = null!;
    private Button _addButton = null!;
    private Button _addFolderButton = null!;
    private Button _removeButton = null!;
    private Button _clearButton = null!;
    private Label _fileCountLabel = null!;
    private ComboBox _modelBox = null!;
    private ComboBox _languageBox = null!;
    private CheckBox _translateCheck = null!;
    private CheckBox _skipExistingCheck = null!;
    private CheckBox _burnCheck = null!;
    private ComboBox _burnModeBox = null!;
    private CheckBox _srtCheck = null!;
    private CheckBox _vttCheck = null!;
    private TextBox _outputBox = null!;
    private Button _outputButton = null!;
    private Button _startButton = null!;
    private Button _cancelButton = null!;
    private ProgressBar _progress = null!;
    private TextBox _logBox = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        _fileList          = new ListView();
        _addButton         = new Button();
        _addFolderButton   = new Button();
        _removeButton      = new Button();
        _clearButton       = new Button();
        _fileCountLabel    = new Label();
        _modelBox          = new ComboBox();
        _languageBox       = new ComboBox();
        _translateCheck    = new CheckBox();
        _skipExistingCheck = new CheckBox();
        _burnCheck         = new CheckBox();
        _burnModeBox       = new ComboBox();
        _srtCheck          = new CheckBox();
        _vttCheck          = new CheckBox();
        _outputBox         = new TextBox();
        _outputButton      = new Button();
        _startButton       = new Button();
        _cancelButton      = new Button();
        _progress          = new ProgressBar();
        _logBox            = new TextBox();

        SuspendLayout();

        // ---- file queue ----
        var filesLabel = new Label
        {
            Text = "Files  (drag and drop files or folders here)",
            Left = 12, Top = 12, AutoSize = true
        };

        _fileList.Left = 12; _fileList.Top = 32; _fileList.Width = 520; _fileList.Height = 150;
        _fileList.View = View.Details;
        _fileList.FullRowSelect = true;
        _fileList.GridLines = false;
        _fileList.HideSelection = false;
        _fileList.AllowDrop = true;
        _fileList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _fileList.Columns.Add("File", 250);
        _fileList.Columns.Add("Status", 80);
        _fileList.Columns.Add("Detail", 170);
        _fileList.DragEnter += OnDragEnter;
        _fileList.DragDrop  += OnDragDrop;
        _fileList.SelectedIndexChanged += OnSelectionChanged;

        _addButton.Text = "Add files..."; _addButton.Left = 544; _addButton.Top = 32;
        _addButton.Width = 110; _addButton.Height = 27;
        _addButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _addButton.Click += OnAddFiles;

        _addFolderButton.Text = "Add folder..."; _addFolderButton.Left = 544; _addFolderButton.Top = 63;
        _addFolderButton.Width = 110; _addFolderButton.Height = 27;
        _addFolderButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _addFolderButton.Click += OnAddFolder;

        _removeButton.Text = "Remove"; _removeButton.Left = 544; _removeButton.Top = 100;
        _removeButton.Width = 110; _removeButton.Height = 27;
        _removeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _removeButton.Click += OnRemoveSelected;

        _clearButton.Text = "Clear all"; _clearButton.Left = 544; _clearButton.Top = 131;
        _clearButton.Width = 110; _clearButton.Height = 27;
        _clearButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _clearButton.Click += OnClearFiles;

        _fileCountLabel.Text = "0 files queued";
        _fileCountLabel.Left = 544; _fileCountLabel.Top = 164; _fileCountLabel.AutoSize = true;
        _fileCountLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        // ---- options ----
        var modelLabel = new Label { Text = "Model", Left = 12, Top = 198, AutoSize = true };
        _modelBox.Left = 84; _modelBox.Top = 194; _modelBox.Width = 448;
        _modelBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modelBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var langLabel = new Label { Text = "Language", Left = 12, Top = 230, AutoSize = true };
        _languageBox.Left = 84; _languageBox.Top = 226; _languageBox.Width = 200;
        // DropDown (not DropDownList) so AutoComplete works: with 100 entries, typing "kan" to
        // reach Kannada is far better than scrolling.
        _languageBox.DropDownStyle = ComboBoxStyle.DropDown;
        _languageBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _languageBox.AutoCompleteSource = AutoCompleteSource.ListItems;

        _translateCheck.Text = "Translate to English";
        _translateCheck.Left = 300; _translateCheck.Top = 228; _translateCheck.AutoSize = true;

        _skipExistingCheck.Text = "Skip files that already have subtitles";
        _skipExistingCheck.Left = 84; _skipExistingCheck.Top = 256; _skipExistingCheck.AutoSize = true;

        _burnCheck.Text = "Write subtitles into the video";
        _burnCheck.Left = 84; _burnCheck.Top = 282; _burnCheck.AutoSize = true;
        _burnCheck.CheckedChanged += OnBurnToggled;

        _burnModeBox.Left = 300; _burnModeBox.Top = 278; _burnModeBox.Width = 232;
        _burnModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _burnModeBox.Enabled = false;
        _burnModeBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _burnModeBox.Items.AddRange(new object[]
        {
            "Burn in - permanent, re-encodes (slow)",
            "Add as a track - instant, switchable"
        });
        _burnModeBox.SelectedIndex = 0;

        var formatLabel = new Label { Text = "Output", Left = 12, Top = 312, AutoSize = true };
        _srtCheck.Text = ".srt"; _srtCheck.Left = 84; _srtCheck.Top = 310; _srtCheck.AutoSize = true;
        _srtCheck.Checked = true;
        _vttCheck.Text = ".vtt"; _vttCheck.Left = 144; _vttCheck.Top = 310; _vttCheck.AutoSize = true;

        var folderLabel = new Label { Text = "Folder", Left = 12, Top = 344, AutoSize = true };
        _outputBox.Left = 84; _outputBox.Top = 340; _outputBox.Width = 448;
        _outputBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _outputBox.PlaceholderText = "(leave empty to write beside each source file)";

        _outputButton.Text = "Browse..."; _outputButton.Left = 544; _outputButton.Top = 338;
        _outputButton.Width = 110; _outputButton.Height = 26;
        _outputButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _outputButton.Click += OnChooseOutput;

        // ---- run ----
        _startButton.Text = "Generate subtitles"; _startButton.Left = 12; _startButton.Top = 378;
        _startButton.Width = 160; _startButton.Height = 32;
        _startButton.Click += OnStart;

        _cancelButton.Text = "Cancel"; _cancelButton.Left = 180; _cancelButton.Top = 378;
        _cancelButton.Width = 90; _cancelButton.Height = 32; _cancelButton.Enabled = false;
        _cancelButton.Click += OnCancel;

        _progress.Left = 280; _progress.Top = 384; _progress.Width = 374; _progress.Height = 20;
        _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _logBox.Left = 12; _logBox.Top = 422; _logBox.Width = 642; _logBox.Height = 190;
        _logBox.Multiline = true; _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Font = new Font("Consolas", 8.5f);
        _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        // ---- form ----
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(666, 626);
        MinimumSize = new Size(560, 546);
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop  += OnDragDrop;
        Controls.AddRange(new Control[]
        {
            filesLabel, _fileList, _addButton, _addFolderButton, _removeButton, _clearButton, _fileCountLabel,
            modelLabel, _modelBox, langLabel, _languageBox, _translateCheck, _skipExistingCheck,
            _burnCheck, _burnModeBox, formatLabel, _srtCheck, _vttCheck,
            folderLabel, _outputBox, _outputButton,
            _startButton, _cancelButton, _progress, _logBox
        });
        Text = "Whisper Subtitle Generator";
        ResumeLayout(false);
        PerformLayout();
    }
}
