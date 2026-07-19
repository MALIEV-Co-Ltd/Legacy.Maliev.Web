using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationUploadClientTests
{
    [Fact]
    public void Contract_UsesServerSessionOpaqueReferencesStableOperationsAndCancellation()
    {
        var methods = typeof(IInstantQuotationUploadClient).GetMethods().OrderBy(method => method.Name).ToArray();

        Assert.Equal(["FinalizeAsync", "RemoveAsync", "UploadAsync"], methods.Select(method => method.Name));
        Assert.All(methods, method => Assert.Equal(typeof(Task<>), method.ReturnType.GetGenericTypeDefinition()));
        Assert.All(methods, method => Assert.Equal(typeof(string), method.GetParameters()[0].ParameterType));
        Assert.All(methods, method => Assert.Contains(method.GetParameters(), parameter => parameter.Name == "operationId"));
        Assert.All(methods, method => Assert.Equal(typeof(CancellationToken), method.GetParameters().Last().ParameterType));
        Assert.Contains(typeof(IInstantQuotationUploadClient).GetMethod("UploadAsync")!.GetParameters(), parameter => parameter.ParameterType == typeof(Stream));
        Assert.Contains(typeof(IInstantQuotationUploadClient).GetMethod("RemoveAsync")!.GetParameters(), parameter => parameter.ParameterType == typeof(InstantQuotationUploadReference));
        var finalizationParameters = typeof(IInstantQuotationUploadClient).GetMethod("FinalizeAsync")!.GetParameters();
        var quotationRequestId = Assert.Single(
            finalizationParameters,
            parameter => parameter.Name == "quotationRequestId");
        Assert.Equal(typeof(int), quotationRequestId.ParameterType);
    }

    [Fact]
    public void PublicResults_AreOpaqueAndDoNotExposeStorageLocationsOrCredentials()
    {
        var publicProperties = new[]
        {
            typeof(InstantQuotationUploadReference),
            typeof(InstantQuotationUploadResult),
            typeof(InstantQuotationRemoveResult),
            typeof(InstantQuotationFinalizationResult),
        }.SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)).ToArray();
        var forbidden = new[]
        {
            "Bucket", "Object", "Path", "Uri", "Url", "Credential", "Token", "Cookie", "Secret", "AuthorizationHeader",
        };

        Assert.DoesNotContain(
            publicProperties,
            property => forbidden.Any(fragment => property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(publicProperties, property => property.Name == "ServiceStatus");
        Assert.Contains(publicProperties, property => property.Name == "AuthorizationStatus");
        Assert.Contains(publicProperties, property => property.Name == "Status");
        Assert.Contains(publicProperties, property => property.Name == "ProblemCategory");
        Assert.Contains(publicProperties, property => property.Name == "OperationId");
        Assert.Equal([typeof(string)], typeof(InstantQuotationUploadReference).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task UnavailableClient_AllOperationsFailClosedWithTypedUnavailableResults()
    {
        var client = new UnavailableInstantQuotationUploadClient();
        using var stream = new MemoryStream([1, 2, 3]);

        var upload = await client.UploadAsync("session", stream, "part.stl", "model/stl", 3, "upload-op", default);
        var remove = await client.RemoveAsync("session", new InstantQuotationUploadReference("opaque"), "remove-op", default);
        var finalize = await client.FinalizeAsync("session", 417, [new InstantQuotationUploadReference("opaque")], "finalize-op", default);

        AssertUnavailable(upload.ServiceStatus, upload.AuthorizationStatus, upload.Status, upload.ProblemCategory);
        AssertUnavailable(remove.ServiceStatus, remove.AuthorizationStatus, remove.Status, remove.ProblemCategory);
        AssertUnavailable(finalize.ServiceStatus, finalize.AuthorizationStatus, finalize.Status, finalize.ProblemCategory);
        Assert.Equal("upload-op", upload.OperationId);
        Assert.Equal("remove-op", remove.OperationId);
        Assert.Equal("finalize-op", finalize.OperationId);
        Assert.Null(upload.UploadReference);
        Assert.Null(upload.AuthoritativeGeometry);
        Assert.Equal(3, stream.Length);
    }

    [Fact]
    public async Task UnavailableClient_Cancellation_IsObservedWithoutIo()
    {
        var client = new UnavailableInstantQuotationUploadClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FinalizeAsync("session", 417, [], "operation", cancellation.Token));
    }

    [Fact]
    public void Registration_UsesOnlyUnavailableImplementationWithNoHttpDependency()
    {
        var services = new ServiceCollection();
        services.AddLegacyServiceClients(new ConfigurationBuilder().Build());

        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IInstantQuotationUploadClient));

        Assert.Equal(typeof(UnavailableInstantQuotationUploadClient), descriptor.ImplementationType);
        Assert.Empty(typeof(UnavailableInstantQuotationUploadClient).GetConstructors().Single().GetParameters());
    }

    [Fact]
    public void FileServiceTransport_IsInternalAndKeptBehindUnavailableRuntimeAdapter()
    {
        var transport = typeof(UnavailableInstantQuotationUploadClient).Assembly.GetType(
            "Legacy.Maliev.Web.Infrastructure.InstantQuotationFileServiceTransport");

        Assert.NotNull(transport);
        Assert.False(transport.IsPublic);

        var methods = transport.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .ToDictionary(method => method.Name, StringComparer.Ordinal);
        Assert.Equal(
            ["CreateSessionAsync", "FinalizeAsync", "RemoveAsync", "UploadAsync"],
            methods.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["cancellationToken"],
            methods["CreateSessionAsync"].GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(
            ["capability", "upload", "operationId", "cancellationToken"],
            methods["UploadAsync"].GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(
            ["capability", "fileId", "cancellationToken"],
            methods["RemoveAsync"].GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(
            ["capability", "quotationRequestId", "fileIds", "operationId", "cancellationToken"],
            methods["FinalizeAsync"].GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(typeof(int), methods["FinalizeAsync"].GetParameters()[1].ParameterType);

        var services = new ServiceCollection();
        services.AddLegacyServiceClients(new ConfigurationBuilder().Build());
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == transport);
    }

    [Fact]
    public void GeometryProvenance_BrowserGeometryCannotConstructAuthoritativeType()
    {
        Assert.DoesNotContain(
            typeof(InstantQuotationGeometry).GetProperties(),
            property => property.Name.Contains("Authoritative", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(typeof(AuthoritativeInstantQuotationGeometry).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(typeof(InstantQuotationGeometry).GetMethod("ToAuthoritative", BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void GeometryProvenance_PublicApiCannotPromoteAdvisoryGeometry()
    {
        Assert.Null(
            typeof(InstantQuotationUploadResult).GetMethod(
                "Succeeded",
                BindingFlags.Static | BindingFlags.Public));
        Assert.Contains(
            typeof(InstantQuotationUploadResult).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>(),
            attribute => attribute.AssemblyName == "Legacy.Maliev.Web.Infrastructure");
    }

    [Fact]
    public void GeometryProvenance_JsonCannotConstructAuthoritativeGeometry()
    {
        var constructors = typeof(AuthoritativeInstantQuotationGeometry)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(
            constructors,
            constructor => constructor.GetCustomAttribute<JsonConstructorAttribute>() is not null);
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<AuthoritativeInstantQuotationGeometry>(
            """
            {
              "HeightMm": 30,
              "VolumeMm3": 20000,
              "FootprintMm2": 400,
              "AreaProfileMm2": [500],
              "PerimeterProfileMm": [80],
              "FacetCount": 1024,
              "BodyCount": 1,
              "IsManifold": true
            }
            """));
    }

    [Fact]
    public void SuccessfulPromotion_DeepCopiesProfilesAndCannotBeMutatedByCallers()
    {
        var areas = new[] { 500.0, 501.0 };
        var perimeters = new[] { 80.0, 81.0 };
        var upload = InstantQuotationUploadResult.Succeeded(
            "operation",
            new InstantQuotationUploadReference("opaque"),
            new InstantQuotationGeometry(30, 20_000, 400, areas, perimeters, 1_024, 1, true));

        areas[0] = -1;
        perimeters[0] = -1;

        var geometry = Assert.IsType<AuthoritativeInstantQuotationGeometry>(upload.AuthoritativeGeometry);
        Assert.Equal([500.0, 501.0], geometry.AreaProfileMm2);
        Assert.Equal([80.0, 81.0], geometry.PerimeterProfileMm);
        Assert.False(geometry.AreaProfileMm2 is IList<double>);
        Assert.False(geometry.PerimeterProfileMm is IList<double>);
        Assert.False(geometry.AreaProfileMm2 is double[]);
        Assert.False(geometry.PerimeterProfileMm is double[]);
    }

    private static void AssertUnavailable(
        InstantQuotationServiceStatus serviceStatus,
        InstantQuotationAuthorizationStatus authorizationStatus,
        InstantQuotationOperationStatus status,
        InstantQuotationProblemCategory problemCategory)
    {
        Assert.Equal(InstantQuotationServiceStatus.Unavailable, serviceStatus);
        Assert.Equal(InstantQuotationAuthorizationStatus.NotEvaluated, authorizationStatus);
        Assert.Equal(InstantQuotationOperationStatus.Failed, status);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, problemCategory);
    }
}
