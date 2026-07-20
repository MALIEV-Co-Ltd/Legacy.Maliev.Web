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
        Assert.All(methods, method =>
        {
            var owner = Assert.Single(method.GetParameters(), parameter => parameter.Name == "ownerIdentity");
            Assert.Equal(typeof(string), owner.ParameterType);
            Assert.Equal(
                System.Reflection.NullabilityState.Nullable,
                new NullabilityInfoContext().Create(owner).ReadState);
        });
        Assert.All(methods, method => Assert.Contains(method.GetParameters(), parameter => parameter.Name == "operationId"));
        Assert.All(methods, method => Assert.Equal(typeof(CancellationToken), method.GetParameters().Last().ParameterType));
        Assert.Contains(typeof(IInstantQuotationUploadClient).GetMethod("UploadAsync")!.GetParameters(), parameter => parameter.ParameterType == typeof(Stream));
        Assert.Contains(typeof(IInstantQuotationUploadClient).GetMethod("RemoveAsync")!.GetParameters(), parameter => parameter.ParameterType == typeof(InstantQuotationUploadReference));
        var finalizationParameters = typeof(IInstantQuotationUploadClient).GetMethod("FinalizeAsync")!.GetParameters();
        var quotationRequestId = Assert.Single(
            finalizationParameters,
            parameter => parameter.Name == "quotationRequestId");
        Assert.Equal(typeof(int), quotationRequestId.ParameterType);
        var uploadParameters = typeof(IInstantQuotationUploadClient).GetMethod("UploadAsync")!.GetParameters();
        Assert.Contains(uploadParameters, parameter => parameter.ParameterType == typeof(InstantQuotationGeometryClaim));
        Assert.True(Array.FindIndex(uploadParameters, parameter => parameter.Name == "geometryClaim")
            < Array.FindIndex(uploadParameters, parameter => parameter.Name == "operationId"));
    }

    [Fact]
    public void GeometryClaim_UsesVersionedExactByteDigestAndCompleteLegacyEvidence()
    {
        var claim = Claim();

        Assert.Equal(1, claim.Version);
        Assert.Equal(64, claim.Sha256.Length);
        Assert.Equal(10, claim.DimensionXmm);
        Assert.Equal(20, claim.DimensionYmm);
        Assert.Equal(30, claim.DimensionZmm);
        Assert.Equal(64, claim.AreaProfileMm2!.Count);
        Assert.All(claim.AreaProfileMm2, value => Assert.Equal(100.0, value));
        Assert.Equal(64, claim.PerimeterProfileMm!.Count);
        Assert.All(claim.PerimeterProfileMm, value => Assert.Equal(40.0, value));
        Assert.True(claim.TopologyChecked);
        Assert.False(claim.NonWatertight);
        Assert.False(claim.NonManifold);
        Assert.Equal(0.8, claim.MinThicknessMm);
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

        var upload = await client.UploadAsync("session", null, stream, "part.stl", "model/stl", 3, Claim(), "upload-op", default);
        var remove = await client.RemoveAsync("session", null, new InstantQuotationUploadReference("opaque"), "remove-op", default);
        var finalize = await client.FinalizeAsync("session", null, 417, [new InstantQuotationUploadReference("opaque")], "finalize-op", default);

        AssertUnavailable(upload.ServiceStatus, upload.AuthorizationStatus, upload.Status, upload.ProblemCategory);
        AssertUnavailable(remove.ServiceStatus, remove.AuthorizationStatus, remove.Status, remove.ProblemCategory);
        AssertUnavailable(finalize.ServiceStatus, finalize.AuthorizationStatus, finalize.Status, finalize.ProblemCategory);
        Assert.Equal("upload-op", upload.OperationId);
        Assert.Equal("remove-op", remove.OperationId);
        Assert.Equal("finalize-op", finalize.OperationId);
        Assert.Null(upload.UploadReference);
        Assert.Null(upload.ContentSha256);
        Assert.Equal(3, stream.Length);
    }

    [Fact]
    public async Task UnavailableClient_Cancellation_IsObservedWithoutIo()
    {
        var client = new UnavailableInstantQuotationUploadClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FinalizeAsync("session", null, 417, [], "operation", cancellation.Token));
    }

    [Fact]
    public void Registration_UsesReviewedFileServiceImplementation()
    {
        var services = new ServiceCollection();
        services.AddLegacyServiceClients(new ConfigurationBuilder().Build());

        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IInstantQuotationUploadClient));

        Assert.Equal(typeof(InstantQuotationFileServiceUploadClient), descriptor.ImplementationType);
        Assert.Single(typeof(InstantQuotationFileServiceUploadClient).GetConstructors());
    }

    [Fact]
    public void FileServiceTransport_IsInternalAndKeptBehindServerSideAdapter()
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
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == transport
                && descriptor.ImplementationType == transport
                && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void GeometryProvenance_LegacyClaimRequiresMatchingSuccessfulPersistedUpload()
    {
        var areas = Enumerable.Range(0, 64).Select(index => 100.0 - index).ToArray();
        var perimeters = Enumerable.Range(0, 64).Select(index => 40.0 - (index * 0.25)).ToArray();
        var claim = Claim() with { AreaProfileMm2 = areas, PerimeterProfileMm = perimeters };
        var unavailable = InstantQuotationUploadResult.Unavailable("failed-operation");
        var persisted = InstantQuotationUploadResult.Succeeded(
            "persisted-operation",
            new InstantQuotationUploadReference("opaque"),
            claim.Sha256);

        Assert.Null(AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(unavailable, claim));
        var admitted = Assert.IsType<AuthoritativeInstantQuotationGeometry>(
            AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(persisted, claim));

        areas[0] = -1;
        perimeters[0] = -1;
        Assert.Equal(30, admitted.HeightMm);
        Assert.Equal(200, admitted.FootprintMm2);
        Assert.Equal(100.0, admitted.AreaProfileMm2[0]);
        Assert.Equal(99.0, admitted.AreaProfileMm2[1]);
        Assert.Equal(40.0, admitted.PerimeterProfileMm[0]);
        Assert.Equal(39.75, admitted.PerimeterProfileMm[1]);
        Assert.Equal(2_200, admitted.SurfaceAreaMm2);
        Assert.Equal(0.8, admitted.MinThicknessMm);
        Assert.True(admitted.TopologyChecked);
        Assert.True(admitted.IsManifold);
        Assert.Empty(typeof(AuthoritativeInstantQuotationGeometry).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.DoesNotContain(
            typeof(InstantQuotationUploadResult).GetProperties(),
            property => property.Name.Contains("Geometry", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(InvalidGeometryClaims))]
    public void GeometryProvenance_InvalidOrUnboundClaimCannotBePromoted(InstantQuotationGeometryClaim claim)
    {
        var persisted = InstantQuotationUploadResult.Succeeded(
            "persisted-operation",
            new InstantQuotationUploadReference("opaque"),
            new string('a', 64));

        Assert.Null(AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(persisted, claim));
    }

    public static TheoryData<InstantQuotationGeometryClaim> InvalidGeometryClaims() => new()
    {
        Claim() with { Version = 2 },
        Claim() with { Sha256 = new string('A', 64) },
        Claim() with { Sha256 = new string('b', 64) },
        Claim() with { DimensionXmm = double.NaN },
        Claim() with { DimensionYmm = 0 },
        Claim() with { DimensionZmm = double.PositiveInfinity },
        Claim() with { DimensionXmm = 1e200, DimensionYmm = 1e-100, DimensionZmm = 1e300 },
        Claim() with { VolumeMm3 = -1 },
        Claim() with { SurfaceAreaMm2 = 0 },
        Claim() with { VolumeMm3 = 7_000 },
        Claim() with { AreaProfileMm2 = [] },
        Claim() with { AreaProfileMm2 = [100] },
        Claim() with { PerimeterProfileMm = [40] },
        Claim() with { FacetCount = 0 },
        Claim() with { BodyCount = 0 },
        Claim() with { BodyCount = 2_000 },
        Claim() with { TopologyChecked = false, BodyCount = 2 },
        Claim() with { TopologyChecked = false, NonWatertight = true },
        Claim() with { AreaProfileMm2 = null, PerimeterProfileMm = null },
        Claim() with { MinThicknessMm = -1 },
    };

    [Fact]
    public void GeometryProvenance_LegacyFallbackWithoutProfilesOrExpensiveTopologyCheckIsAdmittedForManualReview()
    {
        var claim = Claim() with
        {
            AreaProfileMm2 = null,
            PerimeterProfileMm = null,
            TopologyChecked = true,
            BodyCount = 1,
            NonWatertight = true,
            NonManifold = false,
            MinThicknessMm = 0,
        };
        var persisted = InstantQuotationUploadResult.Succeeded(
            "persisted-operation",
            new InstantQuotationUploadReference("opaque"),
            claim.Sha256);

        var admitted = Assert.IsType<AuthoritativeInstantQuotationGeometry>(
            AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(persisted, claim));

        Assert.Empty(admitted.AreaProfileMm2);
        Assert.Empty(admitted.PerimeterProfileMm);
        Assert.True(admitted.TopologyChecked);
        Assert.False(admitted.IsManifold);
    }

    [Fact]
    public void GeometryProvenance_LargeMeshUsesExactTwentyFourProfilesAndConservativeTopologyState()
    {
        var claim = Claim() with
        {
            AreaProfileMm2 = Enumerable.Repeat(100.0, 24).ToArray(),
            PerimeterProfileMm = Enumerable.Repeat(40.0, 24).ToArray(),
            FacetCount = 250_001,
            TopologyChecked = false,
        };
        var persisted = InstantQuotationUploadResult.Succeeded(
            "persisted-operation",
            new InstantQuotationUploadReference("opaque"),
            claim.Sha256);

        var admitted = Assert.IsType<AuthoritativeInstantQuotationGeometry>(
            AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(persisted, claim));

        Assert.Equal(24, admitted.AreaProfileMm2.Count);
        Assert.False(admitted.TopologyChecked);
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

    private static InstantQuotationGeometryClaim Claim() => new(
        Version: 1,
        Sha256: new string('a', 64),
        DimensionXmm: 10,
        DimensionYmm: 20,
        DimensionZmm: 30,
        VolumeMm3: 1_000,
        SurfaceAreaMm2: 2_200,
        AreaProfileMm2: Enumerable.Repeat(100.0, 64).ToArray(),
        PerimeterProfileMm: Enumerable.Repeat(40.0, 64).ToArray(),
        FacetCount: 1_024,
        BodyCount: 1,
        TopologyChecked: true,
        NonWatertight: false,
        NonManifold: false,
        MinThicknessMm: 0.8);
}
