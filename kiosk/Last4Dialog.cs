namespace SutariyaTimeClock;

internal sealed class Last4Dialog : Form
{
    private readonly TextBox _input;
    public string Last4 => _input.Text.Trim();

    public Last4Dialog(string employeeName, string actionText)
    {
        Text = $"Verify {employeeName}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(430, 235);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var title = new Label
        {
            Text = employeeName,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 52
        };

        var prompt = new Label
        {
            Text = $"Enter the last 4 digits of your SSN to {actionText.ToLowerInvariant()}.",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 48
        };

        _input = new TextBox
        {
            Font = new Font("Segoe UI", 19F, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Center,
            MaxLength = 4,
            UseSystemPasswordChar = true,
            Width = 220,
            Height = 42
        };
        _input.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        };

        var inputHost = new Panel { Dock = DockStyle.Top, Height = 58 };
        inputHost.Controls.Add(_input);
        inputHost.Resize += (_, _) =>
        {
            _input.Left = (inputHost.ClientSize.Width - _input.Width) / 2;
            _input.Top = 5;
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 65,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 10, 18, 10)
        };

        var ok = new Button
        {
            Text = actionText,
            Width = 125,
            Height = 40,
            BackColor = Color.FromArgb(23, 63, 107),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            DialogResult = DialogResult.OK
        };
        ok.FlatAppearance.BorderSize = 0;

        var cancel = new Button
        {
            Text = "Cancel",
            Width = 100,
            Height = 40,
            BackColor = Color.FromArgb(104, 115, 134),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            DialogResult = DialogResult.Cancel
        };
        cancel.FlatAppearance.BorderSize = 0;

        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        Controls.Add(buttons);
        Controls.Add(inputHost);
        Controls.Add(prompt);
        Controls.Add(title);

        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => _input.Focus();

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (Last4.Length == 4 && Last4.All(char.IsDigit)) return;
            MessageBox.Show(this, "Enter exactly 4 digits.", "TimeClock", MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
        };
    }
}
