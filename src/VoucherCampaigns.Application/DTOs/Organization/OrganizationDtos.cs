namespace VoucherCampaigns.Application.DTOs.Organization;

public record CreateNetworkRequest(string Name);

public record UpdateNetworkRequest(string Name, bool IsActive);

public record NetworkResponse(Guid Id, string Name, bool IsActive, DateTime CreatedAt);

public record CreateRegionRequest(Guid NetworkId, string Name);

public record UpdateRegionRequest(string Name, bool IsActive);

public record RegionResponse(Guid Id, Guid NetworkId, string Name, bool IsActive, DateTime CreatedAt);

public record CreateEstablishmentRequest(Guid RegionId, string Code, string Name);

public record UpdateEstablishmentRequest(string Code, string Name, bool IsActive);

public record EstablishmentResponse(Guid Id, Guid RegionId, string Code, string Name, bool IsActive, DateTime CreatedAt);

public record CreateChannelRequest(Guid EstablishmentId, string Type);

public record UpdateChannelRequest(bool IsActive);

public record ChannelResponse(Guid Id, Guid EstablishmentId, string Type, bool IsActive, DateTime CreatedAt);
