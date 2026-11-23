using System.Reflection;
using System.Text.RegularExpressions;

namespace MicroinvestConvert.App.Updates;

/// <summary>What is new in every version: CHANGELOG.md, carried inside the program.</summary>
internal static partial class Changelog
{
    internal sealed record Section(Version Version, string Title, string Text);

    private static readonly Lazy<IReadOnlyList<Section>> Own = new(() => Parse(ReadOwn()));

    public static IReadOnlyList<Section> Sections => Own.Value;

    /// <summary>The notes of the versions after one and up to another, as they are in the file: newest first.</summary>
    public static string Between(Version after, Version upTo) => Join(Sections, after, upTo);

    internal static string Join(IEnumerable<Section> sections, Version after, Version upTo) => string.Join("\n\n",
        sections.Where(s => s.Version > after && s.Version <= upTo).Select(s => $"## {s.Title}\n\n{s.Text}"));

    /// <summary>The sections that begin with „## 1.2.3 …".</summary>
    internal static IReadOnlyList<Section> Parse(string markdown)
    {
        string text = markdown.ReplaceLineEndings("\n");
        var headings = Heading().Matches(text);
        return headings.Select((h, i) =>
        {
            int from = h.Index + h.Length, to = i + 1 < headings.Count ? headings[i + 1].Index : text.Length;
            return new Section(Version.Parse(h.Groups["version"].Value), h.Groups["title"].Value.Trim(), text[from..to].Trim());
        }).ToList();
    }

    private static string ReadOwn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CHANGELOG.md");
        if (stream == null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^## (?<title>(?<version>\d+\.\d+\.\d+)[^\n]*)$", RegexOptions.Multiline)]
    private static partial Regex Heading();
}
