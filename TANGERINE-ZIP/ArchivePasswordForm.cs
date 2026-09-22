using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP;

// Stage head: PWDFM
internal sealed class ArchivePasswordForm : Form
{
    private readonly bool _creating;
    private readonly CheckBox _enablePassword = new() { AutoSize = true };
    private readonly Label _description = new() { AutoSize = false, Width = 430, Height = 46 };
    private readonly Label _passwordLabel = new() { AutoSize = true };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Width = 310 };
    private readonly Label _confirmationLabel = new() { AutoSize = true };
    private readonly TextBox _confirmation = new() { UseSystemPasswordChar = true, Width = 310 };
    private readonly CheckBox _showPassword = new() { AutoSize = true };
    private readonly Button _confirm = new() { DialogResult = DialogResult.None, Width = 105, Height = 32 };
    private readonly Button _cancel = new() { DialogResult = DialogResult.Cancel, Width = 105, Height = 32 };

    public ArchivePasswordForm(string archiveName, bool creating, string? errorMessage = null)
    {
        _creating = creating;
        Text = LanguageManager.Get(creating ? "CreatePasswordTitle" : "EnterPasswordTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(480, creating ? 294 : 242);

        _description.Text = string.Format(LanguageManager.Get(creating ? "CreatePasswordDescription" : "EnterPasswordDescription"), archiveName);
        _enablePassword.Text = LanguageManager.Get("EnableArchivePassword");
        _enablePassword.Checked = true;
        _passwordLabel.Text = LanguageManager.Get("PasswordLabel");
        _confirmationLabel.Text = LanguageManager.Get("ConfirmPasswordLabel");
        _showPassword.Text = LanguageManager.Get("ShowPassword");
        _confirm.Text = LanguageManager.Get("Confirm");
        _cancel.Text = LanguageManager.Get("Cancel");

        FlowLayoutPanel fields = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(14), AutoScroll = false
        };
        fields.Controls.Add(_description);
        if (creating) fields.Controls.Add(_enablePassword);
        fields.Controls.Add(_passwordLabel);
        fields.Controls.Add(_password);
        if (creating)
        {
            fields.Controls.Add(_confirmationLabel);
            fields.Controls.Add(_confirmation);
        }
        fields.Controls.Add(_showPassword);
        if (!string.IsNullOrWhiteSpace(errorMessage))
            fields.Controls.Add(new Label { Text = errorMessage, ForeColor = Color.OrangeRed, AutoSize = true, MaximumSize = new Size(430, 42) });

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_confirm);
        Controls.Add(fields);
        Controls.Add(buttons);
        AcceptButton = _confirm;
        CancelButton = _cancel;

        _enablePassword.CheckedChanged += (_, _) => UpdateEnabledState();
        _showPassword.CheckedChanged += (_, _) =>
        {
            _password.UseSystemPasswordChar = !_showPassword.Checked;
            _confirmation.UseSystemPasswordChar = !_showPassword.Checked;
        };
        _confirm.Click += Confirm_Click;
        Shown += (_, _) => _password.Focus();
        UpdateEnabledState();
        DarkTheme.Apply(this);
    }

    public string? PasswordValue { get; private set; }

    private void UpdateEnabledState()
    {
        bool enabled = !_creating || _enablePassword.Checked;
        _passwordLabel.Enabled = enabled;
        _password.Enabled = enabled;
        _confirmationLabel.Enabled = enabled;
        _confirmation.Enabled = enabled;
        _showPassword.Enabled = enabled;
    }

    private void Confirm_Click(object? sender, EventArgs e)
    {
        if (_creating && !_enablePassword.Checked)
        {
            PasswordValue = null;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        if (string.IsNullOrEmpty(_password.Text))
        {
            MessageBox.Show(this, MessageTipGenerator.GenerateTip("PWDFM0001", LanguageManager.Get("ArchivePasswordRequired")),
                LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning); //PWDFM0001
            return;
        }
        if (_creating && !string.Equals(_password.Text, _confirmation.Text, StringComparison.Ordinal))
        {
            MessageBox.Show(this, MessageTipGenerator.GenerateTip("PWDFM0002", LanguageManager.Get("PasswordMismatch")),
                LanguageManager.Get("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning); //PWDFM0002
            return;
        }
        PasswordValue = _password.Text;
        DialogResult = DialogResult.OK;
        Close();
    }

    public static bool TryGetCreationPassword(IWin32Window? owner, string archiveName, out string? password)
    {
        using ArchivePasswordForm form = new(archiveName, true);
        bool accepted = owner is null ? form.ShowDialog() == DialogResult.OK : form.ShowDialog(owner) == DialogResult.OK;
        password = accepted ? form.PasswordValue : null;
        return accepted;
    }

    public static bool TryGetExtractionPassword(IWin32Window? owner, string archiveName, string? errorMessage, out string? password)
    {
        using ArchivePasswordForm form = new(archiveName, false, errorMessage);
        bool accepted = owner is null ? form.ShowDialog() == DialogResult.OK : form.ShowDialog(owner) == DialogResult.OK;
        password = accepted ? form.PasswordValue : null;
        return accepted;
    }
}
