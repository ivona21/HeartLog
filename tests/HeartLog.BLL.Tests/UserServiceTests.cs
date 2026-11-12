using System.Security.Claims;
using HeartLog.BLL.Exceptions;
using HeartLog.BLL.Interfaces;
using HeartLog.BLL.Models.Auth;
using HeartLog.DAL.Interfaces;
using HeartLog.DAL.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeartLog.BLL.Tests;

public sealed class UserServiceTests
{
    [Fact]
    public async Task Login_RevalidatesUnderLockBeforeProvisioning_AndLinksLegacyEmailOnly()
    {
        var repository = new Users();
        var provider = new Provider();
        var service = Service(repository, provider);
        provider.Validate = () => Assert.True(repository.Locked);
        await service.LoginUserAsync("synthetic@example.invalid", "password");
        Assert.Equal(provider.Session.User.ProviderUserId, Assert.Single(repository.Rows).SupabaseUserId);
        Assert.Equal(1, repository.Saves);
        await service.LoginUserAsync("synthetic@example.invalid", "password");
        Assert.Single(repository.Rows);
        Assert.Equal(1, repository.Saves);
        repository.Rows[0].SupabaseUserId = null;
        await service.LoginUserAsync("synthetic@example.invalid", "password");
        Assert.Equal(provider.Session.User.ProviderUserId, repository.Rows[0].SupabaseUserId);
        Assert.Equal(2, repository.Saves);
    }

