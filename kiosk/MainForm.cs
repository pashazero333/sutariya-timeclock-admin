using System.Collections.Concurrent;

namespace SutariyaTimeClock;

internal sealed class MainForm : Form
{
    private const string HubUrl = "http://PASHA:8421";

    private readonly HubClient _hub = new(HubUrl);
    private readonly string _machineId = $"{Environment.MachineName}\\{Environment.UserName}";
    private readonly Dictionary<int, Button> _employeeButtons = new();
    private readonly Dictionary<int, EmployeeStatus> _statuses = new();
    private readonly List<Employee> _employees = new();

    private readonly FlowLayoutPanel _employeeGrid;
    private Label _clockLabel = null!;
    private Label _hubLabel = null!;
    private readonly Label _selectedLabel;
    private readonly Label _messageLabel;
    private readonly Button _clockInButton;
    private readonly Button _clockOutButton;
    private readonly Panel _historyPanel;
    private readonly Label _historyTitle;
    private readonly DataGridView _historyGrid;
    private readonly System.Windows.Forms.Timer _clockTimer;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _historyTimer;

    private Employee? _selectedEmployee;
    private bool _refreshing;

    private static readonly Color Navy = Color.FromArgb(23, 63, 107);
    private static readonly Color Page = Color.FromArgb(244, 246, 248);
    private static readonly Color Green = Color.FromArgb(25, 135, 84);
    private static readonly Color Red = Color.FromArgb(201, 52, 52);
    private static readonly Color Muted = Color.FromArgb(102, 112, 133);
    private static readonly Color LightGreen = Color.FromArgb(233, 247, 239);
    private static readonly Color LightRed = Color.FromArgb(253, 236, 236);

