using HeartLog.BLL.Models.Agreements;

if (args.Length != 4 || args[2] is not ("development" or "production"))
{
    Console.Error.WriteLine("Usage: HeartLog.LegalVerifier <selector> <legal-root> <development|production> <history-root>");
    return 2;
}
try
{
    var release = LegalReleaseLoader.Load(args[0], args[1], args[2] == "production");
    LegalReleaseLoader.VerifyHistoricalSnapshots(args[3]);
    Console.WriteLine($"Verified {release.Release} ({args[2]}).");
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Text.DecoderFallbackException or InvalidOperationException or KeyNotFoundException)
{
    Console.Error.WriteLine($"Legal release verification failed: {ex.Message}");
    return 1;
}
