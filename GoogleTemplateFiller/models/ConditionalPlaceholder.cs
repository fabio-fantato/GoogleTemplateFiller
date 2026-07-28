namespace GoogleTemplateFiller.models;

// Represents a single "{{if:name}}" or "{{endif:name}}" tag found in the document.
public class ConditionalTag
{
    public string Name { get; set; } = string.Empty;
    public bool IsEnd { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }

    // Parses "{{if:name}}" or "{{endif:name}}". Returns null for anything else.
    public static ConditionalTag? Parse(string text)
    {
        bool isEnd = text.StartsWith("{{endif:", StringComparison.Ordinal);
        bool isStart = !isEnd && text.StartsWith("{{if:", StringComparison.Ordinal);
        if ((!isStart && !isEnd) || !text.EndsWith("}}", StringComparison.Ordinal))
            return null;

        int prefixLength = isEnd ? 8 : 5;
        string name = text[prefixLength..^2].Trim();
        if (name.Length == 0)
            return null;

        return new ConditionalTag { Name = name, IsEnd = isEnd };
    }
}

// A matched {{if:name}} ... {{endif:name}} block, with the doc index ranges
// of the tags themselves (BlockStart/BlockEnd) and of the enclosed content
// (ContentStart/ContentEnd).
public class ConditionalBlock
{
    public string Name { get; set; } = string.Empty;
    public int BlockStart { get; set; }
    public int BlockEnd { get; set; }
    public int ContentStart { get; set; }
    public int ContentEnd { get; set; }

    // OutSystems booleans travel as strings ("True"/"False"), so a field is
    // truthy unless it's missing, empty, "false" or "0".
    public static bool IsTruthy(Dictionary<string, string> fields, string name)
    {
        if (!fields.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        return !value.Equals("false", StringComparison.OrdinalIgnoreCase) && value != "0";
    }
}
