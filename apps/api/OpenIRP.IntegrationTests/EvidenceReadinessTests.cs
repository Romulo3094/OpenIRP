using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Incidents;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private Task<ExecutionEvidenceReadiness> Support(ExecutionFixture f, ExecutionItem run, Guid? actor = null) =>
        Executions(s => s.EvidenceReadinessAsync(actor ?? f.Admin, f.Org, f.Incident.Id, run.Id));

    private static async Task<ExecutionEvidenceReadiness> RuntimeSupport(WebApplicationFactory<Program> runtime, ExecutionFixture f, ExecutionItem run)
    { using var scope = runtime.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<IExecutionService>().EvidenceReadinessAsync(f.Admin, f.Org, f.Incident.Id, run.Id); }

    [Fact, Trait("Feature", "EvidenceReadiness")]
    public async Task UnselectedEvidenceNeverSatisfiesCompletionOrCreatesApprovalOrWrites()
    {
        var f = await ExecutionSeed(approvals: true, approvalEvidence: true); var e = await BeginExecution(f);
        e = await AssignTask(f, e, e.Tasks[0].Id); await AddEvidence(f, e);
        e = await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, e.Id));
        var audits = await InScope((_, db) => db.AuditEvents.CountAsync()); var events = await InScope((_, db) => db.IncidentEvents.CountAsync());
        var r = await Support(f, e); var task = r.Tasks.Single(t => t.TaskId == e.Tasks[0].Id);
        Assert.Equal(TaskEvidenceSupportState.AwaitingCompletion, task.SupportState); Assert.Null(task.CompletionEvidenceId); Assert.Null(task.EvidenceKind);
        Assert.Equal(RequiredEvidenceCoverage.NeedsReview, r.RequiredCoverage); Assert.True(r.RequiresHumanReview); Assert.False(r.FileIntegrityVerified); Assert.False(r.TechnicalSufficiencyVerified);
        Assert.Equal(e.Revision, r.ExecutionRevision); Assert.Equal(e.IncidentRevision, r.IncidentRevision);
        Assert.Equal((await ClosureRead(f.Admin, f.Org, f.Incident.Id)).RiskReview.ContextToken, r.RiskContextToken);
        Assert.Equal(audits, await InScope((_, db) => db.AuditEvents.CountAsync())); Assert.Equal(events, await InScope((_, db) => db.IncidentEvents.CountAsync()));
        await Rejected(409, () => TaskState(f, e, e.Tasks[0].Id, ExecutionTaskState.InProgress), "task_approval_required");
    }

    [Theory, Trait("Feature", "EvidenceReadiness")]
    [InlineData(EvidenceKind.Text)] [InlineData(EvidenceKind.Reference)]
    public async Task SelectedTaskEvidenceIsMetadataAvailableAndWithdrawalCreatesCurrentGapWithoutRewritingTask(EvidenceKind kind)
    {
        var (f, e) = await EvidenceSetup(true); var row = await AddEvidence(f, e, kind);
        e = await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, e.Id)); e = await CompleteWithEvidence(f, e, row.Evidence.Id);
        var before = await Support(f, e); var item = before.Tasks.Single(t => t.TaskId == e.Tasks[0].Id);
        Assert.Equal(TaskEvidenceSupportState.MetadataAvailable, item.SupportState); Assert.Equal(row.Evidence.Id, item.CompletionEvidenceId);
        Assert.Equal(kind.ToString().ToUpperInvariant(), item.EvidenceKind); Assert.Equal(1, item.EvidenceRevision); Assert.Null(item.FileState);
        Assert.Equal(RequiredEvidenceCoverage.MetadataAvailable, before.RequiredCoverage); Assert.Equal(1, before.RequiredMetadataAvailableCount);
        var json = JsonSerializer.Serialize(before); Assert.DoesNotContain(row.Evidence.Content!, json); Assert.DoesNotContain(row.Evidence.Title!, json);
        await Evidence(s => s.WithdrawAsync(f.Admin, f.Org, f.Incident.Id, row.Evidence.Id, new(1, "Retirar soporte")));
        var after = await Support(f, e); item = after.Tasks.Single(t => t.TaskId == e.Tasks[0].Id);
        Assert.Equal(TaskEvidenceSupportState.Withdrawn, item.SupportState); Assert.Equal(2, item.EvidenceRevision);
        Assert.Equal("COMPLETED", item.TaskStatus); Assert.Equal(row.Evidence.Id, item.CompletionEvidenceId);
        Assert.Equal(e.Revision, after.ExecutionRevision); Assert.Equal(e.IncidentRevision, after.IncidentRevision);
        Assert.Equal(1, after.CompletedRequiredEvidenceGapCount); Assert.Equal(1, after.CurrentReferencedEvidenceGapCount);
        Assert.Equal(RequiredEvidenceCoverage.NeedsReview, after.RequiredCoverage); Assert.NotEqual(before.RiskContextToken, after.RiskContextToken);
    }

    [Fact, Trait("Feature", "EvidenceReadiness")]
    public async Task OptionalEvidenceReferencesAreStillInspectedWithoutInventingRequiredCoverage()
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f); e = await AssignTask(f, e, e.Tasks[0].Id);
        e = await TaskState(f, e, e.Tasks[0].Id, ExecutionTaskState.InProgress); var row = await AddEvidence(f, e);
        e = await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, e.Id)); e = await CompleteWithEvidence(f, e, row.Evidence.Id);
        Assert.Equal(TaskEvidenceSupportState.MetadataAvailable, (await Support(f, e)).Tasks[0].SupportState);
        await Evidence(s => s.WithdrawAsync(f.Admin, f.Org, f.Incident.Id, row.Evidence.Id, new(1, "Retiro")));
        var r = await Support(f, e); Assert.Equal(TaskEvidenceSupportState.Withdrawn, r.Tasks[0].SupportState);
        Assert.Equal(RequiredEvidenceCoverage.NotRequired, r.RequiredCoverage); Assert.Equal(0, r.EvidenceRequiredTaskCount);
        Assert.Equal(0, r.CompletedRequiredEvidenceGapCount); Assert.Equal(1, r.CurrentReferencedEvidenceGapCount);
    }

    [Theory, Trait("Feature", "EvidenceReadiness")]
    [InlineData(false)] [InlineData(true)]
    public async Task SkippedTaskIsNotTreatedAsCompletedEvidence(bool requiresEvidence)
    {
        var f = await ExecutionSeed(approvals: true, approvalEvidence: requiresEvidence); var e = await BeginExecution(f);
        e = await AssignTask(f, e, e.Tasks[0].Id); e = await AskSkip(f, e); e = await DecideSkip(f, e, await LastSkipRequest(f, e));
        var r = await Support(f, e); var item = r.Tasks[0]; Assert.Equal("SKIPPED", item.TaskStatus); Assert.Null(item.CompletionEvidenceId);
        Assert.Equal(requiresEvidence ? TaskEvidenceSupportState.SkippedNeedsReview : TaskEvidenceSupportState.NotRequired, item.SupportState);
        Assert.Equal(requiresEvidence ? 1 : 0, r.SkippedRequiringEvidenceCount); Assert.Equal(0, r.RequiredMetadataAvailableCount);
    }

    [Theory, Trait("Feature", "EvidenceReadiness")]
    [InlineData("available")] [InlineData("tampered")] [InlineData("missing")] [InlineData("withdrawn")]
    public async Task FileReportUsesMetadataDoesNotOpenBytesAndReflectsQuarantineWhenDetected(string condition)
    {
        var (f, e) = await EvidenceSetup(true);
        await WithFiles(async (runtime, root) => {
            var upload = await UploadFile(runtime, f, e, name: "private-original-name.txt");
            e = await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, e.Id));
            using (var scope = runtime.Services.CreateScope()) e = await scope.ServiceProvider.GetRequiredService<IExecutionService>().ChangeAsync(f.Admin, f.Org, f.Incident.Id, e.Id, e.Tasks[0].Id,
                new(ExecutionTaskState.Completed, e.Revision, e.IncidentRevision, "Completar con archivo", upload.File.EvidenceId));
            var store = (FileStoreFault)runtime.Services.GetRequiredService<IPrivateEvidenceStore>(); var opens = store.Opens;
            var before = await RuntimeSupport(runtime, f, e); Assert.Equal(opens, store.Opens);
            Assert.Equal(TaskEvidenceSupportState.MetadataAvailable, before.Tasks[0].SupportState); Assert.Equal("AVAILABLE", before.Tasks[0].FileState);
            Assert.False(before.FileIntegrityVerified); Assert.DoesNotContain("private-original-name.txt", JsonSerializer.Serialize(before));
            if (condition is "tampered" or "missing")
            {
                var file = Directory.GetFiles(Path.Combine(root, "objects")).Single();
                if (condition == "missing") File.Delete(file); else await File.WriteAllTextAsync(file, "Tampered bytes");
                var uncheckedReport = await RuntimeSupport(runtime, f, e);
                Assert.Equal(TaskEvidenceSupportState.MetadataAvailable, uncheckedReport.Tasks[0].SupportState); Assert.Equal(opens, store.Opens);
                await Rejected(409, () => Files(runtime, s => s.DownloadAsync(f.Admin, f.Org, f.Incident.Id, upload.File.EvidenceId)));
            }
            if (condition == "withdrawn") await Evidence(s => s.WithdrawAsync(f.Admin, f.Org, f.Incident.Id, upload.File.EvidenceId, new(1, "Retiro")));
            opens = store.Opens; var after = await RuntimeSupport(runtime, f, e); Assert.Equal(opens, store.Opens);
            Assert.Equal("COMPLETED", after.Tasks[0].TaskStatus); Assert.Equal(e.Revision, after.ExecutionRevision); Assert.Equal(e.IncidentRevision, after.IncidentRevision);
            Assert.Equal(condition == "available" ? TaskEvidenceSupportState.MetadataAvailable : condition == "withdrawn" ? TaskEvidenceSupportState.Withdrawn : TaskEvidenceSupportState.FileUnavailable, after.Tasks[0].SupportState);
            if (condition != "available") { Assert.Equal("QUARANTINED", after.Tasks[0].FileState); Assert.NotEqual(before.RiskContextToken, after.RiskContextToken); Assert.Equal(1, after.CompletedRequiredEvidenceGapCount); }
        });
    }

    [Fact, Trait("Feature", "EvidenceReadiness")]
    public async Task FailedUnselectedFileDoesNotBecomeACompletionReference()
    {
        var (f, e) = await EvidenceSetup(true);
        await WithFiles(async (runtime, root) => {
            var row = await UploadFile(runtime, f, e); Assert.Equal("FAILED", row.File.State);
            var report = await RuntimeSupport(runtime, f, e); Assert.Equal(TaskEvidenceSupportState.AwaitingCompletion, report.Tasks[0].SupportState);
            Assert.Null(report.Tasks[0].CompletionEvidenceId); Assert.Null(report.Tasks[0].FileState);
        }, failure: "publish_once");
    }

    [Fact, Trait("Feature", "EvidenceReadiness")]
    public async Task OtherExecutionsIncidentsAndOrganizationsCannotSupplyTaskEvidence()
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f); var other = await RegisterIncident(f.Admin, f.Org);
        await Rejected(404, () => Executions(s => s.EvidenceReadinessAsync(f.Admin, f.Org, other.Id, e.Id)));
        await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, e.Id, new(e.Revision, e.IncidentRevision, "Nueva respuesta")));
        f = f with { Incident = await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)) }; var newer = await BeginExecution(f);
        Assert.All((await Support(f, newer)).Tasks, t => Assert.Null(t.CompletionEvidenceId));
        Assert.Equal("CANCELLED", (await Support(f, e)).ExecutionStatus);
        var beta = await Act(s => s.CreateOrganizationAsync(f.Platform, new("Beta", "beta", "CL", Account("beta"))));
        var member = await InScope((_, db) => db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == beta.Id));
        await Rejected(403, () => Support(f, e, member.UserId));
        await Rejected(404, () => Executions(s => s.EvidenceReadinessAsync(member.UserId, beta.Id, f.Incident.Id, e.Id)));
    }

    [Theory, Trait("Feature", "EvidenceReadiness")]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingIncidentAndExecutionReturnNotFound(bool missingIncident)
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f);
        await Rejected(404, () => Executions(s => s.EvidenceReadinessAsync(f.Admin, f.Org, missingIncident ? Guid.NewGuid() : f.Incident.Id, missingIncident ? e.Id : Guid.NewGuid())));
    }

    [Theory, Trait("Feature", "EvidenceReadiness")]
    [InlineData(OrganizationRole.OrgAdmin)] [InlineData(OrganizationRole.Analyst)] [InlineData(OrganizationRole.Viewer)]
    public async Task CurrentMembersCanReadWithoutWritingOrGainingExecutionPermissions(OrganizationRole role)
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f); await Act(s => s.AddMemberAsync(f.Admin, f.Org, new(f.Platform, role)));
        var before = await InScope((_, db) => db.AuditEvents.CountAsync()); var r = await Support(f, e, f.Platform);
        Assert.Equal(e.Id, r.ExecutionId); Assert.Equal(before, await InScope((_, db) => db.AuditEvents.CountAsync()));
        Assert.Equal(e.Revision, (await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, e.Id))).Revision);
    }

    [Theory, Trait("Feature", "EvidenceReadiness")]
    [InlineData("global")] [InlineData("inactive")] [InlineData("locked")] [InlineData("suspended")] [InlineData("organization")]
    public async Task ReadsRevalidateAccountMembershipAndOrganizationWithoutGlobalBypass(string condition)
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f);
        if (condition != "global") await Act(s => s.AddMemberAsync(f.Admin, f.Org, new(f.Platform, OrganizationRole.Viewer)));
        if (condition != "global") await InScope(async (_, db) => {
            if (condition == "inactive") (await db.Users.SingleAsync(u => u.Id == f.Platform)).IsActive = false;
            if (condition == "locked") (await db.Users.SingleAsync(u => u.Id == f.Platform)).LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            if (condition == "suspended") (await db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == f.Org && m.UserId == f.Platform)).Status = MembershipStatus.Suspended;
            if (condition == "organization") (await db.Organizations.SingleAsync(o => o.Id == f.Org)).Status = OrganizationStatus.Suspended;
            return await db.SaveChangesAsync();
        });
        await Rejected(403, () => Support(f, e, f.Platform));
    }

    private sealed class SupportQueryInterceptor(Func<Task>? change = null) : DbCommandInterceptor
    {
        private int changed;
        public List<string> Commands { get; } = [];
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            if (change is not null && command.CommandText.Contains("LEFT JOIN", StringComparison.Ordinal) && command.CommandText.Contains("incident_evidence", StringComparison.Ordinal)
                && Interlocked.Exchange(ref changed, 1) == 0) await change();
            return result;
        }
    }

    [Fact, Trait("Feature", "EvidenceReadiness")]
    public async Task ConcurrentWithdrawalCannotMixTaskEvidenceAndRiskContextSnapshots()
    {
        var (f, e) = await EvidenceSetup(true); var row = await AddEvidence(f, e);
        e = await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, e.Id)); e = await CompleteWithEvidence(f, e, row.Evidence.Id);
        var before = await Support(f, e); var calls = 0;
        var interceptor = new SupportQueryInterceptor(async () => { calls++; await Evidence(s => s.WithdrawAsync(f.Admin, f.Org, f.Incident.Id, row.Evidence.Id, new(1, "Retiro concurrente"))); });
        var connection = await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString()!));
        await using var runtime = new ReadinessApplication(connection, interceptor); using var scope = runtime.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<IExecutionService>().EvidenceReadinessAsync(f.Admin, f.Org, f.Incident.Id, e.Id);
        Assert.Equal(1, calls); Assert.Equal(TaskEvidenceSupportState.MetadataAvailable, report.Tasks[0].SupportState); Assert.Equal(before.RiskContextToken, report.RiskContextToken);
        var fresh = await Support(f, e); Assert.Equal(TaskEvidenceSupportState.Withdrawn, fresh.Tasks[0].SupportState); Assert.NotEqual(before.RiskContextToken, fresh.RiskContextToken);
        Assert.Equal(before.IncidentRevision, fresh.IncidentRevision); Assert.Equal(before.ExecutionRevision, fresh.ExecutionRevision);
    }

    [Fact, Trait("Feature", "EvidenceReadiness")]
    public async Task HundredTaskReportIsCompleteOrderedAndReadsEvidenceInOneMetadataBatch()
    {
        var f = await ExecutionSeed(customize: c => c with { Steps = Enumerable.Range(1, 100).Select(n => c.Steps[0] with { Code = "STEP_" + n, Position = n }).ToList(), Dependencies = [], Requirements = [] });
        var e = await BeginExecution(f); var interceptor = new SupportQueryInterceptor();
        var connection = await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString()!));
        await using var runtime = new ReadinessApplication(connection, interceptor); using var scope = runtime.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<IExecutionService>().EvidenceReadinessAsync(f.Admin, f.Org, f.Incident.Id, e.Id);
        Assert.Equal(100, report.TaskCount); Assert.Equal(Enumerable.Range(1, 100), report.Tasks.Select(t => t.Position)); Assert.Equal(100, report.Tasks.Select(t => t.TaskId).Distinct().Count());
        Assert.Equal(RequiredEvidenceCoverage.NotRequired, report.RequiredCoverage);
        var batch = Assert.Single(interceptor.Commands, c => c.Contains("incident_evidence", StringComparison.Ordinal) && c.Contains("evidence_files", StringComparison.Ordinal));
        Assert.DoesNotContain("storage_id", batch); Assert.DoesNotContain("original_name", batch); Assert.DoesNotContain("sha256", batch); Assert.DoesNotContain(".content", batch); Assert.InRange(interceptor.Commands.Count, 1, 12);
    }

    [Theory, Trait("Feature", "EvidenceReadiness")]
    [InlineData("missing")] [InlineData("incident")] [InlineData("other-task")] [InlineData("other-execution")]
    public async Task LegacyInvalidCompletionReferencesNeverBorrowAnotherEvidenceRecord(string condition)
    {
        var (f, e) = await EvidenceSetup(true); var correct = await AddEvidence(f, e);
        e = await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, e.Id)); e = await CompleteWithEvidence(f, e, correct.Evidence.Id);
        Guid? reference = null;
        if (condition == "incident") reference = (await AddEvidence(f)).Evidence.Id;
        if (condition == "other-task")
        {
            var parent = await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id));
            reference = (await Evidence(s => s.RegisterAsync(f.Admin, f.Org, f.Incident.Id, new(EvidenceKind.Text, EvidenceClassification.Confidential, "Another task private title", "Another task private content", e.Id, e.Tasks[1].Id, parent.Revision, "Registrar otro soporte")))).Evidence.Id;
        }
        if (condition == "other-execution")
        {
            reference = correct.Evidence.Id;
            await Executions(s => s.CancelAsync(f.Admin, f.Org, f.Incident.Id, e.Id, new(e.Revision, e.IncidentRevision, "Otra ejecucion")));
            f = f with { Incident = await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)) };
            e = await BeginExecution(f); e = await AssignTask(f, e, e.Tasks[0].Id);
        }
        // Owned disposable database only: simulate a legacy inconsistency while preserving FK constraints.
        // Transaction rollback restores trigger state even if setup fails. Production guards are not changed.
        await InScope(async (_, db) => {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE app.execution_tasks DISABLE TRIGGER USER");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.execution_tasks SET status='COMPLETED',completion_evidence_id={reference} WHERE id={e.Tasks[0].Id}");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE app.execution_tasks ENABLE TRIGGER USER"); await tx.CommitAsync(); return true;
        });
        var r = await Support(f, e); var task = r.Tasks[0];
        Assert.Equal(condition == "missing" ? TaskEvidenceSupportState.MissingCompletionReference : TaskEvidenceSupportState.InvalidCompletionReference, task.SupportState);
        Assert.Null(task.EvidenceKind); Assert.Null(task.EvidenceRevision); Assert.Null(task.EvidenceStatus); Assert.Null(task.FileState);
        Assert.Equal(1, r.CompletedRequiredEvidenceGapCount); Assert.Equal(RequiredEvidenceCoverage.NeedsReview, r.RequiredCoverage);
    }

    [Fact, Trait("Feature", "EvidenceReadiness")]
    public async Task LimitedRuntimeCanReadSupportWithoutNewGrantsAndWithoutOpeningFiles()
    {
        var (f, e) = await EvidenceSetup(true); var row = await AddEvidence(f, e); e = await Executions(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id, e.Id)); e = await CompleteWithEvidence(f, e, row.Evidence.Id);
        var role = "openirp_support_" + Guid.NewGuid().ToString("N"); var password = "Runtime!9" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(await InScope((_, db) => Task.FromResult(db.Database.GetConnectionString())));
        await using var owner = new NpgsqlConnection(builder.ConnectionString); await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE {role} LOGIN NOINHERIT PASSWORD '{password}'", owner)) await create.ExecuteNonQueryAsync();
        try
        {
            await using (var setting = new NpgsqlCommand("SELECT set_config('openirp.runtime_role',@role,false)", owner)) { setting.Parameters.AddWithValue("role", role); await setting.ExecuteNonQueryAsync(); }
            await using (var grants = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ci", "grant-runtime.sql")), owner)) await grants.ExecuteNonQueryAsync();
            builder.Username = role; builder.Password = password;
            await using var runtime = new TestApplication(builder.ConnectionString); var r = await RuntimeSupport(runtime, f, e);
            Assert.Equal(TaskEvidenceSupportState.MetadataAvailable, r.Tasks[0].SupportState); Assert.False(r.TechnicalSufficiencyVerified);
        }
        finally { NpgsqlConnection.ClearAllPools(); await using var cleanup = new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};", owner); await cleanup.ExecuteNonQueryAsync(); }
    }

    [Fact, Trait("Feature", "EvidenceReadiness")]
    public async Task HttpSupportIsAuthenticatedNoStoreAndEnumsAreTextWithoutCsrfOnGet()
    {
        var f = await ExecutionSeed(); var e = await BeginExecution(f); var path = $"/api/organizations/{f.Org}/incidents/{f.Incident.Id}/executions/{e.Id}/evidence-readiness";
        using var anonymous = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        using var client = Client(); await Login(client, "alpha"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("NotRequired", body.GetProperty("requiredCoverage").GetString());
        Assert.Equal("NotRequired", body.GetProperty("tasks")[0].GetProperty("supportState").GetString());
        Assert.False(body.GetProperty("technicalSufficiencyVerified").GetBoolean());
    }

    [Fact, Trait("Feature", "EvidenceReadiness")]
    public async Task CompletedExecutionAndClosedIncidentStayReadableWithoutReopeningOrClosingSideEffects()
    {
        var f = await ExecutionSeed(); var e = await FinishAllTasks(f, await BeginExecution(f));
        e = await Executions(s => s.CompleteAsync(f.Admin, f.Org, f.Incident.Id, e.Id, new(e.Revision, e.IncidentRevision, "Finalizar respuesta")));
        var i = await ResolvedForClosure(f.Admin, f.Org, f.Membership, false, await Incidents(s => s.GetAsync(f.Admin, f.Org, f.Incident.Id)));
        i = await CloseWithContext(f.Admin, f.Org, i, null);
        var r = await Support(f, e); Assert.Equal("CLOSED", r.IncidentStatus); Assert.Equal("COMPLETED", r.ExecutionStatus);
        Assert.All(r.Tasks, t => Assert.Equal("COMPLETED", t.TaskStatus)); Assert.Equal(RequiredEvidenceCoverage.NotRequired, r.RequiredCoverage);
        Assert.Equal(i.Revision, r.IncidentRevision); Assert.False(r.FileIntegrityVerified); Assert.True(r.RequiresHumanReview);
    }
}
