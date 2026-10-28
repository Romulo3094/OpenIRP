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
    private Task<IncidentItem> MoveWorkflow(Guid actor, Guid org, IncidentItem current, IncidentState target, string? resolution=null) =>
        Incidents(s=>s.TransitionAsync(actor,org,current.Id,new(target,resolution,"Transición justificada",current.Revision)));

    private async Task<IncidentItem> ResolvedForClosure(Guid actor,Guid org,Guid membership,bool high=true,IncidentItem? existing=null,bool reviewed=true)
    {
        var current=existing??await RegisterIncident(actor,org);
        current=await CompleteSeverityInputs(actor,org,current,high?ImpactLevel.High:ImpactLevel.None,high?IncidentCriticality.High:IncidentCriticality.Low);
        current=await Incidents(s=>s.AssignAsync(actor,org,current.Id,new(membership,"Asignación",current.Revision)));
        current=await MoveWorkflow(actor,org,current,IncidentState.UnderEvaluation);
        current=await MoveWorkflow(actor,org,current,IncidentState.InResponse);
        current = await MoveWorkflow(actor,org,current,IncidentState.Resolved,"Resolución privada con evidencia");
        return reviewed ? await AcceptRiskForTest(actor, org, current) : current;
    }

    private async Task<IncidentItem> AskClosure(Guid actor,Guid org,IncidentItem current)
    {
        var state = await ClosureRead(actor, org, current.Id);
        if (!state.RiskReview.AcceptanceCurrent) current = await AcceptRiskForTest(actor, org, current);
        return await Incidents(s=>s.RequestClosureAsync(actor,org,current.Id,new("Solicitar revisión de cierre",current.Revision)));
    }
    private Task<IncidentItem> ReviewClosure(Guid actor,Guid org,IncidentItem current,bool approve=true,bool self=false,string? justification=null) =>
        Incidents(s=>s.DecideClosureAsync(actor,org,current.Id,current.ClosureReview!.Id,new(approve,self,justification,"Decisión de cierre privada",current.Revision)));

    [Fact, Trait("Feature","IncidentWorkflow")]
    public async Task LowIncidentCanBeResolvedAndClosedOnlyByItsActiveAssignedWriter()
    {
        var seed=await Seed();var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership,false);
        Assert.Equal("RESOLVED",current.Status);Assert.Equal(IncidentSeverity.Low,current.MaximumSeverityReached);
        await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.OrgAdmin)));
        await Rejected(409,()=>MoveWorkflow(seed.Platform,seed.Org.Id,current,IncidentState.Closed));
        Assert.False((await ClosureRead(seed.Admin,seed.Org.Id,current.Id)).RiskReview.AcceptanceCurrent);
        current=await AcceptRiskForTest(seed.Admin,seed.Org.Id,current);
        var closed=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.Closed);
        Assert.Equal("CLOSED",closed.Status);Assert.NotNull(closed.Resolution);Assert.Null(closed.ClosureReview);
        var history=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,current.Id))).Items;
        Assert.Equal("RESOLVED",history[^2].Status);Assert.Equal("CLOSED",history[^1].Status);
        Assert.Equal("Resolución privada con evidencia",history[^1].Resolution);
        var audit=await InScope((_,db)=>db.AuditEvents.Where(e=>e.Action=="incident.state_changed").OrderByDescending(e=>e.Id).FirstAsync());
        Assert.DoesNotContain(closed.Resolution!,JsonSerializer.Serialize(audit));
    }

    [Fact, Trait("Feature","IncidentWorkflow")]
    public async Task HighHistoricalMaximumRequiresAdminApprovalEvenWhenCurrentSeverityIsLow()
    {
        var seed=await Seed();var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        current=await Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,current.Id,new(ImpactLevel.None,ImpactLevel.None,ImpactLevel.None,"Impacto reevaluado",current.Revision)));
        Assert.Equal(IncidentSeverity.Low,current.Severity);Assert.Equal(IncidentSeverity.Critical,current.MaximumSeverityReached);
        await Rejected(409,()=>MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.Closed));
        await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.OrgAdmin)));
        var pending=await AskClosure(seed.Admin,seed.Org.Id,current);
        Assert.Equal(ClosureReviewStatus.Pending,pending.ClosureReview!.Status);
        var closed=await ReviewClosure(seed.Platform,seed.Org.Id,pending);
        Assert.Equal("CLOSED",closed.Status);Assert.Equal(ClosureReviewStatus.Approved,closed.ClosureReview!.Status);
        Assert.False(closed.ClosureReview.SelfApproval);Assert.Equal(seed.Platform,closed.ClosureReview.DecidedByUserId);
        Assert.Equal(pending.Revision,closed.ClosureReview.RequestedAtRevision);
        Assert.Equal(closed.Revision,closed.ClosureReview.DecisionRevision);
    }

    [Fact, Trait("Feature","IncidentWorkflow")]
    public async Task RejectedRequestCanBeReplacedWithoutReusingItsApprovalIdentity()
    {
        var seed=await Seed();await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.OrgAdmin)));
        var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        var pending=await AskClosure(seed.Admin,seed.Org.Id,current);
        await Rejected(409,()=>AskClosure(seed.Admin,seed.Org.Id,pending));
        var rejected=await ReviewClosure(seed.Platform,seed.Org.Id,pending,false);
        Assert.Equal("RESOLVED",rejected.Status);Assert.Equal(ClosureReviewStatus.Rejected,rejected.ClosureReview!.Status);
        var replacement=await AskClosure(seed.Admin,seed.Org.Id,rejected);
        Assert.NotEqual(pending.ClosureReview!.Id,replacement.ClosureReview!.Id);
        await Rejected(404,()=>Incidents(s=>s.DecideClosureAsync(seed.Platform,seed.Org.Id,current.Id,pending.ClosureReview.Id,new(true,false,null,"Solicitud anterior",replacement.Revision))));
        Assert.Equal("CLOSED",(await ReviewClosure(seed.Platform,seed.Org.Id,replacement)).Status);
    }

    [Theory, Trait("Feature","IncidentWorkflow")]
    [InlineData(false,null)]
    [InlineData(true,null)]
    [InlineData(true," ")]
    public async Task SelfApprovalNeedsExplicitExceptionAndSeparateJustification(bool self,string? justification)
    {
        var seed=await Seed();var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        var pending=await AskClosure(seed.Admin,seed.Org.Id,current);
        await Rejected(400,()=>ReviewClosure(seed.Admin,seed.Org.Id,pending,true,self,justification));
        Assert.Equal(ClosureReviewStatus.Pending,(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id))).ClosureReview!.Status);
    }

    [Fact, Trait("Feature","IncidentWorkflow")]
    public async Task ExplicitSelfApprovalClosesAtomicallyAndReopeningInvalidatesTheSpentDecision()
    {
        var seed=await Seed();var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        var pending=await AskClosure(seed.Admin,seed.Org.Id,current);
        var closed=await ReviewClosure(seed.Admin,seed.Org.Id,pending,true,true,"Excepción: único administrador disponible y evidencia revisada");
        Assert.True(closed.ClosureReview!.SelfApproval);
        var reopened=await MoveWorkflow(seed.Admin,seed.Org.Id,closed,IncidentState.UnderEvaluation);
        Assert.Null(reopened.Resolution);Assert.Equal(ClosureReviewStatus.Invalidated,reopened.ClosureReview!.Status);
        Assert.False(reopened.ClosureReview.SelfApproval);Assert.Equal(IncidentSeverity.Critical,reopened.MaximumSeverityReached);
        var history=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,current.Id))).Items;
        var approval=history.Single(e=>e.Action=="CLOSURE_APPROVED");
        Assert.True(approval.ClosureReview!.SelfApproval);Assert.NotNull(approval.ClosureReview.SelfApprovalJustification);
        var edited=await Incidents(s=>s.UpdateAsync(seed.Admin,seed.Org.Id,current.Id,new(current.CategoryId,"Después de reapertura",null,"Corrección",reopened.Revision)));
        Assert.Equal("UNDER_EVALUATION",edited.Status);
    }

    [Theory, Trait("Feature","IncidentWorkflow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingOrNotEvaluatedIncidentsCannotCloseOrRequestApproval(bool legacy)
    {
        var seed=await Seed();var current=legacy?await RegisterLegacyIncident(seed.Admin,seed.Org.Id):await RegisterIncident(seed.Admin,seed.Org.Id);
        current=await Incidents(s=>s.AssignAsync(seed.Admin,seed.Org.Id,current.Id,new(seed.Membership,"Asignar",current.Revision)));
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.UnderEvaluation);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.InResponse);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.Resolved,"Resolución con evaluación pendiente");
        await Rejected(409,()=>MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.Closed));
        await Rejected(409,()=>AskClosure(seed.Admin,seed.Org.Id,current));
        Assert.Equal("RESOLVED",(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id))).Status);
    }

    [Theory, Trait("Feature","IncidentWorkflow")]
    [InlineData("update")]
    [InlineData("assignment")]
    [InlineData("cia")]
    [InlineData("context")]
    [InlineData("criticality")]
    [InlineData("severity")]
    [InlineData("capability")]
    public async Task OtherIncidentWritesInvalidatePendingClosureReview(string operation)
    {
        var seed=await Seed();await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.OrgAdmin)));
        var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        var pending=await AskClosure(seed.Admin,seed.Org.Id,current);var revision=pending.Revision;
        if(operation=="update")await Incidents(s=>s.UpdateAsync(seed.Admin,seed.Org.Id,current.Id,new(current.CategoryId,"Nuevo título",null,"Corregir",revision)));
        else if(operation=="assignment")await Incidents(s=>s.AssignAsync(seed.Admin,seed.Org.Id,current.Id,new(seed.Membership,"Revalidar asignación",revision)));
        else if(operation=="cia")await Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,current.Id,Cia(revision)));
        else if(operation=="context")await Incidents(s=>s.AssessContextAsync(seed.Admin,seed.Org.Id,current.Id,ContextAssessment(revision)));
        else if(operation=="criticality")await Incidents(s=>s.AssessCriticalityAsync(seed.Admin,seed.Org.Id,current.Id,CriticalityAssessment(revision)));
        else if(operation=="severity")await Incidents(s=>s.AssessSeverityAsync(seed.Admin,seed.Org.Id,current.Id,new("Reevaluar",revision)));
        else{var cap=await CapabilityId();await Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,current.Id,cap,Assessment(revision)));}
        var changed=await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id));
        Assert.Equal(ClosureReviewStatus.Invalidated,changed.ClosureReview!.Status);
        await Rejected(409,()=>ReviewClosure(seed.Platform,seed.Org.Id,changed));
        var previous=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,current.Id))).Items.Single(e=>e.Action=="CLOSURE_REQUESTED");
        Assert.Equal(ClosureReviewStatus.Pending,previous.ClosureReview!.Status);
    }

    [Theory, Trait("Feature","IncidentWorkflow")]
    [InlineData("none")]
    [InlineData("analyst")]
    [InlineData("viewer")]
    [InlineData("suspended")]
    [InlineData("account")]
    [InlineData("lockout")]
    public async Task ClosureReviewerMustBeAnActiveOrganizationAdmin(string condition)
    {
        var seed=await Seed();var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        var pending=await AskClosure(seed.Admin,seed.Org.Id,current);
        if(condition!="none")
        {
            var role=condition=="analyst"?OrganizationRole.Analyst:condition=="viewer"?OrganizationRole.Viewer:OrganizationRole.OrgAdmin;
            var member=await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,role)));
            if(condition=="suspended")await Act(s=>s.ChangeMemberAsync(seed.Admin,seed.Org.Id,member.Id,new(role,MembershipStatus.Suspended,null)));
            if(condition is "account" or "lockout")await InScope(async (_,db)=>{var user=await db.Users.SingleAsync(u=>u.Id==seed.Platform);if(condition=="account")user.IsActive=false;else user.LockoutEnd=DateTimeOffset.UtcNow.AddMinutes(15);return await db.SaveChangesAsync();});
        }
        await Rejected(403,()=>ReviewClosure(seed.Platform,seed.Org.Id,pending));
        Assert.Equal("RESOLVED",(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id))).Status);
    }

    [Theory, Trait("Feature","IncidentWorkflow")]
    [InlineData("membership")]
    [InlineData("account")]
    [InlineData("lockout")]
    [InlineData("reassigned")]
    public async Task ApprovalRevalidatesTheRequesterAndCurrentAssignment(string condition)
    {
        var seed=await Seed();var reviewer=await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.OrgAdmin)));
        var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        var pending=await AskClosure(seed.Admin,seed.Org.Id,current);
        if(condition=="membership")await Act(s=>s.ChangeMemberAsync(seed.Platform,seed.Org.Id,seed.Membership,new(OrganizationRole.OrgAdmin,MembershipStatus.Suspended,null)));
        else if(condition=="reassigned")pending=await Incidents(s=>s.AssignAsync(seed.Platform,seed.Org.Id,current.Id,new(reviewer.Id,"Nuevo responsable",pending.Revision)));
        else await InScope(async (_,db)=>{var user=await db.Users.SingleAsync(u=>u.Id==seed.Admin);if(condition=="account")user.IsActive=false;else user.LockoutEnd=DateTimeOffset.UtcNow.AddMinutes(15);return await db.SaveChangesAsync();});
        await Rejected(409,()=>ReviewClosure(seed.Platform,seed.Org.Id,pending));
        Assert.Equal("RESOLVED",(await Incidents(s=>s.GetAsync(seed.Platform,seed.Org.Id,current.Id))).Status);
    }

    [Theory, Trait("Feature","IncidentWorkflow")]
    [InlineData(OrganizationRole.OrgAdmin)]
    [InlineData(OrganizationRole.Analyst)]
    public async Task ActiveAssignedOperationalWritersCanCloseLowSeverity(OrganizationRole role)
    {
        var seed=await Seed();var member=await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,role)));
        var current=await ResolvedForClosure(seed.Platform,seed.Org.Id,member.Id,false);
        Assert.Equal("CLOSED",(await MoveWorkflow(seed.Platform,seed.Org.Id,current,IncidentState.Closed)).Status);
    }

    [Fact, Trait("Feature","IncidentWorkflow")]
    public async Task DiscardAndBacktrackingRequireLegalTransitionsAndRetainHistory()
    {
        var seed=await Seed();var current=await RegisterIncident(seed.Admin,seed.Org.Id);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.UnderEvaluation);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.InResponse);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.UnderEvaluation);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.Discarded);
        Assert.Equal("DISCARDED",current.Status);Assert.Null(current.Resolution);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.UnderEvaluation);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.InResponse);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.Resolved,"Resolución revisada");
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.InResponse);
        Assert.Null(current.Resolution);
        var history=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,current.Id))).Items;
        Assert.Contains(history,e=>e.Status=="DISCARDED");Assert.Contains(history,e=>e.Resolution=="Resolución revisada");
    }

    [Fact, Trait("Feature","IncidentWorkflow")]
    public async Task InvalidTransitionsReasonsAndResolutionsNeverAdvanceRevision()
    {
        var seed=await Seed();var current=await RegisterIncident(seed.Admin,seed.Org.Id);
        foreach(var target in new[]{IncidentState.Registered,IncidentState.InResponse,IncidentState.Resolved,IncidentState.Closed,IncidentState.Discarded})
            await Rejected(409,()=>MoveWorkflow(seed.Admin,seed.Org.Id,current,target));
        await Rejected(400,()=>MoveWorkflow(seed.Admin,seed.Org.Id,current,(IncidentState)999));
        await Rejected(400,()=>MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.UnderEvaluation,"Texto no aplicable"));
        await Rejected(400,()=>Incidents(s=>s.TransitionAsync(seed.Admin,seed.Org.Id,current.Id,new(IncidentState.UnderEvaluation,null," ",current.Revision))));
        await Rejected(409,()=>Incidents(s=>s.TransitionAsync(seed.Admin,seed.Org.Id,current.Id,new(IncidentState.UnderEvaluation,null,"Revisión obsoleta",0))));
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.UnderEvaluation);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.InResponse);
        foreach(var resolution in new string?[]{null," ",new string('x',4001)})
            await Rejected(400,()=>MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.Resolved,resolution));
        Assert.Equal(3,(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id))).Revision);
    }

    [Theory, Trait("Feature","IncidentWorkflow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalIncidentsRejectEveryOrdinaryMutationUntilReopened(bool closed)
    {
        var seed=await Seed();var current=await RegisterIncident(seed.Admin,seed.Org.Id);
        var report=await Incidents(s=>s.ReportAssetAsync(seed.Admin,seed.Org.Id,current.Id,Reported()));
        var asset=await Inventory(s=>s.CreateAssetAsync(seed.Admin,seed.Org.Id,SampleAsset("Activo para comprobar bloqueo")));
        current=current with{Revision=2};
        if(closed)
        {
            current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership,true,current);
            current=await AskClosure(seed.Admin,seed.Org.Id,current);
            current=await ReviewClosure(seed.Admin,seed.Org.Id,current,true,true,"Excepción expresamente justificada");
        }
        else
        {
            current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.UnderEvaluation);
            current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.Discarded);
        }
        var revision=current.Revision;var capability=await CapabilityId();
        await Rejected(409,()=>Incidents(s=>s.UpdateAsync(seed.Admin,seed.Org.Id,current.Id,new(current.CategoryId,"Bloqueado",null,"Editar",revision))));
        await Rejected(409,()=>Incidents(s=>s.AssignAsync(seed.Admin,seed.Org.Id,current.Id,new(null,"Desasignar",revision))));
        await Rejected(409,()=>Incidents(s=>s.AssessImpactAsync(seed.Admin,seed.Org.Id,current.Id,Cia(revision))));
        await Rejected(409,()=>Incidents(s=>s.AssessContextAsync(seed.Admin,seed.Org.Id,current.Id,ContextAssessment(revision))));
        await Rejected(409,()=>Incidents(s=>s.AssessCriticalityAsync(seed.Admin,seed.Org.Id,current.Id,CriticalityAssessment(revision))));
        await Rejected(409,()=>Incidents(s=>s.AssessSeverityAsync(seed.Admin,seed.Org.Id,current.Id,new("Recalcular",revision))));
        await Rejected(409,()=>Incidents(s=>s.AssessCapabilityAsync(seed.Admin,seed.Org.Id,current.Id,capability,Assessment(revision))));
        await Rejected(409,()=>Incidents(s=>s.ReportAssetAsync(seed.Admin,seed.Org.Id,current.Id,Reported(revision))));
        await Rejected(409,()=>Incidents(s=>s.AttachAssetAsync(seed.Admin,seed.Org.Id,current.Id,new(asset.Id,null,"Adjuntar",revision))));
        await Rejected(409,()=>Incidents(s=>s.LinkAssetAsync(seed.Admin,seed.Org.Id,current.Id,report.Asset.Id,new(asset.Id,"Vincular",revision))));
        Assert.Equal(revision,(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id))).Revision);
        Assert.Null((await Incidents(s=>s.AssetsAsync(seed.Admin,seed.Org.Id,current.Id))).Items.Single().AssetId);
    }

    [Fact, Trait("Feature","IncidentWorkflow")]
    public async Task ClosureRequestsAndTransitionsCannotCrossOrganizationsOrIncidents()
    {
        var seed=await Seed();var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        var pending=await AskClosure(seed.Admin,seed.Org.Id,current);
        var beta=await Act(s=>s.CreateOrganizationAsync(seed.Platform,new("Beta","beta","CL",Account("beta"))));
        var membership=await InScope((_,db)=>db.OrganizationMemberships.SingleAsync(m=>m.OrganizationId==beta.Id));
        await Rejected(403,()=>MoveWorkflow(membership.UserId,seed.Org.Id,current,IncidentState.InResponse));
        await Rejected(404,()=>Incidents(s=>s.RequestClosureAsync(membership.UserId,beta.Id,current.Id,new("Incidente ajeno",current.Revision))));
        var other=await ResolvedForClosure(membership.UserId,beta.Id,membership.Id);
        var own=await AskClosure(membership.UserId,beta.Id,other);
        await Rejected(404,()=>Incidents(s=>s.DecideClosureAsync(membership.UserId,beta.Id,other.Id,pending.ClosureReview!.Id,new(true,true,"No debe asociarse a otra empresa","Control tenant",own.Revision))));
        Assert.Equal("RESOLVED",(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id))).Status);
    }

    [Theory, Trait("Feature","IncidentWorkflow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RacingApprovalWithMutationOrAnotherApprovalCannotCloseAnObsoleteRevision(bool secondApproval)
    {
        var seed=await Seed();await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.OrgAdmin)));
        var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        var pending=await AskClosure(seed.Admin,seed.Org.Id,current);
        async Task<int> Change(bool second)
        {
            try
            {
                if(!second)await ReviewClosure(seed.Platform,seed.Org.Id,pending);
                else if(secondApproval)await ReviewClosure(seed.Admin,seed.Org.Id,pending,true,true,"Autoaprobación excepcional");
                else await Incidents(s=>s.UpdateAsync(seed.Admin,seed.Org.Id,current.Id,new(current.CategoryId,"Cambio simultáneo",null,"Corrección",pending.Revision)));
                return 200;
            }
            catch(IdentityOperationException e){return e.StatusCode;}
        }
        Assert.Equal(new[]{200,409},(await Race(()=>Change(false),()=>Change(true))).Order().ToArray());
        var after=await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id));
        Assert.Equal(pending.Revision+1,after.Revision);
        if(after.Status=="CLOSED")Assert.Equal(ClosureReviewStatus.Approved,after.ClosureReview!.Status);
        else{Assert.False(secondApproval);Assert.Equal("RESOLVED",after.Status);Assert.Equal(ClosureReviewStatus.Invalidated,after.ClosureReview!.Status);}
        Assert.Equal(IncidentSeverity.Critical,after.MaximumSeverityReached);
    }

    [Theory, Trait("Feature","IncidentWorkflow")]
    [InlineData("transition",false)]
    [InlineData("transition",true)]
    [InlineData("request",false)]
    [InlineData("request",true)]
    [InlineData("approve",false)]
    [InlineData("approve",true)]
    public async Task WorkflowAndClosureRollBackIfEitherHistoryFails(string operation,bool timeline)
    {
        var seed=await Seed();await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.OrgAdmin)));
        var current=operation=="transition"?await RegisterIncident(seed.Admin,seed.Org.Id):await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        if(operation=="approve")current=await AskClosure(seed.Admin,seed.Org.Id,current);
        var before=await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id));
        var events=await InScope((_,db)=>db.IncidentEvents.CountAsync());
        await InScope((_,db)=>db.Database.ExecuteSqlRawAsync(timeline?"""
            CREATE FUNCTION app.reject_workflow_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected workflow history failure'; END; $body$;
            CREATE TRIGGER reject_workflow_test BEFORE INSERT ON app.incident_events FOR EACH ROW
            EXECUTE FUNCTION app.reject_workflow_test();
            """:"""
            CREATE FUNCTION audit.reject_workflow_test() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'Injected workflow audit failure'; END; $body$;
            CREATE TRIGGER reject_workflow_test BEFORE INSERT ON audit.audit_events FOR EACH ROW
            WHEN (NEW.outcome='SUCCESS') EXECUTE FUNCTION audit.reject_workflow_test();
            """));
        await Assert.ThrowsAsync<DbUpdateException>(async()=>
        {
            if(operation=="transition")await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.UnderEvaluation);
            else if(operation=="request")await AskClosure(seed.Admin,seed.Org.Id,current);
            else await ReviewClosure(seed.Platform,seed.Org.Id,current);
        });
        Assert.Equal(before,await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id)));
        Assert.Equal(events,await InScope((_,db)=>db.IncidentEvents.CountAsync()));
    }

    [Theory, Trait("Feature","IncidentWorkflow")]
    [InlineData("jump")]
    [InlineData("approval")]
    [InlineData("terminal")]
    public async Task DatabaseBlocksIllegalStateJumpsMissingApprovalAndTerminalEdits(string invalid)
    {
        var seed=await Seed();var current=invalid=="jump"?await RegisterIncident(seed.Admin,seed.Org.Id):await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        if(invalid=="terminal"){current=await AskClosure(seed.Admin,seed.Org.Id,current);current=await ReviewClosure(seed.Admin,seed.Org.Id,current,true,true,"Excepción revisada");}
        var sql=invalid=="jump"?"UPDATE app.incidents SET status='IN_RESPONSE'":
            invalid=="approval"?"UPDATE app.incidents SET status='CLOSED',revision=revision+1":"UPDATE app.incidents SET title='Edición de cerrado'";
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync(sql)));
        Assert.Equal("23514",error.SqlState);
        Assert.Equal(current.Status,(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id))).Status);
        var historyError=await Assert.ThrowsAsync<PostgresException>(()=>InScope((_,db)=>db.Database.ExecuteSqlRawAsync("UPDATE app.incident_events SET resolution='Historia alterada'")));
        Assert.Equal("42501",historyError.SqlState);
    }

    [Fact, Trait("Feature","IncidentWorkflow")]
    public async Task WorkflowMigrationPreservesOldSnapshotsAndRefusesLossAfterDiscardAndReopen()
    {
        var seed=await Seed();var current=await RegisterIncident(seed.Admin,seed.Org.Id);
        var before=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,current.Id))).Items.Single();
        await InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261023202856_IncidentSeverityClassification");await db.Database.MigrateAsync();return true;});
        var after=(await Incidents(s=>s.EventsAsync(seed.Admin,seed.Org.Id,current.Id))).Items.Single();
        Assert.Equal(before.Id,after.Id);Assert.Equal(before.OccurredAt,after.OccurredAt);Assert.Equal(before.Details.GetRawText(),after.Details.GetRawText());
        Assert.Null(after.Resolution);Assert.Null(after.ClosureReview);Assert.Equal(before.SeverityRuleVersion,after.SeverityRuleVersion);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.UnderEvaluation);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.Discarded);
        current=await MoveWorkflow(seed.Admin,seed.Org.Id,current,IncidentState.UnderEvaluation);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>InScope(async(_,db)=>{await db.GetService<IMigrator>().MigrateAsync("20261023202856_IncidentSeverityClassification");return true;}));
        Assert.Contains("Cannot roll back incident lifecycle",error.MessageText);
        Assert.Equal(4,await InScope((_,db)=>db.IncidentEvents.CountAsync()));
    }

    [Fact, Trait("Feature","IncidentWorkflow")]
    public async Task MisdeclaredSelfApprovalOrOverlongJustificationCannotClose()
    {
        var seed=await Seed();await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.OrgAdmin)));
        var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        var pending=await AskClosure(seed.Admin,seed.Org.Id,current);
        await Rejected(400,()=>ReviewClosure(seed.Platform,seed.Org.Id,pending,true,true,"No es autoaprobación"));
        await Rejected(400,()=>ReviewClosure(seed.Platform,seed.Org.Id,pending,true,false,"Excepción no aplicable"));
        await Rejected(400,()=>ReviewClosure(seed.Admin,seed.Org.Id,pending,true,true,new string('x',2001)));
        await Rejected(400,()=>ReviewClosure(seed.Admin,seed.Org.Id,pending,false,true,"Rechazar no es autoaprobar"));
        Assert.Equal("RESOLVED",(await Incidents(s=>s.GetAsync(seed.Admin,seed.Org.Id,current.Id))).Status);
    }

    [Fact, Trait("Feature","IncidentWorkflow")]
    public async Task HttpWorkflowRequiresExplicitFieldsCsrfAndCurrentAdminDecision()
    {
        var seed=await Seed();var reviewer=await Act(s=>s.AddMemberAsync(seed.Admin,seed.Org.Id,new(seed.Platform,OrganizationRole.OrgAdmin)));
        var current=await ResolvedForClosure(seed.Admin,seed.Org.Id,seed.Membership);
        var root=$"/api/organizations/{seed.Org.Id}/incidents/{current.Id}";
        using var anonymous=Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PutAsJsonAsync(root+"/state",new{targetState="Closed",reason="Anónimo",expectedRevision=current.Revision})).StatusCode);
        using var admin=Client();await Login(admin,"alpha");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(root+"/state",new{reason="Sin destino",expectedRevision=current.Revision})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(root+"/state",new{targetState="Closed",reason="Sin revisión"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync(root+"/state",new{targetState="Closed",reason="No puede forzar aprobación",expectedRevision=current.Revision,maximumSeverityReached="Low",closureReview=new{status="Approved"}})).StatusCode);
        var request=await admin.PostAsJsonAsync(root+"/closure-requests",new{reason="Revisión de cierre HTTP",expectedRevision=current.Revision});
        Assert.Equal(HttpStatusCode.Created,request.StatusCode);
        var pending=(await request.Content.ReadFromJsonAsync<IncidentItem>(InventoryJson))!;
        var route=root+"/closure-requests/"+pending.ClosureReview!.Id;
        using var platform=Client();await Login(platform,"platform");
        foreach(var missing in new[]{"approve","selfApproval","expectedRevision"})
        {
            var fields=new Dictionary<string,object>{{"approve",true},{"selfApproval",false},{"reason","Revisar"},{"expectedRevision",pending.Revision}};
            fields.Remove(missing);
            Assert.Equal(HttpStatusCode.BadRequest,(await platform.PutAsJsonAsync(route,fields)).StatusCode);
        }
        var response=await platform.PutAsJsonAsync(route,new{approve=true,selfApproval=false,reason="Cierre autorizado HTTP",expectedRevision=pending.Revision});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var closed=(await response.Content.ReadFromJsonAsync<IncidentItem>(InventoryJson))!;
        Assert.Equal("CLOSED",closed.Status);Assert.Equal(ClosureReviewStatus.Approved,closed.ClosureReview!.Status);
        Assert.Equal(HttpStatusCode.Conflict,(await platform.PutAsJsonAsync(route,new{approve=true,selfApproval=false,reason="Reutilizar aprobación",expectedRevision=closed.Revision})).StatusCode);
        await Act(s=>s.ChangeMemberAsync(seed.Admin,seed.Org.Id,reviewer.Id,new(OrganizationRole.Viewer,MembershipStatus.Active,null)));
        Assert.Equal(HttpStatusCode.OK,(await platform.GetAsync(root)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await platform.PutAsJsonAsync(root+"/state",new{targetState="UnderEvaluation",reason="Viewer no reabre",expectedRevision=closed.Revision})).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(root+"/state",new{targetState="UnderEvaluation",reason="Sin CSRF",expectedRevision=closed.Revision})).StatusCode);
    }
}
