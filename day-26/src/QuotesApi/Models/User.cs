namespace QuotesApi.Models;

public class User
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Consecutive failed login attempts since the last successful login
    /// (or the last lockout). Reset to 0 on a successful login. Drives the
    /// brute-force lockout in <see cref="LockoutEnd"/> - see
    /// AuthSecurityOptions.MaxFailedLoginAttempts.
    /// </summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>
    /// When set and in the future, login is rejected regardless of whether
    /// the password is correct - the account is temporarily locked out
    /// after too many failed attempts. Null means not locked out.
    /// </summary>
    public DateTimeOffset? LockoutEnd { get; set; }
}
