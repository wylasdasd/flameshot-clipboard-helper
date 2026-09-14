using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BrowserTools;

public static partial class Redact
{
    public const string Redacted = "[REDACTED]";

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "proxy-authorization",
        "authentication",
        "cookie",
        "set-cookie",
        "x-api-key",
        "api-key",
        "apikey",
        "x-auth-token",
        "x-access-token",
        "x-session-token",
        "x-csrf-token",
        "x-xsrf-token",
        "x-amz-security-token",
        "x-goog-api-key",
        "x-functions-key",
        "x-secret"
    };

    [GeneratedRegex(@"-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----[\s\S]*?-----END (?:[A-Z]+ )?PRIVATE KEY-----")]
    private static partial Regex PemKey();

    [GeneratedRegex(@"\b(?:sess|session|client|tok|token|auth|cred|secret|apikey)_[A-Za-z0-9]{16,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex VendorIds();

    [GeneratedRegex(@"\b(?:AKIA|ASIA|AGPA|AIDA|AROA|AIPA|ANPA|ANVA)[A-Z0-9]{16}\b")]
    private static partial Regex AwsKeys();

    [GeneratedRegex(@"\bgithub_pat_[A-Za-z0-9_]{20,}\b")]
    private static partial Regex GitHubPat();

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{20,}\b")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\bsk-(?:ant-)?[A-Za-z0-9_-]{20,}\b")]
    private static partial Regex LlmKeys();

    [GeneratedRegex(@"\b(?:sk|pk|rk)_(?:live|test)_[A-Za-z0-9]{10,}\b")]
    private static partial Regex StripeKeys();

    [GeneratedRegex(@"\bxox[baprs]-[A-Za-z0-9-]{10,}\b")]
    private static partial Regex SlackKeys();

    [GeneratedRegex(@"\bAIza[A-Za-z0-9_-]{35}\b")]
    private static partial Regex GoogleKeys();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{15,}(?:\.[A-Za-z0-9_-]+){0,2}")]
    private static partial Regex JwtCandidate();

    [GeneratedRegex(@"""(?:alg|typ|kid)""")]
    private static partial Regex JwtHeaderFields();

    [GeneratedRegex(@"\b(Bearer|Basic|Token|Digest)\s+[A-Za-z0-9._~+/=-]{16,}", RegexOptions.IgnoreCase)]
    private static partial Regex AuthScheme();

    [GeneratedRegex(@"(""(?:[^""]*(?:password|passwd|secret|token|api[_-]?key|apikey|credential|private[_-]?key|auth)[^""]*)""\s*:\s*)""(?:[^""\\]|\\.)*""", RegexOptions.IgnoreCase)]
    private static partial Regex SecretishJson();

    [GeneratedRegex(@"(password|passwd|secret|api[_-]?key|apikey|credential|private[_-]?key|access[_-]?token|refresh[_-]?token)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretishKeyName();

    public static bool IsSensitiveHeaderName(string name) => SensitiveHeaders.Contains(name);

    public static bool IsJwt(string candidate)
    {
        if (candidate.Split('.').Length >= 3)
            return true;
        var header = candidate.Split('.')[0];
        return JwtHeaderFields().IsMatch(DecodeBase64Prefix(header));
    }

    public static string SecretsInString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var outValue = PemKey().Replace(input, Redacted);
        outValue = VendorIds().Replace(outValue, Redacted);
        outValue = AwsKeys().Replace(outValue, Redacted);
        outValue = GitHubPat().Replace(outValue, Redacted);
        outValue = GitHubToken().Replace(outValue, Redacted);
        outValue = LlmKeys().Replace(outValue, Redacted);
        outValue = StripeKeys().Replace(outValue, Redacted);
        outValue = SlackKeys().Replace(outValue, Redacted);
        outValue = GoogleKeys().Replace(outValue, Redacted);
        outValue = JwtCandidate().Replace(outValue, match => IsJwt(match.Value) ? Redacted : match.Value);
        outValue = AuthScheme().Replace(outValue, match => $"{match.Groups[1].Value} {Redacted}");
        outValue = SecretishJson().Replace(outValue, match => $"{match.Groups[1].Value}\"{Redacted}\"");
        return outValue;
    }

    public static Dictionary<string, string> Headers(IReadOnlyDictionary<string, string>? headers)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (headers is null)
            return result;

        foreach (var (name, value) in headers)
            result[name] = IsSensitiveHeaderName(name) ? Redacted : SecretsInString(value);
        return result;
    }

    public static JsonElement Value(JsonElement value, bool enabled = true)
        => enabled ? Walk(value) : value;

    private static JsonElement Walk(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => JsonUtil.FromString(SecretsInString(value.GetString() ?? "")),
            JsonValueKind.Array => WalkArray(value),
            JsonValueKind.Object => WalkObject(value),
            _ => value.Clone()
        };
    }

    private static JsonElement WalkArray(JsonElement array)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in array.EnumerateArray())
                Walk(item).WriteTo(writer);
            writer.WriteEndArray();
        }

        return JsonUtil.Parse(stream.ToArray());
    }

    private static JsonElement WalkObject(JsonElement obj)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in obj.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (IsSensitiveHeaderName(property.Name) || SecretishKeyName().IsMatch(property.Name))
                    writer.WriteStringValue(Redacted);
                else
                    Walk(property.Value).WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return JsonUtil.Parse(stream.ToArray());
    }

    private static string DecodeBase64Prefix(string value)
    {
        var usable = value.Length > 40 ? value[..40] : value;
        var aligned = usable[..(usable.Length - usable.Length % 4)];
        if (aligned.Length == 0)
            return "";

        try
        {
            var padded = aligned.Replace('-', '+').Replace('_', '/');
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch
        {
            return "";
        }
    }
}
