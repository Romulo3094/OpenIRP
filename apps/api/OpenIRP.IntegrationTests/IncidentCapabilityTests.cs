using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Application.Inventory;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private static AssessIncidentCapability Assessment(long revision = 1, CapabilityAvailability? availability = CapabilityAvailability.Unavailable,
        long profile = 0, long catalog = 1) => new(availability, "Limitación privada del incidente", "Evaluación justificada", revision, profile, catalog);

    [Fact]
    public async Task IncidentCapabilitiesInheritProfileAndUndeclaredUnknownWithoutWritingRows()
    {
        var seed = await Seed();
        var incident = await RegisterIncident(seed.Admin, seed.Org.Id);
        var capability = await CapabilityId();
        await Inventory(s => s.PutCapabilityAsync(seed.Admin, seed.Org.Id, capability, new(CapabilityAvailability.Available, "Notas del perfil", 0)));
        var count = await InScope((_,db) => db.AuditEvents.CountAsync());
        var page = await Incidents(s => s.CapabilitiesAsync(seed.Admin, seed.Org.Id, incident.Id));
        Assert.Equal(1, page.IncidentRevision); Assert.Equal(8, page.Items.Count);
        var inherited = page.Items.Single(c => c.CapabilityId == capability);
        Assert.Equal(CapabilityAvailability.Available, inherited.EffectiveAvailability);
        Assert.Equal(IncidentCapabilitySource.OrganizationProfile, inherited.Source);
        Assert.Equal(1, inherited.OrganizationCapabilityRevision); Assert.Equal("Notas del perfil", inherited.OrganizationNotes);
        Assert.Null(inherited.AssessmentId); Assert.Null(inherited.OverrideAvailability);
        Assert.All(page.Items.Where(c => c.CapabilityId != capability), c =>
        { Assert.Equal(CapabilityAvailability.Unknown,c.EffectiveAvailability); Assert.Equal(IncidentCapabilitySource.Undeclared,c.Source); Assert.Equal(0,c.OrganizationCapabilityRevision); });
        Assert.Equal(0,await InScope((_,db) => db.IncidentCapabilityAssessments.CountAsync()));
        Assert.Equal(count,await InScope((_,db) => db.AuditEvents.CountAsync()));
    }

    [Fact]
    public async Task IncidentAssessmentDoesNotRewriteOrganizationProfileOrOtherIncident()
    {
        var seed = await Seed();
        var first = await RegisterIncident(seed.Admin,seed.Org.Id);
        var second = await RegisterIncident(seed.Admin,seed.Org.Id);
        var capability = await CapabilityId();
        await Inventory(s => s.PutCapabilityAsync(seed.Admin,seed.Org.Id,capability,new(CapabilityAvailability.Available,"Privado organización",0)));
        var changed = await Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,first.Id,capability,Assessment(profile:1)));
        Assert.Equal(2,changed.IncidentRevision); Assert.Equal(CapabilityAvailability.Unavailable,changed.Capability.EffectiveAvailability);
        Assert.Equal(IncidentCapabilitySource.IncidentAssessment,changed.Capability.Source);
        Assert.Equal(CapabilityAvailability.Available,(await Inventory(s => s.GetCapabilitiesAsync(seed.Admin,seed.Org.Id))).Items.Single(c => c.CapabilityId == capability).Availability);
        Assert.Equal(CapabilityAvailability.Available,(await Incidents(s => s.CapabilitiesAsync(seed.Admin,seed.Org.Id,second.Id))).Items.Single(c => c.CapabilityId == capability).EffectiveAvailability);
        var history = (await Incidents(s => s.EventsAsync(seed.Admin,seed.Org.Id,first.Id))).Items[1];
        Assert.Equal("CAPABILITY_ASSESSED",history.Action);
        var snapshot = history.Details.GetProperty("capability");
        Assert.Equal("Available",snapshot.GetProperty("organizationAvailability").GetString());
        Assert.Equal("Unavailable",snapshot.GetProperty("effectiveAvailability").GetString());
        var audit = await InScope((_,db) => db.AuditEvents.SingleAsync(e => e.Action == "incident.capability_assessed"));
        Assert.Equal(history.CorrelationId,audit.CorrelationId);
        var json = JsonSerializer.Serialize(audit);
        Assert.DoesNotContain(Assessment().Limitations!,json); Assert.DoesNotContain(Assessment().Reason,json); Assert.DoesNotContain("Privado organización",json);
    }

    [Fact]
    public async Task ExplicitUnknownOverridesAvailableAndInheritanceResetPreservesIdentityAndRevision()
    {
        var seed = await Seed();var incident = await RegisterIncident(seed.Admin,seed.Org.Id);var capability = await CapabilityId();
        await Inventory(s => s.PutCapabilityAsync(seed.Admin,seed.Org.Id,capability,new(CapabilityAvailability.Available,null,0)));
        var unknown = await Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(availability:CapabilityAvailability.Unknown,profile:1)));
        Assert.Equal(CapabilityAvailability.Unknown,unknown.Capability.EffectiveAvailability);
        Assert.Equal(IncidentCapabilitySource.IncidentAssessment,unknown.Capability.Source);
        var inherited = await Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(2,null,1) with {Limitations=null}));
        Assert.Null(inherited.Capability.OverrideAvailability);
        Assert.Equal(CapabilityAvailability.Available,inherited.Capability.EffectiveAvailability);
        Assert.Equal(unknown.Capability.AssessmentId,inherited.Capability.AssessmentId);
        Assert.Equal(3,inherited.Capability.AssessedAtRevision);
        await Rejected(409,() => Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(1,profile:1))));
        Assert.Equal(1,await InScope((_,db) => db.IncidentCapabilityAssessments.CountAsync()));
        Assert.Equal("CAPABILITY_INHERITED",(await Incidents(s => s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items[2].Action);
    }

    [Fact]
    public async Task LiveProfileChangesAffectInheritanceButNeverRewriteHistoricalSnapshots()
    {
        var seed = await Seed();var incident = await RegisterIncident(seed.Admin,seed.Org.Id);var capability = await CapabilityId();
        await Inventory(s => s.PutCapabilityAsync(seed.Admin,seed.Org.Id,capability,new(CapabilityAvailability.Available,"Original",0)));
        await Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(profile:1)));
        await Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(2,null,1)));
        await Inventory(s => s.PutCapabilityAsync(seed.Admin,seed.Org.Id,capability,new(CapabilityAvailability.Unavailable,"Cambiado",1)));
        var live = (await Incidents(s => s.CapabilitiesAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Single(c => c.CapabilityId == capability);
        Assert.Equal(CapabilityAvailability.Unavailable,live.EffectiveAvailability);Assert.Equal(2,live.OrganizationCapabilityRevision);
        Assert.Equal(Assessment().Limitations,live.Limitations);
        var old = (await Incidents(s => s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items[2].Details.GetProperty("capability");
        Assert.Equal("Available",old.GetProperty("effectiveAvailability").GetString());
        Assert.Equal("Original",old.GetProperty("organizationNotes").GetString());
        await Rejected(409,() => Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(3,profile:1))),"stale_capability_context");
        Assert.Equal(3,(await Incidents(s => s.GetAsync(seed.Admin,seed.Org.Id,incident.Id))).Revision);
    }

    [Fact]
    public async Task CatalogDeactivationForcesUnknownAndAllowsClearingOldOverride()
    {
        var seed = await Seed();var incident = await RegisterIncident(seed.Admin,seed.Org.Id);var capability = await CapabilityId();
        await Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(availability:CapabilityAvailability.Available)));
        await Inventory(s => s.UpdateCatalogAsync(seed.Platform,CatalogKind.Capabilities,capability,new("Retirada","CUSTOM",null,false,1)));
        var inactive = (await Incidents(s => s.CapabilitiesAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Single(c => c.CapabilityId == capability);
        Assert.False(inactive.IsActive);Assert.Equal(CapabilityAvailability.Unknown,inactive.EffectiveAvailability);
        Assert.Equal(IncidentCapabilitySource.InactiveCatalog,inactive.Source);
        Assert.Equal(CapabilityAvailability.Available,inactive.OverrideAvailability);
        await Rejected(409,() => Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(2,catalog:2))),"inactive_capability");
        await Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(2,null,catalog:2)));
        await Inventory(s => s.UpdateCatalogAsync(seed.Platform,CatalogKind.Capabilities,capability,new("Vigente","CUSTOM",null,true,2)));
        var current = (await Incidents(s => s.CapabilitiesAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Single(c => c.CapabilityId == capability);
        Assert.Equal(IncidentCapabilitySource.Undeclared,current.Source);Assert.Null(current.OverrideAvailability);
        Assert.Equal("Available",(await Incidents(s => s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items[1].Details.GetProperty("capability").GetProperty("effectiveAvailability").GetString());
    }

    [Fact]
    public async Task CatalogRevisionChangeRequiresRereadingContextBeforeAssessment()
    {
        var seed = await Seed();var incident = await RegisterIncident(seed.Admin,seed.Org.Id);var capability = await CapabilityId();
        await Inventory(s => s.UpdateCatalogAsync(seed.Platform,CatalogKind.Capabilities,capability,new("Renombrada","CUSTOM",null,true,1)));
        await Rejected(409,() => Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment())),"stale_capability_context");
        await Incidents(s => s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(catalog:2)));
        Assert.Equal(2,(await Incidents(s => s.GetAsync(seed.Admin,seed.Org.Id,incident.Id))).Revision);
    }

    [Theory]
    [InlineData(OrganizationRole.OrgAdmin)]
    [InlineData(OrganizationRole.Analyst)]
    public async Task AuthorizedIncidentRolesCanAssessWithoutChangingGeneralProfile(OrganizationRole role)
    {
        var seed = await Seed();await Act(s => s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,role)));
        var incident = await RegisterIncident(seed.Admin,seed.Org.Id);var capability = await CapabilityId();
        await Incidents(s => s.AssessCapabilityAsync(seed.Platform,seed.Org.Id,incident.Id,capability,Assessment()));
        Assert.Equal(0,await InScope((_,db) => db.OrganizationCapabilities.CountAsync()));
        Assert.Equal(CapabilityAvailability.Unavailable,(await Incidents(s => s.CapabilitiesAsync(seed.Platform,seed.Org.Id,incident.Id))).Items.Single(c => c.CapabilityId == capability).EffectiveAvailability);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerOrPlatformWithoutMembershipCannotAssessCapabilities(bool viewer)
    {
        var seed = await Seed();var incident = await RegisterIncident(seed.Admin,seed.Org.Id);var capability = await CapabilityId();
        if(viewer) {await Act(s => s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.Viewer)));Assert.Equal(8,(await Incidents(s => s.CapabilitiesAsync(seed.Platform,seed.Org.Id,incident.Id))).Items.Count);}
        else await Rejected(403,() => Incidents(s => s.CapabilitiesAsync(seed.Platform,seed.Org.Id,incident.Id)));
        await Rejected(403,() => Incidents(s => s.AssessCapabilityAsync(seed.Platform,seed.Org.Id,incident.Id,capability,Assessment())));
        Assert.Equal(0,await InScope((_,db) => db.IncidentCapabilityAssessments.CountAsync()));
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("organization")]
    [InlineData("account")]
    [InlineData("lockout")]
    public async Task IncidentCapabilitiesRejectInactiveAuthorizationContext(string condition)
    {
        var seed = await Seed();var member = await Act(s => s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.Analyst)));
        var incident = await RegisterIncident(seed.Admin,seed.Org.Id);var capability = await CapabilityId();
        if(condition == "membership") await Act(s => s.ChangeMemberAsync(seed.Admin,seed.Org.Id,member.Id,new(OrganizationRole.Analyst,MembershipStatus.Revoked,null)));
        else await InScope(async (_,db) =>
        {
            if(condition == "organization") (await db.Organizations.SingleAsync()).Status=OrganizationStatus.Suspended;
            else {var user=await db.Users.SingleAsync(u=>u.Id==seed.Platform);if(condition=="account") user.IsActive=false;else user.LockoutEnd=DateTimeOffset.UtcNow.AddMinutes(15);}
            return await db.SaveChangesAsync();
        });
        await Rejected(403,()=>Incidents(s=>s.CapabilitiesAsync(seed.Platform,seed.Org.Id,incident.Id)));
        await Rejected(403,()=>Incidents(s=>s.AssessCapabilityAsync(seed.Platform,seed.Org.Id,incident.Id,capability,Assessment())));
    }

    [Fact]
    public async Task CapabilityAssessmentsAndCompositeForeignKeysRemainTenantScoped()
    {
        var seed = await Seed();var incident = await RegisterIncident(seed.Admin,seed.Org.Id);var capability = await CapabilityId();
        var beta = await Act(s => s.CreateOrganizationAsync(seed.Platform,new("Beta","beta","CL",Account("beta"))));
        var betaAdmin = await InScope((_,db)=>db.OrganizationMemberships.Where(m=>m.OrganizationId==beta.Id).Select(m=>m.UserId).SingleAsync());
        await Rejected(404,()=>Incidents(s=>s.CapabilitiesAsync(betaAdmin,beta.Id,incident.Id)));
        await Rejected(404,()=>Incidents(s=>s.AssessCapabilityAsync(betaAdmin,beta.Id,incident.Id,capability,Assessment())));
        await Rejected(403,()=>Incidents(s=>s.AssessCapabilityAsync(betaAdmin,seed.Org.Id,incident.Id,capability,Assessment())));
        var error = await Assert.ThrowsAsync<DbUpdateException>(()=>InScope(async (_,db)=>
        {
            db.IncidentCapabilityAssessments.Add(new(){OrganizationId=beta.Id,IncidentId=incident.Id,CapabilityId=capability,Availability=CapabilityAvailability.Unavailable,Reason="Foreign",AssessedAtRevision=2});
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23503",Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task DatabaseEnforcesUniqueIncidentCapabilityAndProtectsDecisionMarkers()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);var capability=await CapabilityId();
        await Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment()));
        var duplicate=await Assert.ThrowsAsync<DbUpdateException>(()=>InScope(async (_,db)=>
        {
            db.IncidentCapabilityAssessments.Add(new(){OrganizationId=seed.Org.Id,IncidentId=incident.Id,CapabilityId=capability,Reason="Duplicate",AssessedAtRevision=3});
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23505",Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        foreach(var sql in new[]{"DELETE FROM app.incident_capability_assessments","TRUNCATE app.incident_capability_assessments","UPDATE app.incident_capability_assessments SET assessed_at_revision=1","UPDATE app.incident_capability_assessments SET capability_id=gen_random_uuid(),assessed_at_revision=3"})
        {
            var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql)));
            Assert.Equal("42501",error.SqlState);
        }
    }

    [Fact]
    public async Task InvalidCapabilityInputDoesNotAdvanceAggregateOrCreateDecision()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);var capability=await CapabilityId();
        foreach(var request in new[]{Assessment(availability:(CapabilityAvailability)999),Assessment() with {Limitations=new string('x',2001)},Assessment() with {Reason=" "},Assessment(profile:-1),Assessment(catalog:0)})
            await Rejected(400,()=>Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,request)));
        await Rejected(404,()=>Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,Guid.NewGuid(),Assessment())));
        Assert.Equal(1,(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id))).Revision);
        Assert.Equal(0,await InScope((_,db)=>db.IncidentCapabilityAssessments.CountAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCapabilityDecisionsRequireCurrentAggregateRevision(bool sameCapability)
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);var a=await CapabilityId();var b=sameCapability?a:await CapabilityId("MFA");
        async Task<int> Attempt(Guid capability){try{await Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment()));return 200;}catch(IdentityOperationException e){return e.StatusCode;}}
        Assert.Equal(new[]{200,409},(await Race(()=>Attempt(a),()=>Attempt(b))).Order().ToArray());
        Assert.Equal(1,await InScope((_,db)=>db.IncidentCapabilityAssessments.CountAsync()));
        Assert.Equal(2,await InScope((_,db)=>db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task ConcurrentProfileChangeCannotSilentlyUseObsoleteBaseline()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);var capability=await CapabilityId();
        async Task<int> Assess(){try{await Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment()));return 200;}catch(IdentityOperationException e){return e.StatusCode;}}
        async Task<int> Profile(){await Inventory(s=>s.PutCapabilityAsync(seed.Admin,seed.Org.Id,capability,new(CapabilityAvailability.Available,"New baseline",0)));return 200;}
        var results=await Race(Assess,Profile);Assert.Contains(results[0],new[]{200,409});Assert.Equal(200,results[1]);
        var current=(await Incidents(s=>s.CapabilitiesAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Single(c=>c.CapabilityId==capability);
        Assert.Equal(CapabilityAvailability.Available,current.OrganizationAvailability);
        Assert.Equal(results[0]==200?CapabilityAvailability.Unavailable:CapabilityAvailability.Available,current.EffectiveAvailability);
        Assert.Equal(results[0]==200?1:0,await InScope((_,db)=>db.IncidentCapabilityAssessments.CountAsync()));
    }

    [Fact]
    public async Task ConcurrentCatalogDeactivationNeverProducesEffectiveAvailableAfterward()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);var capability=await CapabilityId();
        async Task<int> Assess(){try{await Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(availability:CapabilityAvailability.Available)));return 200;}catch(IdentityOperationException e){return e.StatusCode;}}
        async Task<int> Deactivate(){await Inventory(s=>s.UpdateCatalogAsync(seed.Platform,CatalogKind.Capabilities,capability,new("Retirada","CUSTOM",null,false,1)));return 200;}
        var results=await Race(Assess,Deactivate);Assert.Contains(results[0],new[]{200,409});Assert.Equal(200,results[1]);
        var page=await Incidents(s=>s.CapabilitiesAsync(seed.Admin,seed.Org.Id,incident.Id));
        Assert.DoesNotContain(page.Items,c=>c.CapabilityId==capability && c.EffectiveAvailability==CapabilityAvailability.Available);
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task FailedCapabilityHistoryRollsBackDecisionAndAggregate(bool inherit,bool timeline)
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);var capability=await CapabilityId();
        if(inherit) await Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment()));
        var revision=inherit?2:1;
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(timeline?"""
            CREATE FUNCTION app.reject_capability_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected timeline failure'; END; $body$;
            CREATE TRIGGER reject_capability_test BEFORE INSERT ON app.incident_events FOR EACH ROW
            WHEN (NEW.action LIKE 'CAPABILITY_%') EXECUTE FUNCTION app.reject_capability_test();
            """:"""
            CREATE FUNCTION audit.reject_capability_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected audit failure'; END; $body$;
            CREATE TRIGGER reject_capability_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
            WHEN (NEW.outcome='SUCCESS' AND NEW.action LIKE 'incident.capability_%') EXECUTE FUNCTION audit.reject_capability_test();
            """));
        await Assert.ThrowsAsync<DbUpdateException>(()=>Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(revision,inherit?null:CapabilityAvailability.Unavailable))));
        Assert.Equal(revision,(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id))).Revision);
        Assert.Equal(revision,await InScope((_,db)=>db.IncidentEvents.CountAsync()));
        Assert.Equal(inherit?1:0,await InScope((_,db)=>db.IncidentCapabilityAssessments.CountAsync()));
        if(inherit) Assert.Equal(CapabilityAvailability.Unavailable,await InScope((_,db)=>db.IncidentCapabilityAssessments.Select(a=>a.Availability).SingleAsync()));
    }

    [Fact]
    public async Task MaximumUnicodeNotesAndLimitationsFitBoundedHistoricalPayload()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);var capability=await CapabilityId();
        await Inventory(s=>s.PutCapabilityAsync(seed.Admin,seed.Org.Id,capability,new(CapabilityAvailability.Available,new string('á',2000),0)));
        await Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(profile:1) with {Limitations=new string('ñ',2000),Reason=new string('ó',2000)}));
        var details=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items[1].Details;
        Assert.Equal(new string('ñ',2000),details.GetProperty("capability").GetProperty("limitations").GetString());
        Assert.Equal(new string('á',2000),details.GetProperty("capability").GetProperty("organizationNotes").GetString());
    }

    [Fact]
    public async Task CapabilityPaginationIsBoundedAndIncludesCoherentIncidentRevision()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        var first=await Incidents(s=>s.CapabilitiesAsync(seed.Admin,seed.Org.Id,incident.Id,limit:4));
        var second=await Incidents(s=>s.CapabilitiesAsync(seed.Admin,seed.Org.Id,incident.Id,skip:first.NextSkip!.Value,limit:4));
        Assert.Equal(8,first.Items.Concat(second.Items).Select(c=>c.CapabilityId).Distinct().Count());Assert.Null(second.NextSkip);
        Assert.Equal(incident.Revision,first.IncidentRevision);Assert.Equal(incident.Revision,second.IncidentRevision);
        await Rejected(400,()=>Incidents(s=>s.CapabilitiesAsync(seed.Admin,seed.Org.Id,incident.Id,limit:101)));
        await Rejected(400,()=>Incidents(s=>s.CapabilitiesAsync(seed.Admin,seed.Org.Id,incident.Id,skip:-1)));
    }

    [Fact]
    public async Task CapabilityMigrationPreservesEarlierHistoryAndRejectsLossyDowngrade()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);var capability=await CapabilityId();
        var before=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Single();
        await InScope(async (_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261022194422_IncidentAssetSnapshots");await db.Database.MigrateAsync();return true;});
        var after=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Single();
        Assert.Equal(before.Id,after.Id);Assert.Equal(before.OccurredAt,after.OccurredAt);Assert.Equal(before.Details.GetRawText(),after.Details.GetRawText());
        await Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment()));
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope(async (_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261022194422_IncidentAssetSnapshots");return true;}));
        Assert.Contains("Cannot roll back incident capabilities",error.MessageText);
        Assert.Equal(1,await InScope((_,db)=>db.IncidentCapabilityAssessments.CountAsync()));
    }

    [Fact]
    public async Task HttpCapabilityAssessmentRequiresExplicitInheritanceAndAllPreconditions()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);var capability=await CapabilityId();
        var route=$"/api/organizations/{seed.Org.Id}/incidents/{incident.Id}/capabilities";
        using var anonymous=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(route)).StatusCode);
        using var admin=Client();await Login(admin,"alpha");
        var valid=JsonSerializer.SerializeToElement(Assessment(),InventoryJson);
        foreach(var missing in new[]{"availability","expectedRevision","expectedOrganizationCapabilityRevision","expectedCatalogRevision"})
        {
            var body=valid.EnumerateObject().Where(p=>p.Name!=missing).ToDictionary(p=>p.Name,p=>(object)p.Value);
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"{route}/{capability}",body)).StatusCode);
        }
        var response=await admin.PutAsJsonAsync($"{route}/{capability}",Assessment());Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var changed=(await response.Content.ReadFromJsonAsync<IncidentCapabilityChange>(InventoryJson))!;
        Assert.Equal(CapabilityAvailability.Unavailable,changed.Capability.EffectiveAvailability);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync($"{route}/{capability}",Assessment(2,null))).StatusCode);
        var read=await admin.GetAsync(route);Assert.True(read.Headers.CacheControl!.NoStore);
        var page=(await read.Content.ReadFromJsonAsync<IncidentCapabilityPage>(InventoryJson))!;
        Assert.Equal(3,page.IncidentRevision);
        Assert.Equal(IncidentCapabilitySource.Undeclared,page.Items.Single(c=>c.CapabilityId==capability).Source);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"{route}/{capability}",Assessment(3))).StatusCode);
    }
}
