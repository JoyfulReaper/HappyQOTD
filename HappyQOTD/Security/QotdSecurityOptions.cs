/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyQOTD.Security;

public sealed class QotdSecurityOptions
{
    public const string SectionName = "QotdSecurity";
    public string AdminApiKey { get; set; } = string.Empty;
}