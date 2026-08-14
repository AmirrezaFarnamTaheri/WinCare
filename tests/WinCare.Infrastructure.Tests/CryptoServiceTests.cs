using System;
using System.Security.Cryptography;
using WinCare.Infrastructure.Security;
using Xunit;

namespace WinCare.Infrastructure.Tests
{
    public sealed class CryptoServiceTests
    {
        private readonly CryptoService _cryptoService = new();

        [Fact]
        public void Encrypt_and_Decrypt_roundtrips_cleanly()
        {
            var secretData = "{\"settings\":{\"theme\":\"Dark\"},\"plugins\":[\"com.cleaner.disk\"]}";
            var passphrase = "SuperSecurePassword123!";

            var encrypted = _cryptoService.Encrypt(secretData, passphrase);

            Assert.NotNull(encrypted);
            Assert.NotEqual(secretData, encrypted);

            var decrypted = _cryptoService.Decrypt(encrypted, passphrase);
            Assert.Equal(secretData, decrypted);
        }

        [Fact]
        public void Decrypt_with_wrong_passphrase_throws_CryptographicException()
        {
            var secretData = "TopSecretConfiguration";
            var passphrase = "CorrectPassword123!";
            var wrongPassphrase = "WrongPassword456!";

            var encrypted = _cryptoService.Encrypt(secretData, passphrase);

            Assert.ThrowsAny<CryptographicException>(() => _cryptoService.Decrypt(encrypted, wrongPassphrase));
        }

        [Fact]
        public void Decrypt_tampered_ciphertext_throws_CryptographicException()
        {
            var secretData = "AuthenticatedPayload";
            var passphrase = "KeyPassphrase";

            var encryptedBase64 = _cryptoService.Encrypt(secretData, passphrase);
            var rawBytes = Convert.FromBase64String(encryptedBase64);

            // Tamper with the last ciphertext byte
            rawBytes[^1] ^= 0xFF;
            var tamperedBase64 = Convert.ToBase64String(rawBytes);

            Assert.ThrowsAny<CryptographicException>(() => _cryptoService.Decrypt(tamperedBase64, passphrase));
        }
    }
}
