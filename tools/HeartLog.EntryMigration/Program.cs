using HeartLog.BLL.Services.Encryption;
using Npgsql;
using NpgsqlTypes;

// Never print exception messages: provider exceptions can include connection/data values.
Guid? activeEntry = null;
try
{
    if (args.Length < 2 || args[0] is not ("inspect" or "backfill" or "verify" or "check-key"))
        throw new InvalidOperationException("Usage: <inspect|backfill|verify|check-key> <env-file> [maximum-entries]");
    DotNetEnv.Env.Load(args[1]);
    var encryption = new EntryEncryption(Environment.GetEnvironmentVariable("EntryEncryption__Keys__v1"));
    await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
    await connection.OpenAsync();
    await new NpgsqlCommand("SET TIME ZONE 'UTC'", connection).ExecuteNonQueryAsync();
    var legacy = (bool)(await new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'EmotionEntries' AND column_name = 'Comment')", connection).ExecuteScalarAsync())!;
    if (args[0] == "inspect")
    {
        foreach (var (label, sql) in new[] {
            ("entries", "SELECT count(*) FROM \"EmotionEntries\""),
            ("legacy_entries", "SELECT count(*) FROM \"EmotionEntries\" WHERE \"EncryptedEmotions\" IS NULL"),
            ("encrypted_entries", "SELECT count(*) FROM \"EmotionEntries\" WHERE \"EncryptedEmotions\" IS NOT NULL") })
            Console.WriteLine($"{label}={await new NpgsqlCommand(sql, connection).ExecuteScalarAsync()}");
        Console.WriteLine($"legacy_schema={legacy}");
        if (legacy)
        {
            Console.WriteLine($"plaintext_notes={await new NpgsqlCommand("SELECT count(*) FROM \"EmotionEntries\" WHERE \"Comment\" IS NOT NULL", connection).ExecuteScalarAsync()}");
            Console.WriteLine($"legacy_emotion_links={await new NpgsqlCommand("SELECT count(*) FROM \"EmotionEntryEmotions\"", connection).ExecuteScalarAsync()}");
        }
        return;
    }
    if (args[0] == "check-key")
    {
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var rows = await ReadRows(connection, transaction, false, false, int.MaxValue);
        var authenticated = 0;
        foreach (var row in rows.Where(row => row.Emotions is not null))
        {
            Verify(encryption, row, null, false);
            authenticated++;
        }
        if (authenticated == 0) throw new InvalidOperationException();
        await transaction.CommitAsync();
        Console.WriteLine($"key_authenticated_entries={authenticated}");
        return;
    }
    var maximum = args.Length > 2 ? int.Parse(args[2]) : int.MaxValue;
    var processed = 0;
    if (args[0] == "backfill")
    {
        if (!legacy) throw new InvalidOperationException();
        // Authenticate existing bundles before any write, so a misconfigured key
        // cannot silently introduce another key under the same v1 identifier.
        await using (var preflight = await connection.BeginTransactionAsync())
        {
            foreach (var row in (await ReadRows(connection, preflight, false, false, int.MaxValue)).Where(row => row.Emotions is not null))
            {
                activeEntry = row.Id;
                Verify(encryption, row, null, false);
            }
            await preflight.CommitAsync();
        }
        // Lock both tables for each small batch. This also excludes link-only writers.
        // No API request can observe a half-written pair of encrypted fields.
        while (processed < maximum)
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await Execute("SET LOCAL lock_timeout = '10s'; SET LOCAL statement_timeout = '60s'; LOCK TABLE \"EmotionEntries\", \"EmotionEntryEmotions\" IN SHARE ROW EXCLUSIVE MODE", connection, transaction);
            var rows = await ReadRows(connection, transaction, true, true, Math.Min(100, maximum - processed));
            if (rows.Count == 0) { await transaction.CommitAsync(); break; }
            foreach (var row in rows)
            {
                activeEntry = row.Id;
                var emotions = await ReadEmotions(connection, transaction, row.Id);
                var note = row.Comment is null ? null : encryption.Encrypt(row.Owner, row.Id, "note", row.Comment);
                var selections = encryption.Encrypt(row.Owner, row.Id, "emotions", emotions);
                await using var update = new NpgsqlCommand("UPDATE \"EmotionEntries\" SET \"EncryptedNote\" = @note, \"EncryptedEmotions\" = @emotions WHERE \"Id\" = @id AND \"EncryptedEmotions\" IS NULL", connection, transaction);
                update.Parameters.AddWithValue("note", NpgsqlDbType.Jsonb, (object?)note ?? DBNull.Value);
                update.Parameters.AddWithValue("emotions", NpgsqlDbType.Jsonb, selections);
                update.Parameters.AddWithValue("id", row.Id);
                if (await update.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException();
                await using var read = new NpgsqlCommand("SELECT \"EncryptedNote\"::text, \"EncryptedEmotions\"::text FROM \"EmotionEntries\" WHERE \"Id\" = @id", connection, transaction);
                read.Parameters.AddWithValue("id", row.Id);
                await using var reader = await read.ExecuteReaderAsync();
                await reader.ReadAsync();
                Verify(encryption, row with { Note = reader.IsDBNull(0) ? null : reader.GetString(0), Emotions = reader.GetString(1) }, emotions, true);
            }
            await transaction.CommitAsync();
            processed += rows.Count;
            Console.WriteLine($"committed_verified={processed}");
        }
    }
    else
    {
        // Revoke earlier approval before verifying: a failed comparison must not
        // leave a previous receipt that could still authorize schema cleanup.
        if (legacy) await new NpgsqlCommand("DROP TABLE IF EXISTS \"EntryEncryptionCutover\"", connection).ExecuteNonQueryAsync();
        // A consistent snapshot; never disclose the decrypted content.
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);

        if (legacy) await Execute("LOCK TABLE \"EmotionEntries\", \"EmotionEntryEmotions\" IN SHARE ROW EXCLUSIVE MODE", connection, transaction);
        var rows = await ReadRows(connection, transaction, legacy, false, int.MaxValue);
        foreach (var row in rows)
        {
            activeEntry = row.Id;
            if (row.Emotions is null) throw new InvalidOperationException();
            var original = legacy ? await ReadEmotions(connection, transaction, row.Id) : null;
            // API edits already clear their legacy fields. For those, authenticate only.
            var compare = legacy && (row.Comment is not null || original!.Count > 0);
            Verify(encryption, row, original, compare);
            processed++;
        }
        if (legacy)
        {
            // The locks above remain held through receipt generation.
            await Execute("CREATE TABLE IF NOT EXISTS \"EntryEncryptionCutover\" (\"Id\" integer PRIMARY KEY CHECK (\"Id\" = 1), \"VerifiedAt\" timestamptz NOT NULL, \"Fingerprint\" text NOT NULL)", connection, transaction);
            await Execute("DELETE FROM \"EntryEncryptionCutover\"; INSERT INTO \"EntryEncryptionCutover\" SELECT 1, now(), " + Fingerprint(), connection, transaction);
        }
        await transaction.CommitAsync();
        Console.WriteLine($"verified={processed}; legacy_schema={legacy}");
    }
    Console.WriteLine($"completed_utc={DateTime.UtcNow:O}");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Migration stopped safely ({exception.GetType().Name}; entry={activeEntry?.ToString() ?? "none"}); no secret or content details logged.");
    Environment.ExitCode = 1;
}

