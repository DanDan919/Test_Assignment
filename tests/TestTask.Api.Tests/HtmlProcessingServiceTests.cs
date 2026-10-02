using System.Text;
using TestTask.Api.Services;
using TestTask.Api.Validation;

namespace TestTask.Api.Tests;

public sealed class HtmlProcessingServiceTests
{
    [Theory]
    [InlineData("UrlB64", ErrorCodes.InvalidUrlBase64)]
    [InlineData("PageB64", ErrorCodes.InvalidPageBase64)]
    [InlineData("KeyBytesB64", ErrorCodes.InvalidKeyBase64)]
    [InlineData("EncryptedTextBytesB64", ErrorCodes.InvalidEncryptedTextBase64)]
    public async Task Invalid_base64_returns_expected_code(
        string field,
        string expectedCode)
    {
        var request = TestData.CreateRequest();
        SetStringProperty(request, field, "not-base64");

        var response = await CreateService().ProcessAsync(request, CancellationToken.None);

        AssertFailure(response, expectedCode);
    }

    [Fact]
    public async Task Invalid_utf8_in_url_returns_url_base64_code()
    {
        var request = TestData.CreateRequest();
        request.UrlB64 = TestData.Encode([0xff]);

        var response = await CreateService().ProcessAsync(request, CancellationToken.None);

        AssertFailure(response, ErrorCodes.InvalidUrlBase64);
    }

    [Fact]
    public async Task Invalid_utf8_in_page_returns_page_base64_code()
    {
        var request = TestData.CreateRequest();
        request.PageB64 = TestData.Encode([0xff]);

        var response = await CreateService().ProcessAsync(request, CancellationToken.None);

        AssertFailure(response, ErrorCodes.InvalidPageBase64);
    }

    [Fact]
    public async Task Selector_order_attributes_outer_html_and_missing_attribute_are_preserved()
    {
        const string page = "<div><a data-id='one'>First</a><a href='two'>Second</a></div>";
        var store = new FakeElementStore();

        var response = await CreateService(store).ProcessAsync(
            TestData.CreateRequest(page, "a", "href"),
            CancellationToken.None);

        Assert.Equal(0, response.IsError);
        Assert.Equal(2, response.ElementsCount);
        Assert.Equal(["", "two"], response.ElementsAttrList);
        Assert.Equal(2, store.SavedElements.Count);
        Assert.Contains("First", store.SavedElements[0].Html);
        Assert.Contains("Second", store.SavedElements[1].Html);
    }

    [Fact]
    public async Task Invalid_css_selector_returns_invalid_selector()
    {
        var response = await CreateService().ProcessAsync(
            TestData.CreateRequest(selector: "["),
            CancellationToken.None);

        AssertFailure(response, ErrorCodes.InvalidSelector);
    }

    [Fact]
    public async Task Emails_are_extracted_with_duplicates_in_source_order()
    {
        const string page = "<p>one@example.com two@example.com one@example.com</p>";

        var response = await CreateService().ProcessAsync(
            TestData.CreateRequest(page, "p", "class"),
            CancellationToken.None);

        Assert.Equal(3, response.EmailsCount);
        Assert.Equal(
            ["one@example.com", "two@example.com", "one@example.com"],
            response.EmailsList);
    }

    [Fact]
    public async Task No_emails_returns_empty_list()
    {
        var response = await CreateService().ProcessAsync(
            TestData.CreateRequest("<p>no address here</p>", "p", "class"),
            CancellationToken.None);

        Assert.Equal(0, response.EmailsCount);
        Assert.Empty(response.EmailsList);
    }

    [Fact]
    public async Task Aes_ecb_no_padding_roundtrips_fixed_block()
    {
        var plaintext = Encoding.ASCII.GetBytes("0123456789ABCDEF");
        var response = await CreateService().ProcessAsync(
            TestData.CreateRequest(ciphertext: TestData.Encrypt(plaintext)),
            CancellationToken.None);

        Assert.Equal(0, response.IsError);
        Assert.Equal("0123456789ABCDEF", response.DecryptedPlainText);
    }

