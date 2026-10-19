using System.Net.Http;
using System.Text;
using System.Text.Json;
using Maliev.JobService.Data.Database.JobContext;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace CareerOriginalMaterialization.Tests;

public sealed class OriginalMaterializationTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData(",\"offers\":null", -1)]
    [InlineData(",\"offers\":[]", 0)]
    [InlineData(",\"offers\":[{\"id\":9,\"levelId\":7,\"isFilled\":false}]", 1)]
    public async Task OriginalReadAsAsyncThenJsonResult_PreservesConstructorAndExplicitMemberSemantics(
        string nestedMember, int expectedCount)
    {
        // Original Jobs clears Level.Offers and omits nulls. Exercise incoming variants independently.
        var upstream = "[{\"id\":42,\"levelId\":7,\"isFilled\":false,\"level\":{\"id\":7" + nestedMember + "}}]";
        using var content = new StringContent(upstream, Encoding.UTF8, "application/json");
        var offers = await content.ReadAsAsync<List<Offer>>();
        var materialized = Assert.Single(offers);
        if (expectedCount < 0)
        {
            Assert.Null(materialized.Level.Offers);
        }
        else
        {
            Assert.Equal(expectedCount, materialized.Level.Offers.Count);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddNewtonsoftJson(options =>
        {
            // Exact original Web Startup settings; null handling remains the MVC default.
            options.SerializerSettings.Formatting = Formatting.Indented;
            options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        });
        await using var provider = services.BuildServiceProvider();
        await using var body = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = body;
        var context = new ActionContext(http, new RouteData(), new ActionDescriptor());
        await new JsonResult(offers.Where(offer => offer.IsFilled == false)).ExecuteResultAsync(context);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        var offer = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(12, offer.EnumerateObject().Count());
        Assert.Equal(42, offer.GetProperty("id").GetInt32());
        Assert.Equal(7, offer.GetProperty("levelId").GetInt32());
        Assert.False(offer.GetProperty("isFilled").GetBoolean());
        foreach (var name in new[] { "title", "introduction", "description", "prerequisites", "whatWeOffer", "location", "createdDate", "modifiedDate" })
        {
            Assert.Equal(JsonValueKind.Null, offer.GetProperty(name).ValueKind);
        }

        var level = offer.GetProperty("level");
        Assert.Equal(6, level.EnumerateObject().Count());
        Assert.Equal(7, level.GetProperty("id").GetInt32());
        foreach (var name in new[] { "name", "description", "createdDate", "modifiedDate" })
        {
            Assert.Equal(JsonValueKind.Null, level.GetProperty(name).ValueKind);
        }

        var collection = level.GetProperty("offers");
        if (expectedCount < 0)
        {
            Assert.Equal(JsonValueKind.Null, collection.ValueKind);
        }
        else
        {
            Assert.Equal(expectedCount, collection.GetArrayLength());
        }
    }
}
