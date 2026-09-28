using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>The input as it may leave the platform, and its SHA-256 (the <c>input_hash</c> column).</summary>
public record PseudonymizedInput(string Json, string Hash);

/// <summary>
///     IA-0, guard 3. Minimization and pseudonymization of every input before it reaches the provider: the input
///     never carries a name, an email, a real identifier or a photo. Each context already builds its input from
///     aggregates (kcal per day, adherence, counts, trend, restrictions, targets); this is the technical net under
///     that discipline, applied the same way to every function.
/// </summary>
/// <remarks>
///     What it does, on the JSON of the input:
///     1. drops every property whose name identifies a person or carries an image (name, email, phone, photo…);
///     2. replaces the value of every identifier property (<c>id</c>, <c>…Id</c>, <c>…Ids</c>, <c>…_id</c>) with a
///     reference local to the request (<c>ref-1</c>, <c>ref-2</c>…; the same real value gets the same reference);
///     3. in every remaining string, masks email addresses and the known identifiers of the request (the names
///     of the patient and of the practitioner, which the caller passes).
///     Numbers outside identifier properties stay as they are: they are the aggregates the function needs.
/// </remarks>
public static partial class AiInputPseudonymizer
{
    public const string EmailMask = "[email]";
    public const string IdentifierMask = "[redacted]";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Property names (lower case, without separators) that are dropped, whatever their value.</summary>
    private static readonly HashSet<string> IdentifyingProperties =
    [
        "name", "fullname", "displayname", "givenname", "givennames", "familyname", "familynames", "firstname",
        "lastname", "surname", "username", "email", "emailaddress", "mail", "phone", "phonenumber", "mobile",
        "address", "photo", "photourl", "photos", "image", "imageurl", "images", "picture", "pictureurl",
        "avatar", "avatarurl", "documentnumber", "dni", "nationalid", "token", "password"
    ];

    public static PseudonymizedInput Pseudonymize(object input, IReadOnlyCollection<string>? knownIdentifiers = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var node = input as JsonNode ?? JsonSerializer.SerializeToNode(input, input.GetType(), SerializerOptions)
            ?? throw new ArgumentException("The input serializes to null.", nameof(input));
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        var known = (knownIdentifiers ?? [])
            .Where(k => !string.IsNullOrWhiteSpace(k) && k.Trim().Length >= 2)
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // Longest first, so "Ana María" is masked whole before "Ana".
            .OrderByDescending(k => k.Length)
            .ToList();

        // Builds a new tree: the caller's node is never modified.
        var clean = Clean(node, references, known);
        var json = clean?.ToJsonString() ?? "null";
        return new PseudonymizedInput(json, Sha256(json));
    }

    public static string Sha256(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static JsonNode? Clean(JsonNode? node, Dictionary<string, string> references, List<string> known)
    {
        switch (node)
        {
            case JsonObject obj:
                var cleanObject = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (IsIdentifying(key)) continue;
                    cleanObject[key] = IsIdentifier(key)
                        ? Reference(value, references)
                        : Clean(value, references, known);
                }

                return cleanObject;
            case JsonArray array:
                return new JsonArray(array.Select(item => Clean(item, references, known)).ToArray());
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                return JsonValue.Create(Mask(value.GetValue<string>(), known));
            default:
                return node?.DeepClone();
        }
    }

    private static JsonNode? Reference(JsonNode? value, Dictionary<string, string> references)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonArray array:
                return new JsonArray(array.Select(item => Reference(item, references)).ToArray());
            case JsonObject:
                // An object under an identifier name has no business here: nothing of it leaves.
                return JsonValue.Create(IdentifierMask);
            default:
                var raw = value.ToJsonString();
                if (!references.TryGetValue(raw, out var reference))
                {
                    reference = $"ref-{references.Count + 1}";
                    references[raw] = reference;
                }

                return JsonValue.Create(reference);
        }
    }

    private static string Mask(string text, List<string> known)
    {
        var masked = EmailPattern().Replace(text, EmailMask);
        // Whole words only: "Ana" is masked, "banana" is not.
        foreach (var identifier in known)
            masked = Regex.Replace(masked, $@"(?<!\w){Regex.Escape(identifier)}(?!\w)", IdentifierMask,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return masked;
    }

    private static bool IsIdentifying(string key)
    {
        return IdentifyingProperties.Contains(Normalize(key));
    }

    /// <summary><c>id</c>, <c>patientId</c>, <c>mealIds</c>, <c>patient_id</c>, <c>PatientID</c>.</summary>
    private static bool IsIdentifier(string key)
    {
        return key.Equals("id", StringComparison.OrdinalIgnoreCase)
               || key.Equals("ids", StringComparison.OrdinalIgnoreCase)
               || key.EndsWith("Id", StringComparison.Ordinal)
               || key.EndsWith("Ids", StringComparison.Ordinal)
               || key.EndsWith("ID", StringComparison.Ordinal)
               || key.EndsWith("_id", StringComparison.OrdinalIgnoreCase)
               || key.EndsWith("_ids", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string key)
    {
        return new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
