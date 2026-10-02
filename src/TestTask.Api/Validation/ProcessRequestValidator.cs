using FluentValidation;
using TestTask.Api.Models;

namespace TestTask.Api.Validation;

public sealed class ProcessRequestValidator : AbstractValidator<ProcessRequest>
{
    public ProcessRequestValidator()
    {
        RuleFor(request => request.Selector)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.RequiredSelector)
            .WithMessage("The selector field is required.")
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithErrorCode(ErrorCodes.RequiredSelector)
            .WithMessage("The selector field must not be blank.")
            .MaximumLength(ProcessingLimits.SelectorCharacters)
            .WithErrorCode(ErrorCodes.LimitExceeded);

        RuleFor(request => request.Attribute)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.RequiredAttribute)
            .WithMessage("The attribute field is required.")
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithErrorCode(ErrorCodes.RequiredAttribute)
            .WithMessage("The attribute field must not be blank.")
            .MaximumLength(ProcessingLimits.AttributeCharacters)
            .WithErrorCode(ErrorCodes.LimitExceeded);

        RuleFor(request => request.UrlB64)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.RequiredUrlBase64)
            .WithMessage("The url_b64 field is required.")
            .MaximumLength(ProcessingLimits.UrlBase64Characters)
            .WithErrorCode(ErrorCodes.LimitExceeded)
            .Must(IsValidBase64)
            .WithErrorCode(ErrorCodes.InvalidUrlBase64)
            .WithMessage("The url_b64 field is not valid Base64.");

        RuleFor(request => request.PageB64)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.RequiredPageBase64)
            .WithMessage("The page_b64 field is required.")
            .MaximumLength(ProcessingLimits.PageBase64Characters)
            .WithErrorCode(ErrorCodes.LimitExceeded)
            .Must(IsValidBase64)
            .WithErrorCode(ErrorCodes.InvalidPageBase64)
            .WithMessage("The page_b64 field is not valid Base64.");

        RuleFor(request => request.KeyBytesB64)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.RequiredKeyBase64)
            .WithMessage("The key_bytes_b64 field is required.")
            .MaximumLength(ProcessingLimits.KeyBase64Characters)
            .WithErrorCode(ErrorCodes.LimitExceeded)
            .Must(IsValidBase64)
            .WithErrorCode(ErrorCodes.InvalidKeyBase64)
            .WithMessage("The key_bytes_b64 field is not valid Base64.")
            .Must(HasAes256KeyLength)
            .WithErrorCode(ErrorCodes.InvalidAesKeyLength)
            .WithMessage("The decoded AES key must contain exactly 32 bytes.");

        RuleFor(request => request.EncryptedTextBytesB64)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.RequiredEncryptedTextBase64)
            .WithMessage("The encrypted_text_bytes_b64 field is required.")
            .MaximumLength(ProcessingLimits.CiphertextBase64Characters)
            .WithErrorCode(ErrorCodes.LimitExceeded)
            .Must(IsValidBase64)
            .WithErrorCode(ErrorCodes.InvalidEncryptedTextBase64)
            .WithMessage("The encrypted_text_bytes_b64 field is not valid Base64.")
            .Must(HasAesBlockAlignedLength)
            .WithErrorCode(ErrorCodes.InvalidAesCiphertextLength)
            .WithMessage("The decoded ciphertext length must be a multiple of 16 bytes.");
    }

    private static bool IsValidBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            _ = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool HasAes256KeyLength(string? value)
    {
        return TryDecode(value, out var bytes) && bytes.Length == 32;
    }

    private static bool HasAesBlockAlignedLength(string? value)
    {
        return TryDecode(value, out var bytes) && bytes.Length % 16 == 0;
    }

    private static bool TryDecode(string? value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value ?? string.Empty);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
