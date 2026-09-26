using System.Text.Json.Serialization;

namespace TestTask.Api.Models;

public sealed class ProcessResponse
{
    [JsonPropertyName("is_error")]
    public int IsError { get; init; }

    [JsonPropertyName("error_code")]
    public string ErrorCode { get; init; } = string.Empty;

    [JsonPropertyName("error_message")]
    public string ErrorMessage { get; init; } = string.Empty;

    [JsonPropertyName("elements_count")]
    public int ElementsCount { get; init; }

    [JsonPropertyName("emails_count")]
    public int EmailsCount { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("decrypted_plain_text")]
    public string DecryptedPlainText { get; init; } = string.Empty;

    [JsonPropertyName("elements_attr_list")]
    public List<string> ElementsAttrList { get; init; } = [];

    [JsonPropertyName("emails_list")]
    public List<string> EmailsList { get; init; } = [];

    public static ProcessResponse Failure(string errorCode, string errorMessage) => new()
    {
        IsError = 1,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}
