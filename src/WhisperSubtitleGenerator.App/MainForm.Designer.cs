namespace WhisperSubtitleGenerator.App;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    private ListBox _fileList = null!;
    private Button _addButton = null!;
    private Button _clearButton = null!;
    private Label _fileCountLabel = null!;
    private ComboBox _modelBox = null!;
    private ComboBox _languageBox = null!;
    private CheckBox _translateCheck = null!;
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
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        _fileList       = new ListBox();
        _addButton      = new Button();
        _clearButton    = new Button();
        _fileCountLabel = new Label();
        _modelBox       = new ComboBox();
        _languageBox    = new ComboBox();
        _translateCheck = new CheckBox();
        _srtCheck       = new CheckBox();
        _vttCheck       = new CheckBox();
        _outputBox      = new TextBox();
        _outputButton   = new Button();
        _startButton    = new Button();
        _cancelButton   = new Button();
        _progress       = new ProgressBar();
        _logBox         = new TextBox();

        SuspendLayout();

        // ---- files ----
        var filesLabel = new Label { Text = "Files", Left = 12, Top = 12, Width = 60, AutoSize = true };
        _fileList.Left = 12; _fileList.Top = 32; _fileList.Width = 460; _fileList.Height = 120;
        _fileList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _fileList.IntegralHeight = false;

        _addButton.Text = "Add files..."; _addButton.Left = 484; _addButton.Top = 32;
        _addButton.Width = 100; _addButton.Height = 28;
        _addButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _addButton.Click += OnAddFiles;

        _clearButton.Text = "Clear"; _clearButton.Left = 484; _clearButton.Top = 66;
        _clearButton.Width = 100; _clearButton.Height = 28;
        _clearButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _clearButton.Click += OnClearFiles;

        _fileCountLabel.Text = "0 files"; _fileCountLabel.Left = 484; _fileCountLabel.Top = 102;
        _fileCountLabel.Width = 100; _fileCountLabel.AutoSize = true;
        _fileCountLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        // ---- options ----
        var modelLabel = new Label { Text = "Model", Left = 12, Top = 168, AutoSize = true };
        _modelBox.Left = 70; _modelBox.Top = 164; _modelBox.Width = 402;
        _modelBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modelBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var langLabel = new Label { Text = "Language", Left = 12, Top = 200, AutoSize = true };
        _languageBox.Left = 70; _languageBox.Top = 196; _languageBox.Width = 160;
        _languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageBox.Items.AddRange(new object[]
        {
            "auto (detect)", "en English", "hi Hindi", "kn Kannada", "ta Tamil", "te Telugu",
            "mr Marathi", "bn Bengali", "ar Arabic", "es Spanish", "fr French", "de German",
            "it Italian", "pt Portuguese", "ru Russian", "ja Japanese", "ko Korean", "zh Chinese"
        });

        _translateCheck.Text = "Translate to English";
        _translateCheck.Left = 246; _translateCheck.Top = 198; _translateCheck.AutoSize = true;

        var formatLabel = new Label { Text = "Output", Left = 12, Top = 232, AutoSize = true };
        _srtCheck.Text = ".srt"; _srtCheck.Left = 70; _srtCheck.Top = 230; _srtCheck.AutoSize = true;
        _srtCheck.Checked = true;
        _vttCheck.Text = ".vtt"; _vttCheck.Left = 130; _vttCheck.Top = 230; _vttCheck.AutoSize = true;

        var folderLabel = new Label { Text = "Folder", Left = 12, Top = 264, AutoSize = true };
        _outputBox.Left = 70; _outputBox.Top = 260; _outputBox.Width = 402;
        _outputBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _outputBox.PlaceholderText = "(leave empty to write beside each source file)";

        _outputButton.Text = "Browse..."; _outputButton.Left = 484; _outputButton.Top = 258;
        _outputButton.Width = 100; _outputButton.Height = 26;
        _outputButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _outputButton.Click += OnChooseOutput;

        // ---- run ----
        _startButton.Text = "Generate subtitles"; _startButton.Left = 12; _startButton.Top = 300;
        _startButton.Width = 160; _startButton.Height = 32;
        _startButton.Click += OnStart;

        _cancelButton.Text = "Cancel"; _cancelButton.Left = 180; _cancelButton.Top = 300;
        _cancelButton.Width = 90; _cancelButton.Height = 32; _cancelButton.Enabled = false;
        _cancelButton.Click += OnCancel;

        _progress.Left = 280; _progress.Top = 306; _progress.Width = 304; _progress.Height = 20;
        _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _logBox.Left = 12; _logBox.Top = 344; _logBox.Width = 572; _logBox.Height = 200;
        _logBox.Multiline = true; _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Font = new Font("Consolas", 8.5f);
        _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        // ---- form ----
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(596, 560);
        MinimumSize = new Size(500, 450);
        Controls.AddRange(new Control[]
        {
            filesLabel, _fileList, _addButton, _clearButton, _fileCountLabel,
            modelLabel, _modelBox, langLabel, _languageBox, _translateCheck,
            formatLabel, _srtCheck, _vttCheck,
            folderLabel, _outputBox, _outputButton,
            _startButton, _cancelButton, _progress, _logBox
        });
        Text = "Whisper Subtitle Generator";
        ResumeLayout(false);
        PerformLayout();
    }
}
