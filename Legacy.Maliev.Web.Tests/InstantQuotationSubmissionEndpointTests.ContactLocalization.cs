using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationSubmissionEndpointTests
{
    [Theory]
    [InlineData("en", "Email", "Please enter your email address.")]
    [InlineData("th", "Email", "กรุณาระบุอีเมล")]
    [InlineData("en", "FirstName", "Please enter your first name.")]
    [InlineData("th", "FirstName", "กรุณาระบุชื่อ")]
    [InlineData("en", "LastName", "Please enter your last name.")]
    [InlineData("th", "LastName", "กรุณาระบุนามสกุล")]
    [InlineData("en", "Mobile", "Please enter your mobile number.")]
    [InlineData("th", "Mobile", "กรุณาระบุหมายเลขโทรศัพท์มือถือ")]
    public async Task InvalidAnonymousContact_PostReturnsLocalizedRequiredErrorWithoutSubmission(
        string culture, string field, string expectedMessage)
    {
        var service = new RecordingSubmissionService(Completed(717));
        await using var application = CreateFactory(service);
        using var client = CreateClient(application);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var token = await GetAntiforgeryTokenAsync(application.Services, client);
        using var response = await client.PostAsync(
            SubmitRoute + "&culture=" + culture,
            CustomerForm(new() { ["__RequestVerificationToken"] = token, [field] = string.Empty }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload = document.RootElement;
        Assert.Equal(new[] { "errors", "invalidFields", "outcome" },
            payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("retry", payload.GetProperty("outcome").GetString());
        Assert.Equal(field, Assert.Single(payload.GetProperty("invalidFields").EnumerateArray()).GetString());
        Assert.Equal(expectedMessage, Assert.Single(payload.GetProperty("errors").EnumerateArray()).GetString());
        Assert.Empty(service.Calls);
    }
}
