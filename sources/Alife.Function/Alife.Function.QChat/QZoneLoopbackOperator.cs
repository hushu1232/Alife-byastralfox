using System;
using System.Net;
using System.Text.Json.Serialization;

namespace Alife.Function.QChat;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QZoneLoopbackOperatorOperation
{
    Read,
    Post,
    Comment,
    Like,
    Image,
    Delete,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QZoneLoopbackOperatorResultCode
{
    Accepted,
    InvalidOperation,
    InvalidEndpoint,
}

public sealed record QZoneLoopbackOperatorRequest
{
    [JsonPropertyName("operation")]
    public QZoneLoopbackOperatorOperation? Operation { get; init; }

    public QZoneLoopbackOperatorResult Validate()
    {
        return Operation is { } operation && Enum.IsDefined(operation)
            ? QZoneLoopbackOperatorResult.Accepted()
            : QZoneLoopbackOperatorResult.Rejected(QZoneLoopbackOperatorResultCode.InvalidOperation);
    }
}

public sealed record QZoneLoopbackOperatorResult(
    [property: JsonPropertyName("succeeded")] bool Succeeded,
    [property: JsonPropertyName("code")] QZoneLoopbackOperatorResultCode Code)
{
    public static QZoneLoopbackOperatorResult Accepted() =>
        new(true, QZoneLoopbackOperatorResultCode.Accepted);

    public static QZoneLoopbackOperatorResult Rejected(QZoneLoopbackOperatorResultCode code)
    {
        if (code == QZoneLoopbackOperatorResultCode.Accepted)
            throw new ArgumentOutOfRangeException(nameof(code));

        return new QZoneLoopbackOperatorResult(false, code);
    }
}

public sealed class QZoneLoopbackOperatorEndpoint
{
    QZoneLoopbackOperatorEndpoint(Uri uri)
    {
        Uri = uri;
    }

    public Uri Uri { get; }

    public static bool TryCreate(
        string? value,
        out QZoneLoopbackOperatorEndpoint? endpoint,
        out QZoneLoopbackOperatorResultCode code)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) == false ||
            uri.Scheme != Uri.UriSchemeHttp ||
            IsAllowedHost(uri) == false ||
            string.IsNullOrEmpty(uri.UserInfo) == false ||
            string.IsNullOrEmpty(uri.Query) == false ||
            string.IsNullOrEmpty(uri.Fragment) == false)
        {
            endpoint = null;
            code = QZoneLoopbackOperatorResultCode.InvalidEndpoint;
            return false;
        }

        endpoint = new QZoneLoopbackOperatorEndpoint(uri);
        code = QZoneLoopbackOperatorResultCode.Accepted;
        return true;
    }

    static bool IsAllowedHost(Uri uri)
    {
        return string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal) ||
               string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               (IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? address) &&
                IPAddress.IPv6Loopback.Equals(address));
    }
}
