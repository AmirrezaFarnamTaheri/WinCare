using System;
using System.Security.Cryptography;
using System.Text;
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
            byte[] envelope = Convert.FromBase64String(encrypted);
            Assert.Equal("WCE", Encoding.ASCII.GetString(envelope, 0, 3));
            Assert.Equal(1, envelope[3]);

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

            rawBytes[^1] ^= 0xFF;
            var tamperedBase64 = Convert.ToBase64String(rawBytes);

            Assert.ThrowsAny<CryptographicException>(() => _cryptoService.Decrypt(tamperedBase64, passphrase));
        }

        [Fact]
        public void Decrypt_remains_compatible_with_legacy_unversioned_envelope()
        {
            const string plaintext = "legacy-profile-data";
            const string passphrase = "LegacyPassphrase!";
            string legacy = CreateLegacyEnvelope(plaintext, passphrase);

            Assert.Equal(plaintext, _cryptoService.Decrypt(legacy, passphrase));
        }

        [Fact]
        public void Decrypt_rejects_unreasonable_versioned_work_factor()
        {
            byte[] envelope = Convert.FromBase64String(_cryptoService.Encrypt("payload", "passphrase"));
            envelope[4] = 1;
            envelope[5] = 0;
            envelope[6] = 0;
            envelope[7] = 0;

            Assert.Throws<CryptographicException>(() =>
                _cryptoService.Decrypt(Convert.ToBase64String(envelope), "passphrase"));
        }

        private static string CreateLegacyEnvelope(string plaintext, string passphrase)
        {
            const int saltSize = 16;
            const int nonceSize = 12;
            const int tagSize = 16;
            byte[] plain = Encoding.UTF8.GetBytes(plaintext);
            byte[] salt = RandomNumberGenerator.GetBytes(saltSize);
            byte[] nonce = RandomNumberGenerator.GetBytes(nonceSize);
            byte[] tag = new byte[tagSize];
            byte[] cipher = new byte[plain.Length];
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, 100_000, HashAlgorithmName.SHA256, 32);
            try
            {
                using var aes = new AesGcm(key, tagSize);
                aes.Encrypt(nonce, plain, cipher, tag);
                byte[] envelope = new byte[saltSize + nonceSize + tagSize + cipher.Length];
                Buffer.BlockCopy(salt, 0, envelope, 0, saltSize);
                Buffer.BlockCopy(nonce, 0, envelope, saltSize, nonceSize);
                Buffer.BlockCopy(tag, 0, envelope, saltSize + nonceSize, tagSize);
                Buffer.BlockCopy(cipher, 0, envelope, saltSize + nonceSize + tagSize, cipher.Length);
                return Convert.ToBase64String(envelope);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }
}
