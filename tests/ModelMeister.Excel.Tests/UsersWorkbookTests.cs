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
                new() { Username = "alice", Email = "a@a", Roles = new() { "Editor", "Admin" }, GenerateApiKey = true, Notes = "Lead" },
                new() { Username = "bob",   Email = "b@b", Roles = new() { "Editor" }, Language = "sv", Company = "Acme" },
            };
            var availableRoles = new List<string> { "Editor", "Admin", "Reader" };
            UsersWorkbook.Save(users, availableRoles, path);

            var loaded = UsersWorkbook.Load(path);
            loaded.Count.ShouldBe(2);
            var alice = loaded.Single(u => u.Username == "alice");
            alice.Email.ShouldBe("a@a");
            alice.Roles.ShouldBe(new[] { "Editor", "Admin" });
            alice.GenerateApiKey.ShouldBeTrue();
            alice.Notes.ShouldBe("Lead");

            var bob = loaded.Single(u => u.Username == "bob");
            bob.Language.ShouldBe("sv");
            bob.Company.ShouldBe("Acme");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
