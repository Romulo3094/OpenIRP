using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenIRP.Application.Identity;
using OpenIRP.Application.Incidents;
using OpenIRP.Domain.Entities;
using Xunit;

namespace OpenIRP.IntegrationTests;

public sealed partial class IdentityAdministrationTests
{
    private static AssessIncidentImpact Cia(long revision=1) => new(ImpactLevel.High,ImpactLevel.None,ImpactLevel.Low,"Evaluación CIA privada",revision);

    [Fact]
    public async Task IncidentStartsUnevaluatedAndPartialAssessmentDoesNotAssumeNone()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        Assert.Null(incident.ConfidentialityImpact);Assert.Null(incident.IntegrityImpact);Assert.Null(incident.AvailabilityImpact);
        Assert.False(incident.IsImpactAssessmentComplete);
        var partial=await Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,new(ImpactLevel.None,null,ImpactLevel.High,"Disponibilidad investigada",1)));
        Assert.Equal(ImpactLevel.None,partial.ConfidentialityImpact);Assert.Null(partial.IntegrityImpact);
        Assert.False(partial.IsImpactAssessmentComplete);Assert.Equal(2,partial.Revision);
        var read=await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id));
        Assert.Null(read.IntegrityImpact);Assert.Equal(ImpactLevel.High,read.AvailabilityImpact);
    }

    [Theory]
    [InlineData(ImpactLevel.None)]
    [InlineData(ImpactLevel.Low)]
    [InlineData(ImpactLevel.Medium)]
    [InlineData(ImpactLevel.High)]
    public async Task AllCiaLevelsRoundTripAndCanReturnToUnknownWithoutLosingHistory(ImpactLevel level)
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        var assessed=await Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,new(level,level,level,"Tres dimensiones evaluadas",1)));
        Assert.True(assessed.IsImpactAssessmentComplete);
        var reset=await Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,new(null,null,null,"Se requiere nueva evidencia",2)));
        Assert.False(reset.IsImpactAssessmentComplete);Assert.Null(reset.ConfidentialityImpact);
        var events=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items;
        Assert.Equal(level,events[1].ConfidentialityImpact);Assert.Equal(level,events[1].IntegrityImpact);Assert.Equal(level,events[1].AvailabilityImpact);
        Assert.Null(events[2].ConfidentialityImpact);Assert.Null(events[2].IntegrityImpact);Assert.Null(events[2].AvailabilityImpact);
    }

    [Fact]
    public async Task EveryLaterIncidentMutationPreservesCurrentCiaSnapshot()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);var capability=await CapabilityId();
        await Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,Cia()));
        await Incidents(s=>s.UpdateAsync(seed.Admin,seed.Org.Id,incident.Id,new(incident.CategoryId,"Título corregido",null,"Corrección",2)));
        await Incidents(s=>s.AssignAsync(seed.Admin,seed.Org.Id,incident.Id,new(seed.Membership,"Asignar",3)));
        await Incidents(s=>s.ReportAssetAsync(seed.Admin,seed.Org.Id,incident.Id,Reported(4)));
        await Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,incident.Id,capability,Assessment(5)));
        var history=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items;
        Assert.Null(history[0].ConfidentialityImpact);
        Assert.All(history.Skip(1),e=>{Assert.Equal(ImpactLevel.High,e.ConfidentialityImpact);Assert.Equal(ImpactLevel.None,e.IntegrityImpact);Assert.Equal(ImpactLevel.Low,e.AvailabilityImpact);});
        Assert.Equal(6,(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id))).Revision);
        var audit=await InScope((_,db)=>db.AuditEvents.SingleAsync(e=>e.Action=="incident.impact_assessed"));
        Assert.Equal(history[1].CorrelationId,audit.CorrelationId);Assert.Equal(incident.Id,audit.ResourceId);
        Assert.DoesNotContain(Cia().Reason,JsonSerializer.Serialize(audit));
        var protectedError=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync("UPDATE app.incident_events SET confidentiality_impact='NONE'")));
        Assert.Equal("42501",protectedError.SqlState);
    }

    [Theory]
    [InlineData(OrganizationRole.OrgAdmin)]
    [InlineData(OrganizationRole.Analyst)]
    public async Task ActiveIncidentWritersCanAssessCia(OrganizationRole role)
    {
        var seed=await Seed();await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,role)));
        var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        var assessed=await Incidents(s=>s.AssessImpactAsync(seed.Platform,seed.Org.Id,incident.Id,Cia()));
        Assert.True(assessed.IsImpactAssessmentComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerOrPlatformWithoutMembershipCannotAssessCia(bool viewer)
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        if(viewer){await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.Viewer)));await Incidents(s=>s.GetAsync(seed.Platform,seed.Org.Id,incident.Id));}
        await Rejected(403,()=>Incidents(s=>s.AssessImpactAsync(seed.Platform,seed.Org.Id,incident.Id,Cia())));
        Assert.Null((await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id))).ConfidentialityImpact);
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("organization")]
    [InlineData("account")]
    [InlineData("lockout")]
    public async Task CiaWritesRejectInactiveAuthorizationContext(string condition)
    {
        var seed=await Seed();var member=await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.Analyst)));
        var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        if(condition=="membership")await Act(s=>s.ChangeMemberAsync(seed.Admin,seed.Org.Id,member.Id,new(OrganizationRole.Analyst,MembershipStatus.Suspended,null)));
        else await InScope(async (_,db)=>
        {
            if(condition=="organization") (await db.Organizations.SingleAsync()).Status=OrganizationStatus.Suspended;
            else{var user=await db.Users.SingleAsync(u=>u.Id==seed.Platform);if(condition=="account")user.IsActive=false;else user.LockoutEnd=DateTimeOffset.UtcNow.AddMinutes(15);}
            return await db.SaveChangesAsync();
        });
        await Rejected(403,()=>Incidents(s=>s.AssessImpactAsync(seed.Platform,seed.Org.Id,incident.Id,Cia())));
        Assert.Equal(1,await InScope((_,db)=>db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task CiaAssessmentCannotCrossOrganizationBoundary()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        var beta=await Act(s=>s.CreateOrganizationAsync(seed.Platform,new("Beta","beta","CL",Account("beta"))));
        var betaAdmin=await InScope((_,db)=>db.OrganizationMemberships.Where(m=>m.OrganizationId==beta.Id).Select(m=>m.UserId).SingleAsync());
        await Rejected(404,()=>Incidents(s=>s.AssessImpactAsync(betaAdmin,beta.Id,incident.Id,Cia())));
        await Rejected(403,()=>Incidents(s=>s.AssessImpactAsync(betaAdmin,seed.Org.Id,incident.Id,Cia())));
        Assert.Equal(1,(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id))).Revision);
    }

    [Fact]
    public async Task InvalidCiaAndReasonsDoNotAdvanceRevision()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        foreach(var request in new[]{Cia() with{ConfidentialityImpact=(ImpactLevel)999},Cia() with{IntegrityImpact=(ImpactLevel)999},Cia() with{AvailabilityImpact=(ImpactLevel)999},Cia() with{Reason=" "},Cia() with{Reason=new string('x',2001)}})
            await Rejected(400,()=>Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,request)));
        await Rejected(409,()=>Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,Cia(0))));
        Assert.Equal(1,(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id))).Revision);
        Assert.Equal(1,await InScope((_,db)=>db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task ConcurrentCiaAssessmentsCannotMixDimensionsOrOverwriteSameRevision()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        async Task<int> Assess(bool second)
        {
            try{await Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,second?new(ImpactLevel.Low,ImpactLevel.High,ImpactLevel.None,"Segunda evaluación",1):Cia()));return 200;}
            catch(IdentityOperationException e){return e.StatusCode;}
        }
        Assert.Equal(new[]{200,409},(await Race(()=>Assess(false),()=>Assess(true))).Order().ToArray());
        var current=await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id));
        var snapshot=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items[1];
        Assert.Equal(current.ConfidentialityImpact,snapshot.ConfidentialityImpact);
        Assert.Equal(current.IntegrityImpact,snapshot.IntegrityImpact);Assert.Equal(current.AvailabilityImpact,snapshot.AvailabilityImpact);
        Assert.True((current.ConfidentialityImpact==ImpactLevel.High && current.IntegrityImpact==ImpactLevel.None && current.AvailabilityImpact==ImpactLevel.Low)
            || (current.ConfidentialityImpact==ImpactLevel.Low && current.IntegrityImpact==ImpactLevel.High && current.AvailabilityImpact==ImpactLevel.None));
        Assert.Equal(2,current.Revision);
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task FailureOfEitherCiaHistoryRollsBackAllDimensions(bool reassessment,bool timeline)
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        if(reassessment)await Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,Cia()));
        var revision=reassessment?2:1;
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(timeline?"""
            CREATE FUNCTION app.reject_cia_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected CIA timeline failure'; END; $body$;
            CREATE TRIGGER reject_cia_test BEFORE INSERT ON app.incident_events FOR EACH ROW
            WHEN (NEW.action='IMPACT_ASSESSED') EXECUTE FUNCTION app.reject_cia_test();
            """:"""
            CREATE FUNCTION audit.reject_cia_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected CIA audit failure'; END; $body$;
            CREATE TRIGGER reject_cia_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
            WHEN (NEW.outcome='SUCCESS' AND NEW.action='incident.impact_assessed') EXECUTE FUNCTION audit.reject_cia_test();
            """));
        await Assert.ThrowsAsync<DbUpdateException>(()=>Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,new(null,null,null,"Revisar evidencia",revision))));
        var current=await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,incident.Id));
        Assert.Equal(revision,current.Revision);
        Assert.Equal(reassessment?ImpactLevel.High:(ImpactLevel?)null,current.ConfidentialityImpact);
        Assert.Equal(reassessment?ImpactLevel.None:(ImpactLevel?)null,current.IntegrityImpact);
        Assert.Equal(reassessment?ImpactLevel.Low:(ImpactLevel?)null,current.AvailabilityImpact);
        Assert.Equal(revision,await InScope((_,db)=>db.IncidentEvents.CountAsync()));
        Assert.Equal(1,await InScope((_,db)=>db.AuditEvents.CountAsync(e=>e.Outcome=="FAILURE")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseRejectsInvalidImpactOnIncidentAndHistoricalEvent(bool historical)
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        var error=await Assert.ThrowsAsync<DbUpdateException>(()=>InScope(async (_,db)=>
        {
            if(historical)db.IncidentEvents.Add(new(){OrganizationId=seed.Org.Id,IncidentId=incident.Id,Revision=2,ActorUserId=seed.Admin,
                Action="IMPACT_ASSESSED",Reason="SQL integrity",CategoryId=incident.CategoryId,Title="Invalid CIA",ConfidentialityImpact=(ImpactLevel)999});
            else (await db.Incidents.SingleAsync()).ConfidentialityImpact=(ImpactLevel)999;
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("23514",Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task CiaMigrationPreservesUnevaluatedHistoryAndRefusesLossAfterReset()
    {
        var seed=await Seed();var incident=await RegisterLegacyIncident(seed.Admin,seed.Org.Id);
        var before=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Single();
        await InScope(async (_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261022204102_IncidentCapabilityAssessments");await db.Database.MigrateAsync();return true;});
        var after=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,incident.Id))).Items.Single();
        Assert.Equal(before.Id,after.Id);Assert.Equal(before.OccurredAt,after.OccurredAt);
        Assert.Equal(before.Details.GetRawText(),after.Details.GetRawText());
        Assert.Null(after.ConfidentialityImpact);Assert.Null(after.IntegrityImpact);Assert.Null(after.AvailabilityImpact);
        await Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,Cia()));
        await Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,incident.Id,new(null,null,null,"Volver a investigar",2)));
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope(async (_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261022204102_IncidentCapabilityAssessments");return true;}));
        Assert.Contains("Cannot roll back CIA",error.MessageText);
        Assert.Equal(3,await InScope((_,db)=>db.IncidentEvents.CountAsync()));
    }

    [Fact]
    public async Task HttpCiaRequiresExplicitDimensionsAndRevisionAndDoesNotAcceptSeverityInput()
    {
        var seed=await Seed();var incident=await RegisterIncident(seed.Admin,seed.Org.Id);
        var route=$"/api/organizations/{seed.Org.Id}/incidents/{incident.Id}/impact";
        using var anonymous=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PutAsJsonAsync(route,Cia())).StatusCode);
        using var admin=Client();await Login(admin,"alpha");
        var valid=JsonSerializer.SerializeToElement(Cia(),InventoryJson);
        foreach(var missing in new[]{"confidentialityImpact","integrityImpact","availabilityImpact","expectedRevision"})
        {
            var body=valid.EnumerateObject().Where(p=>p.Name!=missing).ToDictionary(p=>p.Name,p=>(object)p.Value);
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(route,body)).StatusCode);
        }
        var response=await admin.PutAsJsonAsync(route,new{confidentialityImpact="None",integrityImpact=(string?)null,availabilityImpact="High",reason="HTTP CIA",expectedRevision=1,severity="Critical",maximumSeverityReached="Critical",status="CLOSED"});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var responseBody=await response.Content.ReadAsStringAsync();
        var item=JsonSerializer.Deserialize<IncidentItem>(responseBody,InventoryJson)!;
        Assert.Equal(ImpactLevel.None,item.ConfidentialityImpact);Assert.Null(item.IntegrityImpact);Assert.False(item.IsImpactAssessmentComplete);
        Assert.Equal("REGISTERED",item.Status);
        var raw=JsonSerializer.Deserialize<JsonElement>(responseBody);Assert.Equal(JsonValueKind.Null,raw.GetProperty("severity").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync(route,Cia())).StatusCode);
        var listed=(await admin.GetFromJsonAsync<IncidentPage<IncidentItem>>($"/api/organizations/{seed.Org.Id}/incidents",InventoryJson))!.Items.Single();
        Assert.Equal(ImpactLevel.High,listed.AvailabilityImpact);Assert.Null(listed.IntegrityImpact);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(route,Cia(2))).StatusCode);
    }
}