    [Fact]
    public async Task Login_InvalidatedProviderSession_DoesNotRecreateLocalUser()
    {
        var repository = new Users();
        var provider = new Provider { Validate = () => throw new UnauthorizedAccessException() };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(repository, provider).LoginUserAsync("synthetic@example.invalid", "password"));
        Assert.Empty(repository.Rows);
        Assert.Equal(0, repository.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_RejectsPendingDeletionOrEmailLinkedToDifferentProvider(bool pending)
    {
        var repository = new Users();
        var provider = new Provider();
        var user = new User { Email = provider.Session.User.Email,
            SupabaseUserId = pending ? provider.Session.User.ProviderUserId : Guid.NewGuid(),
            DeletionRequestedAt = pending ? DateTime.UtcNow : null };
        repository.Rows.Add(user);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(repository, provider).LoginUserAsync(user.Email, "password"));
        Assert.Same(user, Assert.Single(repository.Rows));
        Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task Refresh_RequiresExistingActiveLocalUser_AndNeverProvisions()
    {
        var repository = new Users();
        var provider = new Provider();
        var service = Service(repository, provider);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RefreshSessionAsync("refresh"));
        Assert.Empty(repository.Rows);
        var user = new User { Email = provider.Session.User.Email, SupabaseUserId = provider.Session.User.ProviderUserId };
        repository.Rows.Add(user);
        Assert.Same(provider.Session, await service.RefreshSessionAsync("refresh"));
        user.DeletionRequestedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RefreshSessionAsync("refresh"));
        Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task DeletionRequest_ResolvesIdentityUnderLock_VerifiesPassword_AndIsIdempotent()
    {
        var repository = new Users();
        var provider = new Provider();
        var service = Service(repository, provider);
        var id = provider.Session.User.ProviderUserId;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAccountDeletionAsync(id, "password"));
        var user = new User { SupabaseUserId = id, Email = provider.Session.User.Email };
        repository.Rows.Add(user);
        provider.LoginError = new ExternalAuthenticationException(ExternalAuthenticationFailureReason.InvalidCredentials, "synthetic rejection");
        await Assert.ThrowsAsync<CurrentPasswordIncorrectException>(() => service.RequestAccountDeletionAsync(id, "wrong"));
        Assert.Null(user.DeletionRequestedAt);
        provider.LoginError = null;
        await service.RequestAccountDeletionAsync(id, "password");
        Assert.NotNull(user.DeletionRequestedAt);
        Assert.Equal(1, repository.Requests);
        provider.LoginError = new Exception("A repeated pending request must not call the provider");
        await service.RequestAccountDeletionAsync(id, "password");
        Assert.Equal(1, repository.Requests);
    }

    [Fact]
    public async Task DeletionProcessor_RecordsProviderFailure_CompletesSuccess_AndPropagatesCancellation()
    {
        var repository = new Users();
        var provider = new Provider();
        repository.Deletion = new AccountDeletion { SupabaseUserId = provider.Session.User.ProviderUserId, NextAttemptAt = DateTime.UtcNow.AddMinutes(-1) };
        var service = Service(repository, provider);
        provider.DeleteError = new ExternalAuthException("Synthetic outage");
        await service.ProcessAccountDeletionsAsync(CancellationToken.None);
        Assert.Equal(1, repository.Failures);
        Assert.Equal(0, repository.Completions);
        provider.DeleteError = null;
        await service.ProcessAccountDeletionsAsync(CancellationToken.None);
        Assert.Equal(1, repository.Completions);
        provider.DeleteError = new OperationCanceledException();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ProcessAccountDeletionsAsync(canceled.Token));
        Assert.Equal(1, repository.Failures);
    }

    [Fact]
    public async Task CurrentUser_RejectsMissingMalformedUnknownAndPendingSubjects()
    {
        var repository = new Users();
        var service = new CurrentUserService(repository, NullLogger<CurrentUserService>.Instance);
        foreach (var subject in new[] { "", "malformed", Guid.NewGuid().ToString() })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetCurrentUserAsync(Principal(subject)));
        var id = Guid.NewGuid();
        var user = new User { Email = "synthetic@example.invalid", SupabaseUserId = id };
        repository.Rows.Add(user);
        Assert.Same(user, await service.GetCurrentUserAsync(Principal(id.ToString())));
        user.DeletionRequestedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetCurrentUserAsync(Principal(id.ToString())));
    }

    private static ClaimsPrincipal Principal(string subject) => new(new ClaimsIdentity([new Claim("sub", subject)], "synthetic"));
    private static UserService Service(Users users, Provider provider) => new(users, NullLogger<UserService>.Instance, provider);

    private sealed class Users : IUserRepository
    {
        public List<User> Rows { get; } = [];
        public bool Locked;
        public int Saves, Requests, Failures, Completions;
        public AccountDeletion? Deletion;
        public async Task WithProviderLockAsync(Guid id, Func<Task> action, CancellationToken cancellationToken = default)
        { Assert.False(Locked); Locked = true; try { await action(); } finally { Locked = false; } }
        public Task<User?> GetBySupabaseUserIdAsync(Guid id) => Task.FromResult(Rows.SingleOrDefault(u => u.SupabaseUserId == id));
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Rows.SingleOrDefault(u => u.Email == email));
        public Task<User?> GetByUsername(string username) => throw new NotSupportedException();
        public Task AddUserAsync(User user) { Assert.True(Locked); Rows.Add(user); return Task.CompletedTask; }
        public Task SaveChangesAsync() { Saves++; return Task.CompletedTask; }
        public Task RequestDeletionAsync(User user, CancellationToken cancellationToken = default)
        { Assert.True(Locked); Requests++; user.DeletionRequestedAt = DateTime.UtcNow; return Task.CompletedTask; }
        public Task<IReadOnlyList<Guid>> GetDueDeletionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(Deletion is null ? [] : [Deletion.SupabaseUserId]);
        public Task<AccountDeletion?> GetDeletionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Deletion);
        public Task CompleteDeletionAsync(AccountDeletion deletion, CancellationToken cancellationToken) { Assert.True(Locked); Completions++; return Task.CompletedTask; }
        public Task RecordDeletionFailureAsync(AccountDeletion deletion, CancellationToken cancellationToken) { Failures++; return Task.CompletedTask; }
    }

    private sealed class Provider : IExternalAuthService
    {
        public ExternalAuthSession Session { get; } = new() { AccessToken = "access", RefreshToken = "refresh",
            User = new ExternalAuthUser { ProviderUserId = Guid.NewGuid(), Email = "synthetic@example.invalid" } };
        public Action Validate = () => { };
        public Exception? LoginError, DeleteError;
        public Task<ExternalAuthSession> LoginAsync(string email, string password) => LoginError is null ? Task.FromResult(Session) : Task.FromException<ExternalAuthSession>(LoginError);
        public Task<ExternalAuthSession> RefreshAsync(string token) => Task.FromResult(Session);
        public Task ValidateSessionAsync(string token, CancellationToken cancellationToken = default) { Validate(); return Task.CompletedTask; }
        public Task DeleteUserAsync(Guid id, CancellationToken cancellationToken = default) => DeleteError is null ? Task.CompletedTask : Task.FromException(DeleteError);
        public Task TestConnectionAsync() => throw new NotSupportedException();
        public Task<ExternalAuthRegistrationResult> RegisterAsync(string email, string password) => throw new NotSupportedException();
        public Task<ExternalAuthEmailConfirmationResult> ConfirmEmailAsync(string hash, string type) => throw new NotSupportedException();
        public Task ResendConfirmationAsync(string email) => throw new NotSupportedException();
        public Task SendPasswordResetAsync(string email) => throw new NotSupportedException();
        public Task<ExternalAuthSession> ConfirmPasswordResetAsync(string hash, string type) => throw new NotSupportedException();
        public Task ResetPasswordAsync(string token, string password) => throw new NotSupportedException();
        public Task ChangePasswordAsync(string email, string token, string current, string password) => throw new NotSupportedException();
    }
}
