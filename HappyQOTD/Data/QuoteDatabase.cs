using JoyfulReaperLib.Sqlite;

namespace HappyQOTD.Data;

public static class QuoteDatabase
{
    public static string Initialize()
    {
        const string schemaSql = """
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

            INSERT OR IGNORE INTO Quotes
                (Id, Text, Author, Source)
            VALUES
                (
                    1,
                    'The best code is no code at all.',
                    'Jeff Atwood',
                    NULL
                ),
                (
                    2,
                    'Programs must be written for people to read.',
                    'Harold Abelson',
                    NULL
                ),
                (
                    3,
                    'Any sufficiently advanced bug is indistinguishable from a feature.',
                    'Unknown',
                    NULL
                );
            """;

        return SqliteDatabaseInitializer.Initialize(
            "happyqotd.db",
            schemaSql);
    }
}