    [Fact]
    public async Task Exception_looking_plaintext_is_still_successful_decryption()
    {
        const string plaintext = "AES Error: Object reference not set to an instance of an object.";

        var response = await CreateService().ProcessAsync(
            TestData.CreateRequest(
                ciphertext: TestData.Encrypt(Encoding.ASCII.GetBytes(plaintext))),
            CancellationToken.None);

        Assert.Equal(0, response.IsError);
        Assert.Equal(string.Empty, response.ErrorCode);
        Assert.Equal(string.Empty, response.ErrorMessage);
        Assert.Equal(plaintext, response.DecryptedPlainText);
    }

    [Fact]
    public async Task Trailing_null_bytes_are_not_removed()
    {
        var plaintext = new byte[16];
        Encoding.ASCII.GetBytes("hello").CopyTo(plaintext, 0);

        var response = await CreateService().ProcessAsync(
            TestData.CreateRequest(ciphertext: TestData.Encrypt(plaintext)),
            CancellationToken.None);

        Assert.Equal(16, response.DecryptedPlainText.Length);
        Assert.StartsWith("hello", response.DecryptedPlainText);
        Assert.EndsWith("\0", response.DecryptedPlainText);
    }

    [Fact]
    public async Task Wrong_key_length_returns_expected_code()
    {
        var response = await CreateService().ProcessAsync(
            TestData.CreateRequest(key: new byte[31]),
            CancellationToken.None);

        AssertFailure(response, ErrorCodes.InvalidAesKeyLength);
    }

    [Fact]
    public async Task Non_block_aligned_ciphertext_returns_expected_code()
    {
        var response = await CreateService().ProcessAsync(
            TestData.CreateRequest(ciphertext: new byte[15]),
            CancellationToken.None);

        AssertFailure(response, ErrorCodes.InvalidAesCiphertextLength);
    }

    [Fact]
    public async Task Invalid_decrypted_utf8_returns_expected_code()
    {
        var invalidUtf8Plaintext = new byte[16];
        invalidUtf8Plaintext[0] = 0xff;

        var response = await CreateService().ProcessAsync(
            TestData.CreateRequest(ciphertext: TestData.Encrypt(invalidUtf8Plaintext)),
            CancellationToken.None);

        AssertFailure(response, ErrorCodes.InvalidDecryptedTextUtf8);
    }

    [Fact]
    public async Task Success_and_failure_responses_have_expected_contract()
    {
        var success = await CreateService().ProcessAsync(
            TestData.CreateRequest(),
            CancellationToken.None);
        var failureRequest = TestData.CreateRequest();
        failureRequest.UrlB64 = "bad";
        var failure = await CreateService().ProcessAsync(
            failureRequest,
            CancellationToken.None);

        Assert.Equal(0, success.IsError);
        Assert.Equal(string.Empty, success.ErrorCode);
        Assert.Equal(string.Empty, success.ErrorMessage);
        Assert.Equal(1, failure.IsError);
        Assert.Equal(ErrorCodes.InvalidUrlBase64, failure.ErrorCode);
        Assert.DoesNotContain("StackTrace", failure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", failure.ErrorMessage, StringComparison.Ordinal);
    }

    private static HtmlProcessingService CreateService(FakeElementStore? store = null) =>
        new(store ?? new FakeElementStore());

    private static void SetStringProperty(
        TestTask.Api.Models.ProcessRequest request,
        string propertyName,
        string value)
    {
        switch (propertyName)
        {
            case "UrlB64":
                request.UrlB64 = value;
                break;
            case "PageB64":
                request.PageB64 = value;
                break;
            case "KeyBytesB64":
                request.KeyBytesB64 = value;
                break;
            case "EncryptedTextBytesB64":
                request.EncryptedTextBytesB64 = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(propertyName));
        }
    }

    private static void AssertFailure(
        TestTask.Api.Models.ProcessResponse response,
        string expectedCode)
    {
        Assert.Equal(1, response.IsError);
        Assert.Equal(expectedCode, response.ErrorCode);
        Assert.NotEmpty(response.ErrorMessage);
    }
}
