using System.Text.RegularExpressions;

namespace TravelDisruptionAgent.Application.Utilities;

/// <summary>Validates [policy-*] citations in the final answer against retrieved document ids.</summary>
public static partial class AgentCitationValidator
{
    /// <summary>Matches [policy-arrival], [policy-expenses], etc.</summary>
    [GeneratedRegex(@"\[(policy-[a-z0-9-]+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex PolicyCitationRegex();

    /// <summary>
    /// Removes citations that were not part of retrieved policy chunks. Returns cleaned text and warnings.
    /// </summary>
    public static (string Text, List<string> Warnings) SanitizeCitations(
        string answer,
        IReadOnlySet<string> allowedDocumentIds)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(answer))
            return (answer, warnings);

        if (allowedDocumentIds.Count == 0)
        {
            var stripped = PolicyCitationRegex().Replace(answer, m =>
            {
                warnings.Add($"Removed policy citation [{m.Groups[1].Value}] — no retrieved policy documents.");
                return "";
            });
            while (stripped.Contains("  ", StringComparison.Ordinal))
                stripped = stripped.Replace("  ", " ", StringComparison.Ordinal);
            return (stripped.Replace(" \n", "\n", StringComparison.Ordinal).Trim(), warnings);
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        var allowed = new HashSet<string>(allowedDocumentIds, comparer);

        var text = PolicyCitationRegex().Replace(answer, m =>
        {
            var id = m.Groups[1].Value;
            if (allowed.Contains(id))
                return m.Value;
            warnings.Add($"Removed invalid policy citation [{id}] — not in retrieved evidence set.");
            return "";
        });

        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        text = text.Replace(" \n", "\n", StringComparison.Ordinal).Trim();
        return (text, warnings);
    }

    public static IReadOnlyList<string> ListCitations(string answer) =>
        PolicyCitationRegex().Matches(answer).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
