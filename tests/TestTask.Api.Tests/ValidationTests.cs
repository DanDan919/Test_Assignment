using TestTask.Api.Models;
using TestTask.Api.Validation;

namespace TestTask.Api.Tests;

public sealed class ValidationTests
{
    private readonly ProcessRequestValidator validator = new();

    [Theory]
    [InlineData(nameof(ProcessRequest.Selector), ErrorCodes.RequiredSelector)]
    [InlineData(nameof(ProcessRequest.Attribute), ErrorCodes.RequiredAttribute)]
    [InlineData(nameof(ProcessRequest.UrlB64), ErrorCodes.RequiredUrlBase64)]
    [InlineData(nameof(ProcessRequest.PageB64), ErrorCodes.RequiredPageBase64)]
    [InlineData(nameof(ProcessRequest.KeyBytesB64), ErrorCodes.RequiredKeyBase64)]
    [InlineData(nameof(ProcessRequest.EncryptedTextBytesB64), ErrorCodes.RequiredEncryptedTextBase64)]
    public void Missing_required_field_returns_expected_code(
        string propertyName,
        string expectedCode)
    {
        var request = TestData.CreateRequest();
        SetProperty(request, propertyName, null);

        var result = validator.Validate(request);

        Assert.Contains(result.Errors, error =>
            error.PropertyName == propertyName && error.ErrorCode == expectedCode);
    }

    [Theory]
    [InlineData(nameof(ProcessRequest.Selector), ErrorCodes.RequiredSelector)]
    [InlineData(nameof(ProcessRequest.Attribute), ErrorCodes.RequiredAttribute)]
    public void Empty_or_whitespace_selector_and_attribute_are_rejected(
        string propertyName,
        string expectedCode)
    {
        var request = TestData.CreateRequest();
        SetProperty(request, propertyName, "   ");

        var result = validator.Validate(request);

        Assert.Contains(result.Errors, error =>
            error.PropertyName == propertyName && error.ErrorCode == expectedCode);
    }

    [Theory]
    [InlineData(nameof(ProcessRequest.UrlB64), "%%%", ErrorCodes.InvalidUrlBase64)]
    [InlineData(nameof(ProcessRequest.PageB64), "%%%", ErrorCodes.InvalidPageBase64)]
    [InlineData(nameof(ProcessRequest.KeyBytesB64), "%%%", ErrorCodes.InvalidKeyBase64)]
    [InlineData(nameof(ProcessRequest.EncryptedTextBytesB64), "%%%", ErrorCodes.InvalidEncryptedTextBase64)]
    public void Invalid_base64_returns_granular_code(
        string propertyName,
        string value,
        string expectedCode)
    {
        var request = TestData.CreateRequest();
        SetProperty(request, propertyName, value);

        var result = validator.Validate(request);

        Assert.Contains(result.Errors, error =>
            error.PropertyName == propertyName && error.ErrorCode == expectedCode);
    }

    [Fact]
    public void Invalid_key_length_returns_expected_code()
    {
        var request = TestData.CreateRequest(key: new byte[31]);

        var result = validator.Validate(request);

        Assert.Contains(result.Errors, error =>
            error.PropertyName == nameof(ProcessRequest.KeyBytesB64) &&
            error.ErrorCode == ErrorCodes.InvalidAesKeyLength);
    }

    [Fact]
    public void Invalid_ciphertext_length_returns_expected_code()
    {
        var request = TestData.CreateRequest(ciphertext: new byte[15]);

        var result = validator.Validate(request);

        Assert.Contains(result.Errors, error =>
            error.PropertyName == nameof(ProcessRequest.EncryptedTextBytesB64) &&
            error.ErrorCode == ErrorCodes.InvalidAesCiphertextLength);
    }

    [Fact]
    public async Task Valid_request_passes_validation()
    {
        var result = await validator.ValidateAsync(TestData.CreateRequest());

        Assert.True(result.IsValid);
    }

    private static void SetProperty(
        ProcessRequest request,
        string propertyName,
        string? value)
    {
        switch (propertyName)
        {
            case nameof(ProcessRequest.Selector):
                request.Selector = value;
                break;
            case nameof(ProcessRequest.Attribute):
                request.Attribute = value;
                break;
            case nameof(ProcessRequest.UrlB64):
                request.UrlB64 = value;
                break;
            case nameof(ProcessRequest.PageB64):
                request.PageB64 = value;
                break;
            case nameof(ProcessRequest.KeyBytesB64):
                request.KeyBytesB64 = value;
                break;
            case nameof(ProcessRequest.EncryptedTextBytesB64):
                request.EncryptedTextBytesB64 = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(propertyName));
        }
    }
}
