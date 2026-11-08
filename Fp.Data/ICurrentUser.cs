namespace Fp.Data;

/// <summary>
/// Tells the audit interceptor who is making the change.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Username, e-mail or object id of the current user, or <c>null</c> when there is none.
    /// </summary>
    string? UserId { get; }
}

/// <summary>
/// Default <see cref="ICurrentUser"/> without a user; changes are attributed to <see cref="AuditSaveChangesInterceptor.SystemUser"/>.
/// </summary>
public sealed class NoCurrentUser : ICurrentUser
{
    public string? UserId => null;
}
