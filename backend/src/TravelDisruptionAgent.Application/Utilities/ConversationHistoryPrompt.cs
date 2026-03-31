using System.Text;
using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Utilities;

public static class ConversationHistoryPrompt
{
    /// <summary>
    /// Formats prior turns for LLM prompts. Prefers keeping the <b>most recent</b> messages (especially the last
    /// assistant reply); when over budget, shortens older turns instead of dropping them first.
    /// </summary>
    public static string Build(IReadOnlyList<ChatMessage> priorTurns, int maxMessages, int maxChars)
    {
        if (priorTurns.Count == 0 || maxMessages <= 0 || maxChars <= 0)
            return "";

        var slice = priorTurns.Count <= maxMessages
            ? priorTurns.ToList()
            : priorTurns.Skip(priorTurns.Count - maxMessages).ToList();

        var working = slice
            .Select(m => m with { Content = m.Content.Trim() })
            .ToList();

        for (var guard = 0; guard < 80 && Format(working).Length > maxChars; guard++)
        {
            var shortened = false;
            // Shorten oldest messages first; keep the last turn (usually last assistant answer) longest.
            for (var i = 0; i < working.Count - 1; i++)
            {
                if (working[i].Content.Length <= 320)
                    continue;
                var keep = Math.Max(280, working[i].Content.Length * 2 / 3);
                working[i] = working[i] with { Content = working[i].Content[..keep].TrimEnd() + "…" };
                shortened = true;
                if (Format(working).Length <= maxChars)
                    return Format(working).TrimEnd();
            }

            if (!shortened && working.Count > 0)
            {
                var last = working[^1];
                if (last.Content.Length > 400)
                {
                    var keep = Math.Max(400, last.Content.Length * 3 / 4);
                    working[^1] = last with { Content = last.Content[..keep].TrimEnd() + "…" };
                    continue;
                }

                working.RemoveAt(0);
            }
        }

        var result = Format(working);
        if (result.Length <= maxChars)
            return result.TrimEnd();
        var take = Math.Min(maxChars, result.Length);
        return result[..take].TrimEnd() + "…";
    }

    private static string Format(List<ChatMessage> slice)
    {
        var sb = new StringBuilder();
        foreach (var m in slice)
        {
            var label = m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
            sb.Append(label).Append(": ").AppendLine(m.Content);
        }

        return sb.ToString().TrimEnd();
    }
}
