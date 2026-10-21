using HMS.Application.Auditing;
using HMS.Application.Documents;

namespace HMS.Application.UnitTests;

public class AuditCsvTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+2348000000000", "'+2348000000000")]
    [InlineData("-1", "'-1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("plain", "plain")]
    [InlineData(null, "")]
    public void Cells_are_escaped_and_formulas_neutralised(string? value, string expected) =>
        Assert.Equal(expected, AuditQueryService.Cell(value));
}

public class FileRulesTests
{
    [Theory]
    [InlineData("scan.pdf", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
    [InlineData("photo.JPG", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData("sheet.xlsx", "application/octet-stream", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void Allowed_files_pass(string name, string declared, byte[] header, string expected)
    {
        var (contentType, error) = FileRules.Check(name, declared, header);
        Assert.Null(error);
        Assert.Equal(expected, contentType);
    }

    [Theory]
    [InlineData("tool.exe", "application/octet-stream", new byte[] { 0x4D, 0x5A })]
    [InlineData("fake.pdf", "application/pdf", new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03 })]
    [InlineData("scan.pdf", "text/html", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D })]
    [InlineData("noextension", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D })]
    public void Disallowed_or_disguised_files_are_refused(string name, string declared, byte[] header) =>
        Assert.NotNull(FileRules.Check(name, declared, header).Error);

    [Theory]
    [InlineData("..\\..\\windows\\system.pdf", "system.pdf")]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("in<v>a|lid?.pdf", "invalid.pdf")]
    public void Display_names_cannot_carry_paths(string input, string expected) => Assert.Equal(expected, FileRules.SafeName(input));
}
