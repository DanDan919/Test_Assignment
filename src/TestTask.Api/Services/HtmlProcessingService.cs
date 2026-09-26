using AngleSharp;
using AngleSharp.Dom;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TestTask.Api.Data;
using TestTask.Api.Models;
using TestTask.Api.Validation;

namespace TestTask.Api.Services;

public sealed partial class HtmlProcessingService(
    IElementStore elementStore) : IHtmlProcessingService
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<ProcessResponse> ProcessAsync(
        ProcessRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Selector))
        {
            return ProcessResponse.Failure(
                ErrorCodes.RequiredSelector,
                "The selector field is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Attribute))
        {
            return ProcessResponse.Failure(
                ErrorCodes.RequiredAttribute,
                "The attribute field is required.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryDecodeUtf8Base64(
                request.UrlB64,
                ErrorCodes.InvalidUrlBase64,
                "The url_b64 value is not valid Base64 UTF-8.",
                out var url,
                out var urlError))
        {
            return urlError!;
        }

        if (!TryDecodeUtf8Base64(
                request.PageB64,
                ErrorCodes.InvalidPageBase64,
                "The page_b64 value is not valid Base64 UTF-8.",
                out var page,
                out var pageError))
        {
            return pageError!;
        }

        cancellationToken.ThrowIfCancellationRequested();

        IDocument document;
        try
        {
            // The input is already in memory, but AngleSharp's OpenAsync API is
            // the appropriate library API for creating a document asynchronously.
            var context = BrowsingContext.New(Configuration.Default);
            document = await context.OpenAsync(
                response => response.Content(page),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ProcessResponse.Failure(
                ErrorCodes.ProcessingError,
                $"HTML parsing failed: {exception.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        ElementExtractionResult elementResult;
        try
        {
            elementResult = ExtractElements(document, request.Selector, request.Attribute);
        }
        catch (DomException exception)
        {
            return ProcessResponse.Failure(
                ErrorCodes.InvalidSelector,
                $"The CSS selector is invalid: {exception.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var emails = EmailRegex()
            .Matches(page)
            .Select(match => match.Value)
            .ToList();

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryDecodeBase64(
                request.KeyBytesB64,
                ErrorCodes.InvalidKeyBase64,
                "The key_bytes_b64 value is not valid Base64.",
                out var key,
                out var keyError))
        {
            return keyError!;
        }

        if (key.Length != 32)
        {
            return ProcessResponse.Failure(
                ErrorCodes.InvalidAesKeyLength,
                "The decoded AES key must contain exactly 32 bytes.");
        }

        if (!TryDecodeBase64(
                request.EncryptedTextBytesB64,
                ErrorCodes.InvalidEncryptedTextBase64,
                "The encrypted_text_bytes_b64 value is not valid Base64.",
                out var ciphertext,
                out var ciphertextError))
        {
            return ciphertextError!;
        }

        if (ciphertext.Length % 16 != 0)
        {
            return ProcessResponse.Failure(
                ErrorCodes.InvalidAesCiphertextLength,
                "The decoded ciphertext length must be a multiple of 16 bytes.");
        }

        string decryptedPlainText;
        try
        {
            decryptedPlainText = DecryptUtf8(ciphertext, key);
        }
        catch (DecoderFallbackException exception)
        {
            return ProcessResponse.Failure(
                ErrorCodes.InvalidDecryptedTextUtf8,
                $"The decrypted plaintext is not valid UTF-8: {exception.Message}");
        }
        catch (CryptographicException exception)
        {
            return ProcessResponse.Failure(
                ErrorCodes.DecryptionError,
                $"AES decryption failed: {exception.Message}");
        }

        await elementStore.SaveAsync(elementResult.Items, cancellationToken);

        return new ProcessResponse
        {
            IsError = 0,
            Url = url,
            ElementsCount = elementResult.Items.Count,
            EmailsCount = emails.Count,
            DecryptedPlainText = decryptedPlainText,
            ElementsAttrList = elementResult.Items
                .Select(item => item.AttributeValue)
                .ToList(),
            EmailsList = emails
        };
    }

    private static ElementExtractionResult ExtractElements(
        IDocument document,
        string selector,
        string attribute)
    {
        var selectedElements = document.QuerySelectorAll(selector);
        var items = new List<ElementRecord>(selectedElements.Length);

        foreach (var element in selectedElements)
        {
            // A missing attribute is represented as an empty string. The element
            // still counts and its OuterHtml remains available for DB persistence.
            var attributeValue = element.GetAttribute(attribute) ?? string.Empty;
            items.Add(new ElementRecord(attributeValue, element.OuterHtml));
        }

        return new ElementExtractionResult(items);
    }

    private static string DecryptUtf8(byte[] ciphertext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(
            ciphertext,
            inputOffset: 0,
            inputCount: ciphertext.Length);

        // PaddingMode.None means no bytes are removed here. In particular,
        // trailing '\0' bytes remain part of the returned plaintext.
        return StrictUtf8.GetString(plaintextBytes);
    }

    private static bool TryDecodeUtf8Base64(
        string? value,
        string errorCode,
        string errorMessage,
        out string decoded,
        out ProcessResponse? error)
    {
        if (!TryDecodeBase64(value, errorCode, errorMessage, out var bytes, out error))
        {
            decoded = string.Empty;
            return false;
        }

        try
        {
            decoded = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException exception)
        {
            decoded = string.Empty;
            error = ProcessResponse.Failure(errorCode, $"{errorMessage} {exception.Message}");
            return false;
        }
    }

    private static bool TryDecodeBase64(
        string? value,
        string errorCode,
        string errorMessage,
        out byte[] decoded,
        out ProcessResponse? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            decoded = [];
            error = ProcessResponse.Failure(errorCode, errorMessage);
            return false;
        }

        try
        {
            decoded = Convert.FromBase64String(value ?? string.Empty);
            error = null;
            return true;
        }
        catch (FormatException exception)
        {
            decoded = [];
            error = ProcessResponse.Failure(errorCode, $"{errorMessage} {exception.Message}");
            return false;
        }
    }

    [GeneratedRegex(
        @"(?<![\w.+-])[\w.!#$%&'*+/=?^_`{|}~-]+@[\w-]+(?:\.[\w-]+)+(?![\w-])",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    private sealed record ElementExtractionResult(
        IReadOnlyList<ElementRecord> Items);
}
