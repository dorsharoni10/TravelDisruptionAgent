using System.Text;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>
/// Bounded transcript: full text for recent observations, single-line rollup for older steps.
/// </summary>
public sealed class AgentTranscriptBuffer
{
    private readonly int _maxFullObservations;
    private readonly int _maxTotalChars;
    private readonly Queue<string> _fullBlocks = new();
    private readonly List<string> _rolledUpLines = [];
    private int _approxChars;

    public AgentTranscriptBuffer(int maxFullObservations, int maxTotalChars)
    {
        _maxFullObservations = Math.Max(1, maxFullObservations);
        _maxTotalChars = Math.Max(2000, maxTotalChars);
    }

    public void AppendObservation(int stepIndex, string observationText)
    {
        var block = $"--- Step {stepIndex} observation ---\n{observationText}";
        _fullBlocks.Enqueue(block);
        _approxChars += block.Length;

        while (_fullBlocks.Count > _maxFullObservations)
        {
            var old = _fullBlocks.Dequeue();
            _approxChars -= old.Length;
            var oneLine = ToRollupLine(old);
            _rolledUpLines.Add(oneLine);
        }

        TrimByTotalChars();
    }

    public string BuildTranscript(string headerLine)
    {
        var sb = new StringBuilder();
        sb.AppendLine(headerLine);
        if (_rolledUpLines.Count > 0)
        {
            sb.AppendLine("--- Earlier steps (summary) ---");
            foreach (var line in _rolledUpLines)
                sb.AppendLine(line);
            sb.AppendLine("--- Recent full observations ---");
        }

        foreach (var block in _fullBlocks)
            sb.AppendLine(block);

        var s = sb.ToString();
        if (s.Length <= _maxTotalChars)
            return s;
        return s[^_maxTotalChars..];
    }

    private void TrimByTotalChars()
    {
        while (_approxChars > _maxTotalChars && _fullBlocks.Count > 1)
        {
            var old = _fullBlocks.Dequeue();
            _approxChars -= old.Length;
            _rolledUpLines.Add(ToRollupLine(old));
        }
    }

    private static string ToRollupLine(string block)
    {
        var flat = block.Replace("\r", " ").Replace("\n", " ", StringComparison.Ordinal);
        return flat.Length <= 200 ? flat : flat[..200] + "…";
    }
}
