using System.Text.Json;
using ModelMeister.Rest;
using Shouldly;
using Xunit;

namespace ModelMeister.Inriver.Tests;

/// <summary>
/// Pins the wire format of the user-provision request body. The inriver endpoint
/// (POST /api/v1.0.0/system/users:provision) is strict: it rejects unknown fields (e.g. Company)
/// and requires camelCase keys plus a segmentRoles array shaped as { segmentId, roleNames }.
/// </summary>
public class UserCreatePayloadTests
{
    [Fact]
    public void Serializes_with_roles_as_a_single_segment_zero_entry()
    {
        var body = new UserCreate
        {
            Username = "maxtest@tetrapak.com",
            Email = "maxtest@tetrapak.com",
            FirstName = "Max",
            LastName = "Test",
            SegmentRoles = { new SegmentRole { SegmentId = 0, RoleNames = { "RestrictedUser" } } },
        };

        var json = JsonSerializer.Serialize(body, InriverRestClient.JsonOptions);

        json.ShouldBe(
            """{"username":"maxtest@tetrapak.com","email":"maxtest@tetrapak.com","firstName":"Max","lastName":"Test","segmentRoles":[{"segmentId":0,"roleNames":["RestrictedUser"]}]}""");
        // No stray fields the endpoint rejects.
        json.ShouldNotContain("company", Case.Insensitive);
        json.ShouldNotContain("language", Case.Insensitive);
    }

    [Fact]
    public void Serializes_an_empty_segmentRoles_array_when_no_roles()
    {
        var body = new UserCreate { Username = "a@b.com", Email = "a@b.com" };

        var json = JsonSerializer.Serialize(body, InriverRestClient.JsonOptions);

        json.ShouldContain("\"segmentRoles\":[]");
    }
}
