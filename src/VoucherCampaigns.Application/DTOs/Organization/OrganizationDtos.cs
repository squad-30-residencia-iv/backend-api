using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Application.DTOs.Organization;

public static class OrganizationContract
{
    public const int NameMaxLength = 150;
    public const int TimeZoneMaxLength = 64;
    public const int EstablishmentCodeMaxLength = 20;
    public const int ChannelTypeMaxLength = 20;

    public const string NetworkNameRequired = "Informe o nome da rede.";
    public const string NameMaxLengthMessage = "O nome deve ter no máximo 150 caracteres.";
    public const string TimeZoneRequired = "Informe o fuso da rede.";
    public const string TimeZoneMaxLengthMessage = "O fuso deve ter no máximo 64 caracteres.";
    public const string TimeZoneInvalid = "Informe um fuso IANA válido.";
    public const string NetworkActiveRequired = "Informe se a rede está ativa.";
    public const string NetworkRequired = "Informe a rede.";
    public const string RegionNameRequired = "Informe o nome da região.";
    public const string RegionActiveRequired = "Informe se a região está ativa.";
    public const string RegionRequired = "Informe a região do estabelecimento.";
    public const string EstablishmentCodeRequired = "Informe o código do estabelecimento.";
    public const string EstablishmentCodeMaxLengthMessage = "O código deve ter no máximo 20 caracteres.";
    public const string EstablishmentNameRequired = "Informe o nome do estabelecimento.";
    public const string EstablishmentActiveRequired = "Informe se o estabelecimento está ativo.";
    public const string EstablishmentRequired = "Informe o estabelecimento do canal.";
    public const string ChannelTypeRequired = "Informe o tipo do canal.";
    public const string ChannelTypeInvalid = "Tipo de canal inválido. Use 'Physical' ou 'Virtual'.";
    public const string ChannelActiveRequired = "Informe se o canal está ativo.";

    public const string NetworkNotFound = "Rede não encontrada.";
    public const string RegionNotFound = "Região não encontrada.";
    public const string EstablishmentNotFound = "Estabelecimento não encontrado.";
    public const string ChannelNotFound = "Canal não encontrado.";
}

public record CreateNetworkRequest(
    [Required(ErrorMessage = OrganizationContract.NetworkNameRequired)]
    [StringLength(OrganizationContract.NameMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.NameMaxLengthMessage)]
    string Name,
    [Required(ErrorMessage = OrganizationContract.TimeZoneRequired)]
    [StringLength(OrganizationContract.TimeZoneMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.TimeZoneMaxLengthMessage)]
    string TimeZone);

public record UpdateNetworkRequest(
    [Required(ErrorMessage = OrganizationContract.NetworkNameRequired)]
    [StringLength(OrganizationContract.NameMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.NameMaxLengthMessage)]
    string Name,
    [Required(ErrorMessage = OrganizationContract.TimeZoneRequired)]
    [StringLength(OrganizationContract.TimeZoneMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.TimeZoneMaxLengthMessage)]
    string TimeZone,
    [Required(ErrorMessage = OrganizationContract.NetworkActiveRequired)]
    bool? IsActive);

public record NetworkResponse(Guid Id, string Name, string TimeZone, bool IsActive, DateTime CreatedAt);

public record CreateRegionRequest(
    [Required(ErrorMessage = OrganizationContract.NetworkRequired)]
    Guid? NetworkId,
    [Required(ErrorMessage = OrganizationContract.RegionNameRequired)]
    [StringLength(OrganizationContract.NameMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.NameMaxLengthMessage)]
    string Name) : INetworkScopedRequest
{
    Guid INetworkScopedRequest.NetworkId => NetworkId ?? Guid.Empty;
}

public record UpdateRegionRequest(
    [Required(ErrorMessage = OrganizationContract.RegionNameRequired)]
    [StringLength(OrganizationContract.NameMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.NameMaxLengthMessage)]
    string Name,
    [Required(ErrorMessage = OrganizationContract.RegionActiveRequired)]
    bool? IsActive);

public record RegionResponse(Guid Id, Guid NetworkId, string Name, bool IsActive, DateTime CreatedAt);

public record CreateEstablishmentRequest(
    [Required(ErrorMessage = OrganizationContract.RegionRequired)]
    Guid? RegionId,
    [Required(ErrorMessage = OrganizationContract.EstablishmentCodeRequired)]
    [StringLength(OrganizationContract.EstablishmentCodeMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.EstablishmentCodeMaxLengthMessage)]
    string Code,
    [Required(ErrorMessage = OrganizationContract.EstablishmentNameRequired)]
    [StringLength(OrganizationContract.NameMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.NameMaxLengthMessage)]
    string Name);

public record UpdateEstablishmentRequest(
    [Required(ErrorMessage = OrganizationContract.EstablishmentCodeRequired)]
    [StringLength(OrganizationContract.EstablishmentCodeMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.EstablishmentCodeMaxLengthMessage)]
    string Code,
    [Required(ErrorMessage = OrganizationContract.EstablishmentNameRequired)]
    [StringLength(OrganizationContract.NameMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.NameMaxLengthMessage)]
    string Name,
    [Required(ErrorMessage = OrganizationContract.EstablishmentActiveRequired)]
    bool? IsActive);

public record EstablishmentResponse(Guid Id, Guid RegionId, string Code, string Name, bool IsActive, DateTime CreatedAt);

public record CreateChannelRequest(
    [Required(ErrorMessage = OrganizationContract.EstablishmentRequired)]
    Guid? EstablishmentId,
    [Required(ErrorMessage = OrganizationContract.ChannelTypeRequired)]
    [StringLength(OrganizationContract.ChannelTypeMaxLength, MinimumLength = 1, ErrorMessage = OrganizationContract.ChannelTypeInvalid)]
    string Type);

public record UpdateChannelRequest(
    [Required(ErrorMessage = OrganizationContract.ChannelActiveRequired)]
    bool? IsActive);

public record ChannelResponse(Guid Id, Guid EstablishmentId, string Type, bool IsActive, DateTime CreatedAt);
