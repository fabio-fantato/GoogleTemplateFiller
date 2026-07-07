namespace GoogleTemplateFiller.models;

public class ImagePlaceholder
{
    public string Name { get; set; } = string.Empty;
    public string RawPlaceholder { get; set; } = string.Empty;
    public float? Width { get; set; }
    public float? Height { get; set; }

    // Parses "{{img:name|w:200|h:150}}" — width/height in points, both optional.
    public static ImagePlaceholder? Parse(string text)
    {
        if (!text.StartsWith("{{img:", StringComparison.Ordinal) || !text.EndsWith("}}", StringComparison.Ordinal))
            return null;

        string inner = text[6..^2];
        string[] parts = inner.Split('|');

        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            return null;

        var result = new ImagePlaceholder
        {
            Name = parts[0].Trim(),
            RawPlaceholder = text
        };

        foreach (string part in parts[1..])
        {
            if (part.StartsWith("w:", StringComparison.Ordinal) &&
                float.TryParse(part[2..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w))
                result.Width = w;
            else if (part.StartsWith("h:", StringComparison.Ordinal) &&
                float.TryParse(part[2..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h))
                result.Height = h;
        }

        return result;
    }
}
