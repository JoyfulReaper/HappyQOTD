using JoyfulReaperLib.Sqlite;

namespace HappyQOTD.Data;

public static class QuoteDatabase
{
    public static string Initialize()
    {
        const string schemaSql = """
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

        return SqliteDatabaseInitializer.Initialize(
            "happyqotd.db",
            schemaSql);
    }
}