using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Services.Encryption;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;

namespace HeartLog.BLL.Tests;

public sealed class EmotionEntryServiceTests
{
    public static TheoryData<string[], string, string> InvalidSelections => new()
    {
        { [], "joy", "At least one emotion must be selected." },
        { [" "], "joy", "Emotion keys cannot be empty." },
        { ["joy", " joy "], "joy", "Duplicate emotion keys are not allowed." },
        { ["joy"], " ", "A primary emotion key is required." },
        { ["joy"], "calm", "Primary emotion key must be one of the selected emotions." },
        { ["unknown"], "unknown", "One or more selected emotions do not exist or are inactive." },
        { ["inactive"], "inactive", "One or more selected emotions do not exist or are inactive." },
        { ["JOY"], "JOY", "One or more selected emotions do not exist or are inactive." }
    };

    [Theory]
    [MemberData(nameof(InvalidSelections))]
    public async Task InvalidSelection_DoesNotWrite_OnCreateOrUpdate(string[] keys, string primary, string message)
    {
        var repository = new Entries();
        var service = Service(repository);
        var owner = Guid.NewGuid();
        var original = await service.CreateEmotionEntryAsync(owner, ["joy"], "joy", "original", null);
        var ciphertext = repository.Row!.EncryptedEmotions;
        foreach (var update in new[] { false, true })
        {
            var error = await Assert.ThrowsAsync<InvalidEmotionEntryException>(() => update
                ? service.UpdateEmotionEntryAsync(owner, original.EntryId, keys, primary, "changed", null)
                : service.CreateEmotionEntryAsync(owner, keys, primary, "changed", null));
            Assert.Equal(message, error.Message);
            Assert.Equal(1, repository.Saves);
            Assert.Same(ciphertext, repository.Row.EncryptedEmotions);
            Assert.Equal("original", Assert.Single(await service.GetAllByUserAsync(owner)).Comment);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Synthetic čćž\nnotes")]
    public async Task Notes_AreOptionalAndPreserved_AndKeysAreTrimmed(string? note)
    {
        var repository = new Entries();
        var service = Service(repository);
        var owner = Guid.NewGuid();
        var before = DateTime.UtcNow;
        var created = await service.CreateEmotionEntryAsync(owner, [" joy "], " joy ", note, null);
        Assert.Equal(owner, repository.Row!.UserId);
        Assert.InRange(created.OccurredAt, before, DateTime.UtcNow);
        Assert.Equal("joy", Assert.Single(created.SelectedEmotions).EmotionKey);
        Assert.Equal(note, Assert.Single(await service.GetAllByUserAsync(owner)).Comment);
        var updated = await service.UpdateEmotionEntryAsync(owner, created.EntryId, ["joy"], "joy", null, null);
        Assert.Null(updated.Comment);
        Assert.Equal(created.OccurredAt, updated.OccurredAt);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
    }

    [Fact]
    public async Task MissingEntry_AndEmptyIdentity_DoNotWrite()
    {
        var repository = new Entries();
        var service = Service(repository);
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<EmotionEntryNotFoundException>(() => service.UpdateEmotionEntryAsync(owner, id, ["joy"], "joy", null, null));
        await Assert.ThrowsAsync<EmotionEntryNotFoundException>(() => service.DeleteEmotionEntryAsync(owner, id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateEmotionEntryAsync(Guid.Empty, ["joy"], "joy", null, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateEmotionEntryAsync(Guid.Empty, id, ["joy"], "joy", null, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteEmotionEntryAsync(Guid.Empty, id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAllByUserAsync(Guid.Empty));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetSummaryAsync(Guid.Empty));
        Assert.Equal(0, repository.Saves);
    }

    private static EmotionEntryService Service(Entries entries) => new(new Emotions(), entries,
        new EntryEncryption("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="));

    private sealed class Emotions : IEmotionsRepository
    {
        private readonly Emotion _joy = new() { Id = Guid.NewGuid(), Key = "joy" };
        private readonly Emotion _inactive = new() { Id = Guid.NewGuid(), Key = "inactive", IsActive = false };
        public Task<List<Emotion>> GetActiveByKeysAsync(IEnumerable<string> keys) => Task.FromResult(new[] { _joy, _inactive }.Where(e => e.IsActive && keys.Contains(e.Key)).ToList());
        public Task<List<Emotion>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) => Task.FromResult(new List<Emotion> { _joy });
        public Task<List<Emotion>> GetAllWithTranslationsAsync(string locale) => throw new NotSupportedException();
    }

    private sealed class Entries : IEmotionEntriesRepository
    {
        public EmotionEntry? Row;
        public int Saves;
        public Task AddAsync(EmotionEntry entry, CancellationToken cancellationToken = default) { Row = entry; return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) { Saves++; return Task.CompletedTask; }
        public Task<EmotionEntry?> GetByIdAndUserIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(Row?.Id == id && Row.UserId == userId ? Row : null);
        public Task<List<EmotionEntry>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(Row is not null && Row.UserId == userId ? new List<EmotionEntry> { Row } : []);
        public Task<bool> DeleteByIdAndUserIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int> CountByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DateTime?> GetLatestOccurredAtByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
