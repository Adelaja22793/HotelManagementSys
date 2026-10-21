namespace HMS.Application.Documents;

/// <summary>Allowed file types, checked by extension, declared MIME type and the file's own signature bytes.</summary>
public static class FileRules
{
    private sealed record Rule(string ContentType, byte[][] Signatures);

    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    private static readonly Dictionary<string, Rule> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = new("application/pdf", [Pdf]),
        [".png"] = new("image/png", [Png]),
        [".jpg"] = new("image/jpeg", [Jpeg]),
        [".jpeg"] = new("image/jpeg", [Jpeg]),
        [".docx"] = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [Zip]),
        [".xlsx"] = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [Zip]),
    };

    public static IReadOnlyCollection<string> Extensions => Rules.Keys;

    /// <summary>Returns the content type to store, or an error message.</summary>
    public static (string? ContentType, string? Error) Check(string fileName, string? declaredContentType, ReadOnlySpan<byte> header)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !Rules.TryGetValue(extension, out var rule))
            return (null, $"Only {string.Join(", ", Rules.Keys)} files can be uploaded.");
        if (!string.IsNullOrWhiteSpace(declaredContentType)
            && !declaredContentType.Equals(rule.ContentType, StringComparison.OrdinalIgnoreCase)
            && !declaredContentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return (null, "The file type does not match its extension.");
        foreach (var signature in rule.Signatures)
            if (header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature))
                return (rule.ContentType, null);
        return (null, "The file content does not match its extension.");
    }

    /// <summary>Keeps only a safe display name: no folders, control characters or path tricks.</summary>
    public static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?')).ToArray()).Trim();
        if (name.Length == 0) name = "file";
        return name.Length > 200 ? name[^200..] : name;
    }
}
