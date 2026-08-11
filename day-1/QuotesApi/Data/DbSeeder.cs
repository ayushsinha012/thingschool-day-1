using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var user = new User
        {
            Email = "ayush.test@example.com",

            PasswordHash =
                BCrypt.Net.BCrypt.HashPassword(
                    "TestPassword123!")
        };

        db.Users.Add(user);

        await db.SaveChangesAsync(cancellationToken);
    }
}
