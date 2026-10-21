using System.ComponentModel.DataAnnotations;

namespace SistemaManutencao.API.Helpers;

/// <summary>Valida o mínimo do texto que será persistido após Trim().</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MinLengthTrimmedAttribute(int length) : ValidationAttribute
{
    public override bool IsValid(object? value)
        => value is null || value is string text && text.Trim().Length >= length;

    public override string FormatErrorMessage(string name)
        => $"O campo {name} deve conter pelo menos {length} caracteres após remover espaços nas extremidades.";
}
