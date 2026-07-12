using System.Text.Json;
using FluentAssertions;

namespace ClinicQueue.UnitTests.Web;

/// <summary>
/// Accessibility + RTL/i18n QA gate (T085): ar/en locale key parity and no empty strings.
/// </summary>
public sealed class LocaleParityTests
{
    [Fact]
    public void Arabic_and_English_locale_keys_match()
    {
        var (arPath, enPath) = ResolveLocalePaths();
        using var arDoc = JsonDocument.Parse(File.ReadAllText(arPath));
        using var enDoc = JsonDocument.Parse(File.ReadAllText(enPath));

        var arKeys = Flatten(arDoc.RootElement).ToDictionary(x => x.Key, x => x.Value);
        var enKeys = Flatten(enDoc.RootElement).ToDictionary(x => x.Key, x => x.Value);

        // Compare base keys (plural category suffixes stripped): Arabic's CLDR plural
        // set (zero/one/two/few/many/other) is legitimately larger than English's
        // (one/other), so an exact key-count match would be a false failure.
        var arBaseKeys = arKeys.Keys.Select(StripPluralSuffix).Distinct();
        var enBaseKeys = enKeys.Keys.Select(StripPluralSuffix).Distinct();

        arBaseKeys.Should().BeEquivalentTo(enBaseKeys);
        arKeys.Values.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v));
        enKeys.Values.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v));
    }

    private static readonly string[] PluralSuffixes =
        { "_zero", "_one", "_two", "_few", "_many", "_other" };

    private static string StripPluralSuffix(string key)
    {
        foreach (var suffix in PluralSuffixes)
        {
            if (key.EndsWith(suffix, StringComparison.Ordinal))
            {
                return key[..^suffix.Length];
            }
        }

        return key;
    }

    [Fact]
    public void Feature_tsx_files_do_not_hardcode_membership_guid_placeholder()
    {
        var webSrc = ResolveWebSrc();
        var offenders = Directory.EnumerateFiles(webSrc, "*.tsx", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("placeholder=\"membership-guid\"", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(webSrc, f))
            .ToArray();

        offenders.Should().BeEmpty();
    }

    private static (string Ar, string En) ResolveLocalePaths()
    {
        var webSrc = ResolveWebSrc();
        return (
            Path.Combine(webSrc, "locales", "ar.json"),
            Path.Combine(webSrc, "locales", "en.json"));
    }

    private static string ResolveWebSrc()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web", "src")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "web", "src"))
        };
        var path = candidates.FirstOrDefault(Directory.Exists);
        path.Should().NotBeNull("web/src must be resolvable for locale QA");
        return path!;
    }

    private static IEnumerable<KeyValuePair<string, string>> Flatten(JsonElement element, string prefix = "")
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                foreach (var child in Flatten(prop.Value, key))
                {
                    yield return child;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            yield return new KeyValuePair<string, string>(prefix, element.GetString() ?? "");
        }
    }
}
