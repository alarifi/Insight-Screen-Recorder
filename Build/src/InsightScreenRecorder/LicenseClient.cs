using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace InsightScreenRecorder;

internal sealed class LicenseClient
{
    internal const string ProductId = "insight-screen-recorder";
    private static readonly Uri BaseUri = new("https://software.insight-ye.com/api/license/");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly string _statePath = Path.Combine(Path.GetDirectoryName(RecorderSettings.DefaultPath)!, "license.dat");
    private readonly string _fingerprint = GetFingerprint();
    private string? _serialKey;
    private DateTimeOffset? _expiresAt;
    internal bool IsPro { get; private set; }
    internal string Status { get; private set; } = "Free edition";
    internal DateTimeOffset? ExpiresAt => _expiresAt;

    internal async Task<string> VerifySavedAsync()
    {
        Load();
        if (string.IsNullOrWhiteSpace(_serialKey)) { IsPro = false; Status = "Free edition · no license activated"; return Status; }
        return await VerifyAsync();
    }

    internal async Task<string> ActivateAsync(string serialKey)
    {
        serialKey = serialKey.Trim();
        if (serialKey.Length is < 8 or > 100) throw new InvalidOperationException("Enter a valid license key.");
        using var response = await PostAsync("activate", new { serialKey, product = ProductId, hardwareFingerprint = _fingerprint, appVersion = Application.ProductVersion, osInfo = Environment.OSVersion.VersionString });
        var result = await ReadAsync<ActivationResponse>(response);
        if (!response.IsSuccessStatusCode || result?.Success != true)
            throw new InvalidOperationException(result?.Message ?? "The license server rejected this key.");
        if (!string.Equals(ReadTokenProduct(result.Token), ProductId, StringComparison.Ordinal)
            || !string.Equals(ReadTokenKey(result.Token), serialKey, StringComparison.OrdinalIgnoreCase))
        {
            try { using var undo = await PostAsync("unbind-device", new { serialKey, product = ProductId, hardwareFingerprint = _fingerprint }); } catch { }
            throw new InvalidOperationException("This key was issued for a different INSIGHT product.");
        }
        _serialKey = serialKey;
        Save();
        return await VerifyAsync();
    }

    internal async Task<string> DeactivateThisDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(_serialKey)) { IsPro = false; Status = "Free edition · no license activated"; return Status; }
        using var response = await PostAsync("unbind-device", new { serialKey = _serialKey, hardwareFingerprint = _fingerprint, product = ProductId });
        var result = await ReadAsync<ActivationResponse>(response);
        if (!response.IsSuccessStatusCode || result?.Success != true)
            throw new InvalidOperationException(result?.Message ?? "Could not deactivate this device.");
        Clear();
        IsPro = false; Status = "This device was deactivated";
        return Status;
    }

    private async Task<string> VerifyAsync()
    {
        try
        {
            using var response = await PostAsync("verify", new { serialKey = _serialKey, product = ProductId, hardwareFingerprint = _fingerprint });
            var result = await ReadAsync<VerificationResponse>(response);
            if (!response.IsSuccessStatusCode || result?.Valid != true || result.Success != true)
            {
                IsPro = false;
                Status = result?.Message ?? "License could not be verified online";
                if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound) Clear();
                return Status;
            }
            IsPro = true;
            _expiresAt = result.ExpiresAt;
            Status = result.ExpiresAt is null ? "Insight Screen Recorder Pro · lifetime" : $"Insight Screen Recorder Pro · expires {result.ExpiresAt:yyyy-MM-dd}";
            Save();
            return Status;
        }
        catch (HttpRequestException)
        {
            IsPro = false;
            Status = "License server is unavailable · connect to the internet to verify Pro";
            return Status;
        }
        catch (TaskCanceledException)
        {
            IsPro = false;
            Status = "License check timed out · connect to the internet to verify Pro";
            return Status;
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string endpoint, object body)
    {
        using var content = JsonContent.Create(body);
        return await Http.PostAsync(new Uri(BaseUri, endpoint), content);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response)
    {
        try { return await response.Content.ReadFromJsonAsync<T>(Json); }
        catch (JsonException) { return default; }
    }

    private static string? ReadTokenProduct(string? token)
    {
        try
        {
            var parts = token?.Split('.');
            if (parts is not { Length: 3 }) return null;
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty("product", out var product) ? product.GetString() : null;
        }
        catch { return null; }
    }

    private static string? ReadTokenKey(string? token)
    {
        try
        {
            var parts = token?.Split('.');
            if (parts is not { Length: 3 }) return null;
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty("key", out var key) ? key.GetString() : null;
        }
        catch { return null; }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return;
            var protectedBytes = File.ReadAllBytes(_statePath);
            var plain = ProtectedData.Unprotect(protectedBytes, Encoding.UTF8.GetBytes(ProductId), DataProtectionScope.CurrentUser);
            _serialKey = JsonSerializer.Deserialize<LicenseState>(plain, Json)?.SerialKey;
        }
        catch { _serialKey = null; }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_serialKey)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var data = JsonSerializer.SerializeToUtf8Bytes(new LicenseState(_serialKey));
        File.WriteAllBytes(_statePath, ProtectedData.Protect(data, Encoding.UTF8.GetBytes(ProductId), DataProtectionScope.CurrentUser));
    }

    private void Clear()
    {
        _serialKey = null; _expiresAt = null;
        try { if (File.Exists(_statePath)) File.Delete(_statePath); } catch { }
    }

    private static string GetFingerprint()
    {
        string machineId = (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string)?.Trim()
            ?? Environment.MachineName + "|" + Environment.UserName;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ProductId + "|" + machineId)));
    }

    private sealed record LicenseState(string SerialKey);
    private sealed class ActivationResponse { [JsonPropertyName("success")] public bool Success { get; set; } [JsonPropertyName("message")] public string? Message { get; set; } [JsonPropertyName("token")] public string? Token { get; set; } }
    private sealed class VerificationResponse { public bool Success { get; set; } public bool Valid { get; set; } public string? Message { get; set; } public DateTimeOffset? ExpiresAt { get; set; } }
}
