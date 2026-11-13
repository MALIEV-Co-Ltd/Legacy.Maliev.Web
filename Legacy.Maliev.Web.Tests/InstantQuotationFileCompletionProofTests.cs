using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationFileCompletionProofTests
{
    [Fact]
    public void PersistedLinkedFiles_MustMatchEveryDistinctRequestedFileAndHash()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var sha = new string('a', 64);
        var reference = new InstantQuotationUploadReference(id.ToString("D"));
        var claim = new InstantQuotationGeometryClaim(1, sha, 8, 8, 8, 512, 384,
            Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
            12, 1, true, false, false, 1);
        var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
            InstantQuotationUploadResult.Succeeded("synthetic", reference, sha), claim)!;
        var part = new InstantQuotationPart(Guid.NewGuid(), "synthetic.stl", reference, geometry,
            new InstantQuotationPartConfiguration("PLA", "Black", 1));
        var now = DateTimeOffset.UnixEpoch;
        var session = new InstantQuotationSessionState("session", "submission", new([part]), now, now);
        var file = new InstantQuotationFinalizedFile(id, "bucket", "object", "synthetic.stl", "model/stl", 1, sha);
        var checkpoint = new InstantQuotationSubmissionCheckpoint("submission", 42,
            InstantQuotationSubmissionCheckpointStatus.Completed, "snapshot", [file]);

        Assert.True(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session, checkpoint));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session, checkpoint with { FinalizedFiles = null }));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session, checkpoint with { FinalizedFiles = [] }));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session, checkpoint with { Status = InstantQuotationSubmissionCheckpointStatus.Persisted }));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session, checkpoint with { FinalizedFiles = [file with { Sha256 = new string('b', 64) }] }));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session, checkpoint with { FinalizedFiles = [file with { FileId = Guid.NewGuid() }] }));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session with { RequestState = new([part, part]) }, checkpoint with { FinalizedFiles = [file, file] }));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session with { RequestState = new([]) }, checkpoint with { FinalizedFiles = [] }));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session with { RequestState = new([null!]) }, checkpoint));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session with { RequestState = new([part with { UploadReference = null! }]) }, checkpoint));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session with { RequestState = new([part with { Geometry = null! }]) }, checkpoint));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session, checkpoint with { FinalizedFiles = [null!] }));
        Assert.False(InstantQuotationSubmissionService.HasVerifiedLinkedFiles(session, checkpoint with { FinalizedFiles = [file with { Sha256 = new string('x', 64) }] }));
    }
}
