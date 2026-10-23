using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIRP.Application.Audit;
using OpenIRP.Application.Identity;
using OpenIRP.Domain.Entities;
using OpenIRP.Infrastructure.Persistence;

namespace OpenIRP.Infrastructure.Audit;

public sealed record AuditAttempt(string Action, Guid? Actor, Guid? Organization, string ResourceType, Guid? ResourceId = null);

public sealed class AuditRecorder(IServiceScopeFactory scopes, AuditContext context, ILogger<AuditRecorder> logger)
{
    public AuditEvent Success(AuditAttempt attempt, string details = "{}") => new()
    {
        OrganizationId = attempt.Organization, ActorUserId = attempt.Actor,
        ActorType = attempt.Actor is null ? "SYSTEM" : "USER", Action = attempt.Action,
        ResourceType = attempt.ResourceType, ResourceId = attempt.ResourceId, Outcome = "SUCCESS",
        CorrelationId = context.CorrelationId, DetailsJson = details
    };

    public async Task RejectedAsync(AuditAttempt attempt, Exception error)
    {
        // Separate connection/transaction, called only after business rollback.
        // All rejected attempts are global: unverified tenant/resource IDs cannot pollute another tenant's log.
        // Never persist exception messages, request bodies, emails, passwords, cookies or tokens.
        var business = error as IdentityOperationException;
        var code = business?.Code.Split(':')[0] ?? "operation_failed";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OpenIrpDbContext>();
            db.AuditEvents.Add(new AuditEvent
            {
                ActorUserId = attempt.Actor, ActorType = attempt.Actor is null ? "SYSTEM" : "USER",
                Action = attempt.Action, ResourceType = attempt.ResourceType,
                Outcome = business?.StatusCode is 403 or 404 ? "DENIED" : "FAILURE",
                CorrelationId = context.CorrelationId, DetailsJson = JsonSerializer.Serialize(new { code })
            });
            await db.SaveChangesAsync(timeout.Token);
        }
        catch (Exception)
        {
            // Persistence of a rejection cannot be atomic with a rolled-back operation.
            // Preserve the original denial and emit an operational alarm without sensitive exception data.
            logger.LogError("AUDIT_REJECTION_NOT_PERSISTED correlation={CorrelationId} action={Action}", context.CorrelationId, attempt.Action);
        }
    }
}
