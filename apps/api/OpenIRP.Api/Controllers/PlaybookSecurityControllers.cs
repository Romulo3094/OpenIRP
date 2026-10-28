using Microsoft.AspNetCore.Mvc;
using OpenIRP.Application.Executions;
using OpenIRP.Application.Playbooks;
namespace OpenIRP.Api.Controllers;
public sealed partial class PublicPlaybookDraftController
{
    [HttpPost("{playbook:guid}/versions/{version:guid}/security-withdrawals")]
    public async Task<ActionResult<SecurityHoldItem>> WithdrawForSecurity(Guid playbook,Guid version,PlaybookVersionCommand request,CancellationToken ct)=>
        Ok(await service.WithdrawForSecurityAsync(Actor,null,playbook,version,request,ct));
}
public sealed partial class PrivatePlaybookDraftController
{
    [HttpPost("{playbook:guid}/versions/{version:guid}/security-withdrawals")]
    public async Task<ActionResult<SecurityHoldItem>> WithdrawForSecurity(Guid organization,Guid playbook,Guid version,PlaybookVersionCommand request,CancellationToken ct)=>
        Ok(await service.WithdrawForSecurityAsync(Actor,organization,playbook,version,request,ct));
}
