/*
 * Happy QOTD Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using JoyfulReaperLib.Sqlite;

namespace HappyQOTD.Data;

public static class QuoteDatabase
{
    public const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS Quotes
        (
            Id        INTEGER PRIMARY KEY AUTOINCREMENT,
            Text      TEXT NOT NULL,
            Author    TEXT,
            Source    TEXT,
            IsActive  INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS DailyQuoteSelections
        (
            SelectionDate TEXT PRIMARY KEY,
            QuoteId       INTEGER NOT NULL,
            SelectedAt    TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

            FOREIGN KEY (QuoteId)
                REFERENCES Quotes(Id)
        );

        WITH SeedQuotes(Text, Author, Source) AS
        (
            VALUES
                (
                    'The best code is no code at all.',
                    'Jeff Atwood',
                    NULL
                ),
                (
                    'Programs must be written for people to read.',
                    'Harold Abelson',
                    NULL
                ),
                (
                    'Any sufficiently advanced bug is indistinguishable from a feature.',
                    'Unknown',
                    NULL
                ),
                (
                    'Readable code is a favor you leave for your future self.',
                    NULL,
                    'HappyQOTD original'
                ),
                (
                    'The bug you can reproduce is already halfway fixed.',
                    NULL,
                    'HappyQOTD original'
                ),
                (
                    'A small working system beats a perfect system that never ships.',
                    NULL,
                    'HappyQOTD original'
                ),
                (
                    'Fast is useful. Correct is mandatory.',
                    NULL,
                    'HappyQOTD original'
                ),
                (
                    'Every abstraction should earn its complexity.',
                    NULL,
                    'HappyQOTD original'
                ),
                (
                    'Backups are cheaper than regret.',
                    NULL,
                    'HappyQOTD original'
                )
        )
        INSERT INTO Quotes
        (
            Text,
            Author,
            Source
        )
        SELECT
            seed.Text,
            seed.Author,
            seed.Source
        FROM SeedQuotes AS seed
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM Quotes AS existing
            WHERE existing.Text = seed.Text
        );
        """;

    public static string Initialize()
    {
        return SqliteDatabaseInitializer.Initialize("happyqotd.db", SchemaSql);
    }
}
