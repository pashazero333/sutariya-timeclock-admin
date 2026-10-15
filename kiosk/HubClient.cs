using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SutariyaTimeClock;

internal sealed class HubClient : IDisposable
{
    private const int RoundingMinutes = 5;
    private const string AppVersion = "1.1.2";

    private static readonly TimeZoneInfo OfficeTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _timeGate = new();
    private readonly string _machineId = Environment.MachineName;
    private readonly string _machineName = Environment.MachineName;
    private readonly string _dataDir;
    private readonly string _cachePath;
    private readonly string _queuePath;
    private readonly string _devicePath;
    private readonly string _trustedTimePath;

    private CacheEnvelope _cache;
    private List<QueuedEvent> _queue;
    private DeviceRegistration _device;
    private TrustedTimeState _trustedTimeState;
    private DateTimeOffset? _trustedUtcAnchor;
    private long _trustedAnchorTimestamp;

    public bool IsOnline { get; private set; }
    public int PendingCount => _queue.Count;
    public string? LastError { get; private set; }

    public HubClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(12)
        };

        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SutariyaTimeClock");
        Directory.CreateDirectory(_dataDir);
        _cachePath = Path.Combine(_dataDir, "employee_cache.json");
        _queuePath = Path.Combine(_dataDir, "queue.json");
        _devicePath = Path.Combine(_dataDir, "device.json");
        _trustedTimePath = Path.Combine(_dataDir, "trusted_time.json");

        _cache = LoadFile(_cachePath, new CacheEnvelope());
        _queue = LoadFile(_queuePath, new List<QueuedEvent>());
        _device = LoadFile(_devicePath, new DeviceRegistration());
        _trustedTimeState = LoadFile(_trustedTimePath, new TrustedTimeState());
        RestoreTrustedClock();
    }

    public DateTimeOffset GetTrustedUtcNow()
    {
        lock (_timeGate)
        {
            if (_trustedUtcAnchor.HasValue)
            {
                var elapsed = Stopwatch.GetElapsedTime(_trustedAnchorTimestamp);
                return _trustedUtcAnchor.Value.Add(elapsed);
            }

            if (_trustedTimeState.ServerUtc.HasValue && _trustedTimeState.ClientUtcAtSync.HasValue)
            {
                var offset = _trustedTimeState.ServerUtc.Value.ToUniversalTime()
                    - _trustedTimeState.ClientUtcAtSync.Value.ToUniversalTime();
                return DateTimeOffset.UtcNow.Add(offset);
            }

            // First-ever offline launch has no trusted cloud anchor yet.
            // As soon as the kiosk reaches Supabase, future time is cloud-corrected.
            return DateTimeOffset.UtcNow;
        }
    }

    public DateTimeOffset GetOfficeNow()
        => ToOfficeTime(GetTrustedUtcNow());

    public DateTimeOffset ToOfficeTime(DateTimeOffset value)
        => TimeZoneInfo.ConvertTime(value.ToUniversalTime(), OfficeTimeZone);

    public async Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        await TrySyncAndRefreshAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.Employees.Count == 0)
                throw new InvalidOperationException(LastError ?? "Connect this computer to the Internet once to load the TimeClock employees.");

            return _cache.Employees
                .OrderBy(e => e.Name)
                .Select(e => new Employee(e.Id, e.Name))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EmployeeStatus> GetStatusAsync(int employeeId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return BuildStatusCore(employeeId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PunchResult> PunchAsync(int employeeId, string last4, string action, string machineId, CancellationToken cancellationToken = default)
    {
        action = action.Trim().ToLowerInvariant();
        if (action is not ("in" or "out"))
            throw new InvalidOperationException("Action must be Clock In or Clock Out.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var employee = FindEmployee(employeeId)
                ?? throw new InvalidOperationException("This employee is not available on this TimeClock.");

            if (!VerifyLast4(employee, last4))
                throw new InvalidOperationException("The Last 4 SSN entered were not correct.");

            var current = BuildStatusCore(employeeId);
            if (action == "in" && current.ClockedIn)
                throw new InvalidOperationException("This employee is already clocked in.");
            if (action == "out" && !current.ClockedIn)
                throw new InvalidOperationException("This employee is not currently clocked in.");

            _queue.Add(new QueuedEvent
            {
                EventId = Guid.NewGuid().ToString(),
                EmployeeId = employeeId,
                Action = action,
                OccurredAt = GetTrustedUtcNow().ToUniversalTime(),
                MachineId = _machineId
            });
            SaveFileAtomic(_queuePath, _queue);
        }
        finally
        {
            _gate.Release();
        }

        await TrySyncAndRefreshAsync(cancellationToken);
        var status = await GetStatusAsync(employeeId, cancellationToken);
        return new PunchResult(status.PendingCount == 0
            ? "Punch saved."
            : "Punch saved offline. It will sync automatically when the Internet is available.");
    }

    public async Task<bool> HealthAsync(CancellationToken cancellationToken = default)
        => await TrySyncAndRefreshAsync(cancellationToken);

    private async Task<bool> TrySyncAndRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var token = await EnsureRegistrationAsync(cancellationToken);
            List<QueuedEvent> queueSnapshot;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                queueSnapshot = _queue
                    .OrderBy(e => e.OccurredAt)
                    .Select(e => e.Clone())
                    .ToList();
            }
            finally
            {
                _gate.Release();
            }

            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (queueSnapshot.Count > 0)
            {
                using var syncRequest = new HttpRequestMessage(HttpMethod.Post, "sync");
                syncRequest.Headers.Add("X-Kiosk-Token", token);
                syncRequest.Content = JsonContent.Create(new
                {
                    events = queueSnapshot.Select(e => new
                    {
                        event_id = e.EventId,
                        employee_id = e.EmployeeId,
                        action = e.Action,
                        occurred_at = e.OccurredAt.ToUniversalTime().ToString("O"),
                        machine_id = _machineId
                    }),
                    machine_name = _machineName,
                    app_version = AppVersion
                });

                using var syncResponse = await _http.SendAsync(syncRequest, cancellationToken);
                var syncText = await syncResponse.Content.ReadAsStringAsync(cancellationToken);
                EnsureSuccess(syncResponse, syncText);
                var sync = JsonSerializer.Deserialize<SyncEnvelope>(syncText, _json) ?? new SyncEnvelope();
                foreach (var id in sync.Accepted) accepted.Add(id);
            }

            using var configRequest = new HttpRequestMessage(HttpMethod.Get, "config");
            configRequest.Headers.Add("X-Kiosk-Token", token);
            using var configResponse = await _http.SendAsync(configRequest, cancellationToken);
            var configText = await configResponse.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(configResponse, configText);
            var fresh = JsonSerializer.Deserialize<CacheEnvelope>(configText, _json) ?? new CacheEnvelope();

            // generated_at is created by the Supabase Edge Function itself, so this
            // anchors the kiosk to cloud UTC rather than the employee PC's clock.
            if (fresh.GeneratedAt.HasValue)
                UpdateTrustedClock(fresh.GeneratedAt.Value);

            await _gate.WaitAsync(cancellationToken);
            try
            {
                _cache = fresh;
                if (accepted.Count > 0)
                    _queue = _queue.Where(e => !accepted.Contains(e.EventId)).ToList();
                SaveFileAtomic(_cachePath, _cache);
                SaveFileAtomic(_queuePath, _queue);
            }
            finally
            {
                _gate.Release();
            }

            await SendHeartbeatAsync(token, cancellationToken);
            IsOnline = true;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            IsOnline = false;
            LastError = FriendlyError(ex.Message);
            return false;
        }
    }

    private async Task<string> EnsureRegistrationAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_device.Token)) return _device.Token;

        using var response = await _http.PostAsJsonAsync("bootstrap", new
        {
            machine_id = _machineId,
            machine_name = _machineName,
            app_version = AppVersion
        }, _json, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, text);

        var bootstrap = JsonSerializer.Deserialize<BootstrapEnvelope>(text, _json);
        var token = bootstrap?.Token?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("TimeClock registration did not return a device token.");

        _device = new DeviceRegistration { Token = token };
        SaveFileAtomic(_devicePath, _device);
        return token;
    }

    private async Task SendHeartbeatAsync(string token, CancellationToken cancellationToken)
    {
        int pending;
        await _gate.WaitAsync(cancellationToken);
        try { pending = _queue.Count; }
        finally { _gate.Release(); }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "heartbeat");
            request.Headers.Add("X-Kiosk-Token", token);
            request.Content = JsonContent.Create(new
            {
                machine_name = _machineName,
                pending_count = pending,
                app_version = AppVersion
            });
            using var response = await _http.SendAsync(request, cancellationToken);
        }
        catch
        {
            // A failed heartbeat does not invalidate a successful punch/config sync.
        }
    }

    private EmployeeStatus BuildStatusCore(int employeeId)
    {
        var employee = FindEmployee(employeeId)
            ?? throw new InvalidOperationException("This employee is not available on this TimeClock.");

        var clockedIn = employee.ClockedIn;
        var pending = _queue
            .Where(e => e.EmployeeId == employeeId)
            .OrderBy(e => e.OccurredAt)
            .ToList();

        var rows = employee.History
            .Select(p => new WorkingPunch
            {
                ClockIn = p.ClockIn,
                ClockOut = p.ClockOut,
                RawHours = p.RawHours,
                RoundedHours = p.RoundedHours
            })
            .ToList();

        foreach (var e in pending)
        {
            if (e.Action == "in")
            {
                clockedIn = true;
                rows.Add(new WorkingPunch { ClockIn = e.OccurredAt });
            }
            else
            {
                clockedIn = false;
                var open = rows
                    .Where(p => p.ClockIn.HasValue && !p.ClockOut.HasValue)
                    .OrderByDescending(p => p.ClockIn)
                    .FirstOrDefault();
                if (open is not null)
                {
                    open.ClockOut = e.OccurredAt;
                    var raw = Math.Max(0, (e.OccurredAt - open.ClockIn!.Value).TotalHours);
                    open.RawHours = raw;
                    open.RoundedHours = RoundUpHours(raw);
                }
            }
        }

        var cutoff = GetTrustedUtcNow().AddDays(-14);
        var history = rows
            .Where(p => p.ClockIn.HasValue && p.ClockIn.Value >= cutoff)
            .OrderByDescending(p => p.ClockIn)
            .Select(p => new PunchHistory(p.ClockIn, p.ClockOut, p.RawHours, p.RoundedHours))
            .ToList();

        return new EmployeeStatus(clockedIn, pending.Count, history);
    }

    private void RestoreTrustedClock()
    {
        lock (_timeGate)
        {
            if (!_trustedTimeState.ServerUtc.HasValue || !_trustedTimeState.ClientUtcAtSync.HasValue)
                return;

            var offset = _trustedTimeState.ServerUtc.Value.ToUniversalTime()
                - _trustedTimeState.ClientUtcAtSync.Value.ToUniversalTime();
            _trustedUtcAnchor = DateTimeOffset.UtcNow.Add(offset);
            _trustedAnchorTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private void UpdateTrustedClock(DateTimeOffset serverTime)
    {
        lock (_timeGate)
        {
            var clientUtc = DateTimeOffset.UtcNow;
            var serverUtc = serverTime.ToUniversalTime();
            _trustedUtcAnchor = serverUtc;
            _trustedAnchorTimestamp = Stopwatch.GetTimestamp();
            _trustedTimeState = new TrustedTimeState
            {
                ServerUtc = serverUtc,
                ClientUtcAtSync = clientUtc
            };
            SaveFileAtomic(_trustedTimePath, _trustedTimeState);
        }
    }

    private CachedEmployee? FindEmployee(int employeeId)
        => _cache.Employees.FirstOrDefault(e => e.Id == employeeId);

    private static bool VerifyLast4(CachedEmployee employee, string last4)
    {
        last4 = last4.Trim();
        if (last4.Length != 4 || !last4.All(char.IsDigit)) return false;
        if (string.IsNullOrWhiteSpace(employee.VerificationSalt) || string.IsNullOrWhiteSpace(employee.VerificationHash)) return false;

        var bytes = Encoding.UTF8.GetBytes($"{employee.VerificationSalt}:{last4}");
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return string.Equals(actual, employee.VerificationHash.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    private static double RoundUpHours(double hours)
    {
        if (hours <= 0) return 0;
        var minutes = hours * 60.0;
        return Math.Ceiling(minutes / RoundingMinutes) * RoundingMinutes / 60.0;
    }

    private T LoadFile<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), _json);
            return value is null ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private void SaveFileAtomic<T>(string path, T value)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, _json), new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string text)
    {
        if (response.IsSuccessStatusCode) return;
        var message = string.IsNullOrWhiteSpace(text) ? $"TimeClock cloud returned {(int)response.StatusCode}." : text;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
                message = detail.GetString() ?? message;
        }
        catch { }
        throw new InvalidOperationException(message);
    }

    private static string FriendlyError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unable to reach the TimeClock cloud.";
        var clean = message.Trim();
        if (clean.StartsWith("{", StringComparison.Ordinal)) return "Unable to reach the TimeClock cloud.";
        return clean.Length > 250 ? clean[..250] : clean;
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    private sealed class DeviceRegistration
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }

    private sealed class BootstrapEnvelope
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }

    private sealed class CacheEnvelope
    {
        [JsonPropertyName("generated_at")]
        public DateTimeOffset? GeneratedAt { get; set; }

        [JsonPropertyName("employees")]
        public List<CachedEmployee> Employees { get; set; } = new();
    }

    private sealed class CachedEmployee
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("verification_salt")]
        public string VerificationSalt { get; set; } = string.Empty;

        [JsonPropertyName("verification_hash")]
        public string VerificationHash { get; set; } = string.Empty;

        [JsonPropertyName("clocked_in")]
        public bool ClockedIn { get; set; }

        [JsonPropertyName("history")]
        public List<CachedPunch> History { get; set; } = new();
    }

    private sealed class CachedPunch
    {
        [JsonPropertyName("clock_in")]
        public DateTimeOffset? ClockIn { get; set; }

        [JsonPropertyName("clock_out")]
        public DateTimeOffset? ClockOut { get; set; }

        [JsonPropertyName("raw_hours")]
        public double? RawHours { get; set; }

        [JsonPropertyName("rounded_hours")]
        public double? RoundedHours { get; set; }
    }

    private sealed class QueuedEvent
    {
        [JsonPropertyName("event_id")]
        public string EventId { get; set; } = string.Empty;

        [JsonPropertyName("employee_id")]
        public int EmployeeId { get; set; }

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("occurred_at")]
        public DateTimeOffset OccurredAt { get; set; }

        [JsonPropertyName("machine_id")]
        public string MachineId { get; set; } = string.Empty;

        public QueuedEvent Clone() => new()
        {
            EventId = EventId,
            EmployeeId = EmployeeId,
            Action = Action,
            OccurredAt = OccurredAt,
            MachineId = MachineId
        };
    }

    private sealed class SyncEnvelope
    {
        [JsonPropertyName("accepted")]
        public List<string> Accepted { get; set; } = new();
    }

    private sealed class TrustedTimeState
    {
        [JsonPropertyName("server_utc")]
        public DateTimeOffset? ServerUtc { get; set; }

        [JsonPropertyName("client_utc_at_sync")]
        public DateTimeOffset? ClientUtcAtSync { get; set; }
    }

    private sealed class WorkingPunch
    {
        public DateTimeOffset? ClockIn { get; set; }
        public DateTimeOffset? ClockOut { get; set; }
        public double? RawHours { get; set; }
        public double? RoundedHours { get; set; }
    }
}
