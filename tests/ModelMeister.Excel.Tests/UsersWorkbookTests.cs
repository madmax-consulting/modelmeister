using Shouldly;
using ModelMeister.Excel;
using Xunit;

namespace ModelMeister.Excel.Tests;

public class UsersWorkbookTests
{
    [Fact]
    public void Round_trips_users_and_roles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mm-users-{Guid.NewGuid():N}.xlsx");
        try
        {
            var users = new List<UsersWorkbook.UserRow>
            {
                new() { Email = "alice@a.com", Roles = new() { "Editor", "Admin" }, GenerateApiKey = true },
                new() { Email = "bob@b.com",   Roles = new() { "Editor" }, Language = "sv" },
            };
            var availableRoles = new List<string> { "Editor", "Admin", "Reader" };
            UsersWorkbook.Save(users, availableRoles, path);

            var loaded = UsersWorkbook.Load(path);
            loaded.Count.ShouldBe(2);
            var alice = loaded.Single(u => u.Email == "alice@a.com");
            // Username is derived from Email — there is no separate Username column.
            alice.Username.ShouldBe("alice@a.com");
            alice.Roles.ShouldBe(new[] { "Editor", "Admin" });
            alice.GenerateApiKey.ShouldBeTrue();

            var bob = loaded.Single(u => u.Email == "bob@b.com");
            bob.Language.ShouldBe("sv");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
