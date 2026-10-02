using TestTask.Api.Models;

namespace TestTask.Api.Validation;

// Bounds apply before decoding and before persisting repeated OuterHtml values.
public static class ProcessingLimits
{
    public const int RequestBodyBytes = 1024 * 1024;
    public const int SelectorCharacters = 256;
    public const int AttributeCharacters = 128;
    public const int UrlBase64Characters = 8192;
    public const int PageBase64Characters = 699052; // 512 KiB decoded, without whitespace
    public const int KeyBase64Characters = 128;
    public const int CiphertextBase64Characters = 87384; // 64 KiB decoded
    public const int Elements = 1000;
    public const int DomNodes = 10000;
    public const int DomDepth = 128;
    public const int Emails = 1000;
    public const int StoredHtmlBytes = 4 * 1024 * 1024;
    public const long DatabaseBytes = 256L * 1024 * 1024;

    public static bool IsWithinInputLimits(ProcessRequest request) =>
        (request.Selector?.Length ?? 0) <= SelectorCharacters &&
        (request.Attribute?.Length ?? 0) <= AttributeCharacters &&
        (request.UrlB64?.Length ?? 0) <= UrlBase64Characters &&
        (request.PageB64?.Length ?? 0) <= PageBase64Characters &&
        (request.KeyBytesB64?.Length ?? 0) <= KeyBase64Characters &&
        (request.EncryptedTextBytesB64?.Length ?? 0) <= CiphertextBase64Characters;
}

public sealed class ProcessingLimitException : Exception;
