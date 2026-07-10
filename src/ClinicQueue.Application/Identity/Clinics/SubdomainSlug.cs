using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClinicQueue.Application.Identity.Clinics;

public static class SubdomainSlug
{
    private static readonly Regex NonSlug = new("[^a-z0-9]+", RegexOptions.Compiled);

    public static string FromClinicName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        var ascii = new string(chars).Normalize(NormalizationForm.FormC);
        var slug = NonSlug.Replace(ascii, "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "clinic";
        }

        return $"{slug}-{Guid.NewGuid().ToString("N")[..8]}";
    }
}