    public MainForm()
    {
        Text = "Sutariya TimeClock";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 720);
        Size = new Size(1160, 820);
        BackColor = Page;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 24, 28, 24),
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Page
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        Controls.Add(outer);

        var header = BuildHeader();
        outer.Controls.Add(header, 0, 0);

        var instruction = new Label
        {
            Text = "Select your name, then choose Clock In or Clock Out.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.FromArgb(23, 32, 51),
            Padding = new Padding(4, 0, 0, 0)
        };
        outer.Controls.Add(instruction, 0, 1);

        _employeeGrid = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            Padding = new Padding(2, 4, 2, 8),
            Margin = new Padding(0),
            BackColor = Page
        };
        outer.Controls.Add(_employeeGrid, 0, 2);

        _selectedLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            Text = "SELECT AN EMPLOYEE",
            BackColor = Color.White,
            ForeColor = Muted,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 0, 8)
        };
        outer.Controls.Add(_selectedLabel, 0, 3);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 6, 0, 6)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _clockInButton = BuildActionButton("CLOCK IN", Green);
        _clockOutButton = BuildActionButton("CLOCK OUT", Red);
        _clockInButton.Enabled = false;
        _clockOutButton.Enabled = false;
        _clockInButton.Click += async (_, _) => await PunchAsync("in");
        _clockOutButton.Click += async (_, _) => await PunchAsync("out");
        actions.Controls.Add(_clockInButton, 0, 0);
        actions.Controls.Add(_clockOutButton, 1, 0);
        outer.Controls.Add(actions, 0, 4);

        _messageLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 10F),
            Text = "Connecting to the office TimeClock hub..."
        };
        outer.Controls.Add(_messageLabel, 0, 5);

        _historyTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 38,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0)
        };
        _historyGrid = BuildHistoryGrid();
        _historyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false,
            Margin = new Padding(0, 6, 0, 0)
        };
        _historyPanel.Controls.Add(_historyGrid);
        _historyPanel.Controls.Add(_historyTitle);
        outer.Controls.Add(_historyPanel, 0, 6);

        _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        _refreshTimer.Tick += async (_, _) => await RefreshAllAsync(false);
        _refreshTimer.Start();

        _historyTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        _historyTimer.Tick += (_, _) =>
        {
            _historyTimer.Stop();
            _historyPanel.Visible = false;
        };

        Shown += async (_, _) => await RefreshAllAsync(true);
        FormClosed += (_, _) => _hub.Dispose();
    }

    private Control BuildHeader()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Navy,
            Padding = new Padding(20, 10, 20, 10),
            Margin = new Padding(0, 0, 0, 10)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Navy
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));

        var title = new Label
        {
            Text = "Sutariya TimeClock",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 23F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Navy
        };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        _clockLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight
        };

        _hubLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(220, 229, 238),
            Font = new Font("Segoe UI", 9.5F),
            TextAlign = ContentAlignment.TopRight,
            Text = "Office Hub: connecting..."
        };

        right.Controls.Add(_clockLabel, 0, 0);
        right.Controls.Add(_hubLabel, 0, 1);
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(right, 1, 0);
        card.Controls.Add(layout);
        return card;
    }

    private static Button BuildActionButton(string text, Color color)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 4, 8, 4),
            BackColor = color,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 17F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(color, .08f);
        return button;
    }

    private static DataGridView BuildHistoryGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            Top = 38,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 34,
            RowTemplate = { Height = 32 }
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(93, 107, 128);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grid.DefaultCellStyle.Font = new Font("Segoe UI", 9.5F);
        grid.Columns.Add("date", "Day / Date");
        grid.Columns.Add("in", "Clock In");
        grid.Columns.Add("out", "Clock Out");
        grid.Columns.Add("raw", "Raw");
        grid.Columns.Add("rounded", "Rounded");
        return grid;
    }

    private void UpdateClock()
    {
        _clockLabel.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy   h:mm:ss tt");
    }

    private async Task RefreshAllAsync(bool showLoading)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (showLoading) _messageLabel.Text = "Loading employees...";
            var employees = await _hub.GetEmployeesAsync();
            _hubLabel.Text = "Office Hub: connected";
            _hubLabel.ForeColor = Color.FromArgb(213, 245, 225);

            var selectedId = _selectedEmployee?.Id;
            _employees.Clear();
            _employees.AddRange(employees.OrderBy(e => e.Name));
            RebuildEmployeeButtons(selectedId);

            var results = new ConcurrentDictionary<int, EmployeeStatus>();
            await Task.WhenAll(_employees.Select(async employee =>
            {
                try
                {
                    var state = await _hub.GetStatusAsync(employee.Id);
                    results[employee.Id] = state;
                }
                catch { }
            }));

            foreach (var pair in results) _statuses[pair.Key] = pair.Value;
            UpdateAllEmployeeButtons();
            RenderSelectedState();

            if (showLoading)
                _messageLabel.Text = _employees.Count == 0 ? "No active employees are available." : "Select your name to begin.";
        }
        catch (Exception ex)
        {
            _hubLabel.Text = "Office Hub: unavailable";
            _hubLabel.ForeColor = Color.FromArgb(255, 210, 210);
            _messageLabel.Text = "This computer cannot reach the office TimeClock hub.";
            _selectedLabel.Text = "OFFICE HUB UNAVAILABLE";
            _selectedLabel.BackColor = Color.White;
            _selectedLabel.ForeColor = Red;
            _clockInButton.Enabled = false;
            _clockOutButton.Enabled = false;
            if (showLoading && _employees.Count == 0)
                _messageLabel.Text = $"Office Hub unavailable. {CleanError(ex.Message)}";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RebuildEmployeeButtons(int? selectedId)
    {
        _employeeGrid.SuspendLayout();
        _employeeGrid.Controls.Clear();
        _employeeButtons.Clear();

        foreach (var employee in _employees)
        {
            var button = new Button
            {
                Width = 205,
                Height = 78,
                Margin = new Padding(7),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(23, 32, 51),
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Tag = employee
            };
            button.FlatAppearance.BorderSize = 3;
            button.Click += async (_, _) => await SelectEmployeeAsync(employee);
            _employeeButtons[employee.Id] = button;
            _employeeGrid.Controls.Add(button);
        }

        if (selectedId.HasValue)
            _selectedEmployee = _employees.FirstOrDefault(e => e.Id == selectedId.Value);
        else if (_selectedEmployee is not null)
            _selectedEmployee = _employees.FirstOrDefault(e => e.Id == _selectedEmployee.Id);

        _employeeGrid.ResumeLayout();
        UpdateAllEmployeeButtons();
    }

    private void UpdateAllEmployeeButtons()
    {
        foreach (var employee in _employees)
        {
            if (!_employeeButtons.TryGetValue(employee.Id, out var button)) continue;
            var selected = _selectedEmployee?.Id == employee.Id;
            if (_statuses.TryGetValue(employee.Id, out var state))
            {
                button.FlatAppearance.BorderColor = state.ClockedIn ? Green : Red;
                button.Text = state.ClockedIn ? $"{employee.Name}\nCLOCKED IN" : $"{employee.Name}\nCLOCKED OUT";
            }
            else
            {
                button.FlatAppearance.BorderColor = Color.FromArgb(184, 193, 204);
                button.Text = employee.Name;
            }
            button.BackColor = selected ? Color.FromArgb(232, 240, 249) : Color.White;
        }
    }

    private async Task SelectEmployeeAsync(Employee employee)
    {
        _historyTimer.Stop();
        _historyPanel.Visible = false;
        _selectedEmployee = employee;
        UpdateAllEmployeeButtons();
        _messageLabel.Text = $"Loading {employee.Name}...";

        try
        {
            var state = await _hub.GetStatusAsync(employee.Id);
            _statuses[employee.Id] = state;
            RenderSelectedState();
            UpdateAllEmployeeButtons();
            _messageLabel.Text = "Choose Clock In or Clock Out.";
        }
        catch (Exception ex)
        {
            _messageLabel.Text = CleanError(ex.Message);
            _clockInButton.Enabled = false;
            _clockOutButton.Enabled = false;
        }
    }

    private void RenderSelectedState()
    {
        if (_selectedEmployee is null)
        {
            _selectedLabel.Text = "SELECT AN EMPLOYEE";
            _selectedLabel.BackColor = Color.White;
            _selectedLabel.ForeColor = Muted;
            _clockInButton.Enabled = false;
            _clockOutButton.Enabled = false;
            return;
        }

        if (!_statuses.TryGetValue(_selectedEmployee.Id, out var state))
        {
            _selectedLabel.Text = _selectedEmployee.Name.ToUpperInvariant();
            _selectedLabel.BackColor = Color.White;
            _selectedLabel.ForeColor = Muted;
            _clockInButton.Enabled = false;
            _clockOutButton.Enabled = false;
            return;
        }

        if (state.ClockedIn)
        {
            _selectedLabel.Text = $"{_selectedEmployee.Name.ToUpperInvariant()}  —  CLOCKED IN";
            _selectedLabel.BackColor = LightGreen;
            _selectedLabel.ForeColor = Green;
            _clockInButton.Enabled = false;
            _clockOutButton.Enabled = true;
        }
        else
        {
            _selectedLabel.Text = $"{_selectedEmployee.Name.ToUpperInvariant()}  —  CLOCKED OUT";
            _selectedLabel.BackColor = LightRed;
            _selectedLabel.ForeColor = Red;
            _clockInButton.Enabled = true;
            _clockOutButton.Enabled = false;
        }

        if (state.PendingCount > 0)
            _messageLabel.Text = $"{state.PendingCount} punch event(s) waiting to sync to the cloud.";
    }

    private async Task PunchAsync(string action)
    {
        var employee = _selectedEmployee;
        if (employee is null) return;

        var actionText = action == "in" ? "Clock In" : "Clock Out";
        using var dialog = new Last4Dialog(employee.Name, actionText);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        SetPunchButtonsEnabled(false);
        _messageLabel.Text = "Saving punch...";

        try
        {
            var result = await _hub.PunchAsync(employee.Id, dialog.Last4, action, _machineId);
            var state = await _hub.GetStatusAsync(employee.Id);
            _statuses[employee.Id] = state;
            RenderSelectedState();
            UpdateAllEmployeeButtons();
            _messageLabel.Text = string.IsNullOrWhiteSpace(result.Message) ? "Punch saved." : result.Message;
            ShowHistory(employee, state.History);
            _ = RefreshAllAsync(false);
        }
        catch (Exception ex)
        {
            _messageLabel.Text = CleanError(ex.Message);
            MessageBox.Show(this, CleanError(ex.Message), "TimeClock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RenderSelectedState();
        }
    }

    private void SetPunchButtonsEnabled(bool enabled)
    {
        if (!enabled)
        {
            _clockInButton.Enabled = false;
            _clockOutButton.Enabled = false;
            return;
        }
        RenderSelectedState();
    }

    private void ShowHistory(Employee employee, IReadOnlyList<PunchHistory> history)
    {
        _historyGrid.Rows.Clear();
        var cutoff = DateTimeOffset.Now.AddDays(-14);
        var rows = history
            .Where(p => p.ClockIn.HasValue && p.ClockIn.Value >= cutoff)
            .OrderByDescending(p => p.ClockIn)
            .ToList();

        foreach (var p in rows)
        {
            var localIn = p.ClockIn?.LocalDateTime;
            var localOut = p.ClockOut?.LocalDateTime;
            _historyGrid.Rows.Add(
                localIn?.ToString("ddd M/d/yyyy") ?? "",
                localIn?.ToString("h:mm tt") ?? "",
                localOut?.ToString("h:mm tt") ?? "OPEN",
                FormatHours(p.RawHours),
                FormatHours(p.RoundedHours));
        }

        _historyTitle.Text = $"{employee.Name} — last 14 days" + (rows.Count == 0 ? " (no completed history yet)" : "");
        _historyPanel.Visible = true;
        _historyPanel.BringToFront();
        _historyTimer.Stop();
        _historyTimer.Start();
    }

    private static string FormatHours(double? value)
    {
        if (!value.HasValue) return string.Empty;
        return $"{value.Value:0.00} hrs ({Math.Round(value.Value * 60):0} min)";
    }

    private static string CleanError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unable to complete the request.";
        var clean = message.Trim();
        if (clean.StartsWith("{", StringComparison.Ordinal)) return "Unable to complete the request. Please try again.";
        return clean.Length > 250 ? clean[..250] : clean;
    }
}
