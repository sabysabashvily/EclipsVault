using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Web.Controllers.Ui;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EclipsVault.Tests.Web;

/// <summary>Signing in writes audit rows, so only a stub can show the vault an empty trail.</summary>
public sealed class AuditCheckpointResponseTests
{
    [Fact]
    public async Task A_checkpoint_of_an_empty_trail_answers_nothing_to_checkpoint_in_the_Legacy_words()
    {
        var controller = new AuditController(null!, new EmptyTrail(), null!, null!);

        var refused = Assert.IsType<JsonResult>(await controller.CreateCheckpoint(CancellationToken.None));

        Assert.Equal(409, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.ContentType);
        var problem = Assert.IsType<AdministratorProblemResponse>(refused.Value);
        Assert.Equal(("nothing_to_checkpoint", "There are no audit entries to checkpoint yet."), (problem.Code, problem.Title));
    }

    private sealed class EmptyTrail : IAuditCheckpointService
    {
        public string SigningKeyId => "sk-test";

        public Task<AuditCheckpointDto?> CreateCheckpointAsync(CancellationToken ct) => Task.FromResult<AuditCheckpointDto?>(null);

        public Task<AuditIntegrityReport> VerifyAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<AuditCheckpointDto?> GetLatestAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<AuditBundleExport> ExportAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
