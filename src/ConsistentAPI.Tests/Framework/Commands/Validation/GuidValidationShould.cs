using System.Net;

namespace ConsistentAPI.Tests.Framework.Commands.Validation;

public class GuidValidationShould
{
  private const string Url = "/commands/guid-validation-command";

  [Theory(DisplayName = "reject an invalid required guid as a validation error")]
  [InlineData("00000000-0000-0000-0000-0000000000")]
  [InlineData("00000001")]
  [InlineData("")]
  [InlineData(" ")]
  [InlineData(null)]
  public async Task Test1(string? id)
  {
    await using var setup = await Initializer.Do();
    var response = await setup.RawPost(Url, GetJson(id));
    Assert.Equal((int)HttpStatusCode.BadRequest, response.StatusCode);
    var errorResponse = await response.GetJsonAsync<ErrorResponse>();
    Assert.Equal("Invalid request", errorResponse.Message);
    Assert.Contains("Failed to deserialize: id", errorResponse.Errors);
    return;

    string GetJson(string? forId) => $"{{\"id\":\"{forId}\"}}";
  }

  [Theory(DisplayName = "reject an invalid nullable guid as a validation error")]
  [InlineData("000000-0000-0000-0000-000000000")]
  [InlineData("00000001")]
  [InlineData(" ")]
  [InlineData("123e4567-e89b-12d3-a44174000")]
  public async Task Test2(string id)
  {
    await using var setup = await Initializer.Do();
    var response = await setup.RawPost(Url, GetJson(id));
    Assert.Equal((int)HttpStatusCode.BadRequest, response.StatusCode);
    var errorResponse = await response.GetJsonAsync<ErrorResponse>();
    Assert.Equal("Invalid request", errorResponse.Message);
    Assert.Contains("Failed to deserialize: nullableId", errorResponse.Errors);
    return;

    string GetJson(string forId) => $"{{\"id\":\"{Guid.NewGuid()}\",\"nullableId\":\"{forId}\"}}";
  }
}
