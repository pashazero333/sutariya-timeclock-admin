using System.Net.Http.Json;
using System.Text.Json;

namespace SutariyaTimeClock;

internal sealed class HubClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HubClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(8)
        };
    }

    public async Task<IReadOnlyList<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("employees", cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, text);

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (!root.TryGetProperty("employees", out var employees) || employees.ValueKind != JsonValueKind.Array)
            return Array.Empty<Employee>();

        var result = new List<Employee>();
        foreach (var item in employees.EnumerateArray())
        {
            if (!TryGetInt(item, "id", out var id)) continue;
            var name = GetString(item, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            result.Add(new Employee(id, name.Trim()));
        }
        return result;
    }

    public async Task<EmployeeStatus> GetStatusAsync(int employeeId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"status?employee_id={employeeId}", cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, text);

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var clockedIn = GetBool(root, "clocked_in");
        var pending = TryGetInt(root, "pending_count", out var p) ? p : 0;
        var history = new List<PunchHistory>();

        if (root.TryGetProperty("history", out var h) && h.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in h.EnumerateArray())
            {
                history.Add(new PunchHistory(
                    GetDate(item, "clock_in"),
                    GetDate(item, "clock_out"),
                    GetDouble(item, "raw_hours"),
                    GetDouble(item, "rounded_hours")));
            }
        }

        return new EmployeeStatus(clockedIn, pending, history);
    }

    public async Task<PunchResult> PunchAsync(int employeeId, string last4, string action, string machineId, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            employee_id = employeeId,
            last4,
            action,
            machine_id = machineId
        };

        using var response = await _http.PostAsJsonAsync("punch", payload, _json, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, text);

        if (string.IsNullOrWhiteSpace(text)) return new PunchResult("Punch saved.");
        try
        {
            using var doc = JsonDocument.Parse(text);
            var message = GetString(doc.RootElement, "message");
            return new PunchResult(string.IsNullOrWhiteSpace(message) ? "Punch saved." : message);
        }
        catch
        {
            return new PunchResult("Punch saved.");
        }
    }

    public async Task<bool> HealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string text)
    {
        if (response.IsSuccessStatusCode) return;
        var message = string.IsNullOrWhiteSpace(text) ? $"Office Hub returned {(int)response.StatusCode}." : text;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var detail = GetString(doc.RootElement, "detail");
            if (!string.IsNullOrWhiteSpace(detail)) message = detail;
        }
        catch { }
        throw new InvalidOperationException(message);
    }

    private static string GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var item)) return false;
        if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out value)) return true;
        return int.TryParse(item.ToString(), out value);
    }

    private static bool GetBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var item)) return false;
        if (item.ValueKind == JsonValueKind.True) return true;
        if (item.ValueKind == JsonValueKind.False) return false;
        return bool.TryParse(item.ToString(), out var value) && value;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var value = GetString(element, name);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var item) || item.ValueKind == JsonValueKind.Null) return null;
        if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var value)) return value;
        return double.TryParse(item.ToString(), out var parsed) ? parsed : null;
    }

    public void Dispose() => _http.Dispose();
}
