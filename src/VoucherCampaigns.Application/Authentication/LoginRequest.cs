using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Authentication;

// Atributos no parâmetro: o MVC valida records posicionais pelo construtor.
public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);
