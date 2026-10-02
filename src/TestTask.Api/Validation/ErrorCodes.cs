namespace TestTask.Api.Validation;

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidRequest = "INVALID_REQUEST";

    public const string RequiredSelector = "REQUIRED_SELECTOR";
    public const string RequiredAttribute = "REQUIRED_ATTRIBUTE";
    public const string RequiredUrlBase64 = "REQUIRED_URL_BASE64";
    public const string RequiredPageBase64 = "REQUIRED_PAGE_BASE64";
    public const string RequiredKeyBase64 = "REQUIRED_KEY_BASE64";
    public const string RequiredEncryptedTextBase64 = "REQUIRED_ENCRYPTED_TEXT_BASE64";

    public const string InvalidUrlBase64 = "INVALID_URL_BASE64";
    public const string InvalidPageBase64 = "INVALID_PAGE_BASE64";
    public const string InvalidKeyBase64 = "INVALID_KEY_BASE64";
    public const string InvalidEncryptedTextBase64 = "INVALID_ENCRYPTED_TEXT_BASE64";
    public const string InvalidAesKeyLength = "INVALID_AES_KEY_LENGTH";
    public const string InvalidAesCiphertextLength = "INVALID_AES_CIPHERTEXT_LENGTH";

    public const string InvalidSelector = "INVALID_SELECTOR";
    public const string DatabaseError = "DATABASE_ERROR";
    public const string InternalError = "INTERNAL_ERROR";
    public const string InvalidDecryptedTextUtf8 = "INVALID_DECRYPTED_TEXT_UTF8";
    public const string DecryptionError = "DECRYPTION_ERROR";
    public const string ProcessingError = "PROCESSING_ERROR";
    public const string LimitExceeded = "LIMIT_EXCEEDED";
    public const string RequestTooLarge = "REQUEST_TOO_LARGE";
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
    public const string RequestTimeout = "REQUEST_TIMEOUT";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string HttpsRequired = "HTTPS_REQUIRED";
}
