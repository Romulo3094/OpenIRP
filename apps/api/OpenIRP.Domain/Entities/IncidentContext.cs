namespace OpenIRP.Domain.Entities;

// Observed scope, independent of the number of inventory associations.
public enum IncidentScope { Single, Multiple, OrganizationWide }
public enum OperationalImpact { None, Degraded, MajorInterruption, CriticalInterruption }
