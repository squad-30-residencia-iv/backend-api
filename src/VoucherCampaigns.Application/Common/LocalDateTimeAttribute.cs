using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Common;

/// <summary>
/// Exige data e hora sem fuso (ex.: "2026-10-01T08:00:00"), interpretadas depois no fuso do escopo.
/// Com "Z" ou offset, o System.Text.Json produz Kind Utc ou Local, que são recusados.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class LocalDateTimeAttribute : ValidationAttribute
{
    public LocalDateTimeAttribute()
        : base("Informe data e hora sem fuso; elas são interpretadas no fuso da rede.")
    {
    }

    public override bool IsValid(object? value) => value is not DateTime date || date.Kind == DateTimeKind.Unspecified;
}
