using System.Security.Cryptography;
using System.Text;
using TestTask.Api.Data;
using TestTask.Api.Models;

namespace TestTask.Api.Tests;

internal sealed class FakeElementStore : IElementStore
{
    public List<ElementRecord> SavedElements { get; } = [];

    public Task SaveAsync(
        IReadOnlyList<ElementRecord> elements,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SavedElements.AddRange(elements);
        return Task.CompletedTask;
    }
}

internal static class TestData
{
    public static readonly byte[] Key = Enumerable.Range(0, 32)
        .Select(value => (byte)value)
        .ToArray();

    public static ProcessRequest CreateRequest(
        string page = "<html><body></body></html>",
        string selector = "a",
        string attribute = "href",
        byte[]? key = null,
        byte[]? ciphertext = null,
        string url = "https://example.test/page")
    {
        return new ProcessRequest
        {
            Selector = selector,
            Attribute = attribute,
            UrlB64 = Encode(Encoding.UTF8.GetBytes(url)),
            PageB64 = Encode(Encoding.UTF8.GetBytes(page)),
            KeyBytesB64 = Encode(key ?? Key),
            EncryptedTextBytesB64 = Encode(ciphertext ?? Encrypt(Encoding.ASCII.GetBytes("0123456789ABCDEF")))
        };
    }

    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes);

    public static byte[] Encrypt(byte[] plaintext, byte[]? key = null)
    {
        using var aes = Aes.Create();
        aes.Key = key ?? Key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }
}
