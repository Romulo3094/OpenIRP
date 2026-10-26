namespace OpenIRP.Domain.Entities;

public sealed class Playbook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OrganizationId { get; set; }
    public Guid? CurrentPublishedVersionId { get; set; }
    public Guid? SourceVersionId { get; set; }
    public long? SourceRevision { get; set; }
    public string? CloneReason { get; set; }
    public Guid CategoryId { get; set; }
    public string Title { get; set; } = "";
    public long Revision { get; set; } = 1;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PlaybookVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlaybookId { get; set; }
    public int VersionNumber { get; set; } = 1;
    public string Title { get; set; } = "";
    public Guid CategoryId { get; set; }
    public Guid? SourceVersionId { get; set; }
    public Guid? ReviewRequestId { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string Objective { get; set; } = "";
    public string Instructions { get; set; } = "";
}

public sealed class PlaybookStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public string Code { get; set; } = "";
    public int Position { get; set; }
    public string Phase { get; set; } = "";
    public string Instructions { get; set; } = "";
    public bool IsRequired { get; set; }
    public bool RequiresApproval { get; set; }
    public bool RequiresEvidence { get; set; }
}

public sealed class PlaybookStepDependency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public Guid PredecessorId { get; set; }
    public Guid SuccessorId { get; set; }
}

public enum PlaybookRequirementLevel { Required, Recommended }

public sealed class PlaybookCapabilityRequirement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VersionId { get; set; }
    public Guid? StepId { get; set; }
    public Guid CapabilityId { get; set; }
    public PlaybookRequirementLevel Level { get; set; }
}
