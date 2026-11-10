using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Domain.Steps;

internal sealed record RtlTestReport(
    string LocalText, int DeviceType, string DeviceIdent, string? DeviceSerial,
    string StandName, DateTime TestTime, bool IsFull, bool IsSuccess, IReadOnlyList<TestReportEntry> SubTests)
{
    internal const string Endpoint = "/api/Api.svc/SendTestResultUniverse";
    internal static bool IsEndpoint(string? value) =>
        string.Equals(value?.Trim().TrimStart('/'), Endpoint.TrimStart('/'), StringComparison.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    internal HttpContent CreateContent(string sessionId)
    {
        var json = JsonSerializer.Serialize(new
        {
            result = new
            {
                deviceSerial = DeviceSerial,
                deviceType = DeviceType,
                deviceIdent = DeviceIdent,
                standName = StandName,
                testTime = TestTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                isFull = IsFull,
                isSuccess = IsSuccess,
                userId = 0,
                subTests = SubTests.Select(entry => new
                {
                    name = entry.Name, isSuccess = entry.IsSuccess, additionalInfo = entry.Value
                }),
                testId = 0,
                specialDeviceType = 0,
                hwVersion = (string?)null
            },
            sessId = sessionId,
            isPswDevice = true
        });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    internal bool TryReadSerial(string json, out int serial)
    {
        serial = 0;
        try
        {
            var response = JsonSerializer.Deserialize<DeviceInfo>(json, ResponseOptions);
            return response is { Id: > 0 } &&
                (response.DeviceType == null || response.DeviceType == DeviceType) &&
                int.TryParse(response.SerialNumber.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out serial) &&
                serial > 0;
        }
        catch (JsonException) { return false; }
    }

    private sealed record DeviceInfo(uint Id, JsonElement SerialNumber, int? DeviceType);
}
