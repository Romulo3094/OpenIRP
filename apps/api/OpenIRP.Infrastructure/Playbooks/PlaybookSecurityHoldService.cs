using Microsoft.EntityFrameworkCore;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Playbooks;
using OpenIRP.Domain.Entities;
namespace OpenIRP.Infrastructure.Playbooks;
public sealed partial class PlaybookDraftService
{
    public async Task<SecurityHoldItem> WithdrawForSecurityAsync(Guid actor, Guid? organization, Guid playbook, Guid version, PlaybookVersionCommand request, CancellationToken ct=default)
    {
        PlaybookSecurityHold? hold=null;
        await Write(actor,organization,playbook,"playbook.security_withdrawn",async()=>
        {
            var entity=await Find(organization,playbook,request.ExpectedRevision,request.Reason,ct);
            var item=await FindVersion(playbook,version,ct);
            if(item.Status is not ("PUBLISHED" or "ARCHIVED"))throw Error(409,"security_withdrawal_requires_published_version");
            if(item.Status=="PUBLISHED")
            {
                item.Status="ARCHIVED";
                Review(entity,item,actor,"WITHDRAWN",request.Reason,Content(await View(entity,ct,version)));
            }
            if(entity.CurrentPublishedVersionId==version)entity.CurrentPublishedVersionId=null;
            hold=new(){PlaybookId=playbook,VersionId=version,Revision=entity.Revision,ActorUserId=actor,CorrelationId=context.CorrelationId,Reason=request.Reason.Trim()};
            db.PlaybookSecurityHolds.Add(hold);
            return(entity,item);
        },ct);
        return new(hold!.Id,version,hold.Revision,actor,hold.Reason,hold.OccurredAt);
    }
}