// Only ciphertext and public entry metadata are fingerprinted; no plaintext digests.
static string Fingerprint() => "md5(COALESCE((SELECT string_agg(md5(jsonb_build_array(\"Id\", \"UserId\", \"OccurredAt\", \"CreatedAt\", \"UpdatedAt\", \"EncryptedNote\", \"EncryptedEmotions\")::text), '' ORDER BY \"Id\") FROM \"EmotionEntries\"), ''))";

static void Verify(EntryEncryption encryption, Row row, List<StoredEmotion>? original, bool compare)
{
    var emotions = encryption.Decrypt<List<StoredEmotion>>(row.Owner, row.Id, "emotions", row.Emotions!);
    var note = row.Note is null ? null : encryption.Decrypt<string>(row.Owner, row.Id, "note", row.Note);
    if (compare && (note != row.Comment || !emotions.OrderBy(e => e.EmotionId).SequenceEqual(original!.OrderBy(e => e.EmotionId))))
        throw new InvalidOperationException();
}

static async Task Execute(string sql, NpgsqlConnection connection, NpgsqlTransaction transaction)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    await command.ExecuteNonQueryAsync();
}

static async Task<List<StoredEmotion>> ReadEmotions(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id)
{
    await using var command = new NpgsqlCommand("SELECT \"EmotionId\", \"IsPrimary\" FROM \"EmotionEntryEmotions\" WHERE \"EmotionEntryId\" = @id ORDER BY \"EmotionId\"", connection, transaction);
    command.Parameters.AddWithValue("id", id);
    await using var reader = await command.ExecuteReaderAsync();
    var result = new List<StoredEmotion>();
    while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetBoolean(1)));
    return result;
}

static async Task<List<Row>> ReadRows(NpgsqlConnection connection, NpgsqlTransaction transaction, bool legacy, bool pendingOnly, int limit)
{
    var sql = "SELECT \"Id\", \"UserId\", " + (legacy ? "\"Comment\"" : "NULL::text") + ", \"EncryptedNote\"::text, \"EncryptedEmotions\"::text FROM \"EmotionEntries\"" + (pendingOnly ? " WHERE \"EncryptedEmotions\" IS NULL" : "") + " ORDER BY \"Id\" LIMIT @limit";
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("limit", limit);
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<Row>();
    while (await reader.ReadAsync()) rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
    return rows;
}

record Row(Guid Id, Guid Owner, string? Comment, string? Note, string? Emotions);
