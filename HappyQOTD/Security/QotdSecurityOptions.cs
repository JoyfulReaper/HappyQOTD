namespace HappyQOTD.Security;

public sealed class QotdSecurityOptions
{
    public const string SectionName = "QotdSecurity";

    public string AdminApiKey { get; set; } = string.Empty;
}