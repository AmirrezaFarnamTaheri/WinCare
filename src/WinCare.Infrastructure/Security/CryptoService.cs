using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WinCare.Infrastructure.Security
{
    public interface ICryptoService
    {
        string Encrypt(string plainText, string passphrase);
        string Decrypt(string cipherTextBase64, string passphrase);
    }

    public sealed class CryptoService : ICryptoService
    {
        private const int SaltSizeBytes = 16;
        private const int NonceSizeBytes = 12;
        private const int TagSizeBytes = 16;
        private const int KeySizeBytes = 32; // 256 bits
        private const int Pbkdf2Iterations = 100_000;

        public string Encrypt(string plainText, string passphrase)
        {
            if (plainText is null) throw new ArgumentNullException(nameof(plainText));
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var tag = new byte[TagSizeBytes];
            var cipherBytes = new byte[plainBytes.Length];

            using var kdf = new Rfc2898DeriveBytes(passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            var key = kdf.GetBytes(KeySizeBytes);

            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

            // Envelope format: [Salt(16) | Nonce(12) | Tag(16) | Ciphertext(N)]
            var envelope = new byte[SaltSizeBytes + NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
            Buffer.BlockCopy(salt, 0, envelope, 0, SaltSizeBytes);
            Buffer.BlockCopy(nonce, 0, envelope, SaltSizeBytes, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, envelope, SaltSizeBytes + NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(cipherBytes, 0, envelope, SaltSizeBytes + NonceSizeBytes + TagSizeBytes, cipherBytes.Length);

            return Convert.ToBase64String(envelope);
        }

        public string Decrypt(string cipherTextBase64, string passphrase)
        {
            if (string.IsNullOrWhiteSpace(cipherTextBase64)) throw new ArgumentException("Ciphertext cannot be empty", nameof(cipherTextBase64));
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

            var envelope = Convert.FromBase64String(cipherTextBase64);
            var minLength = SaltSizeBytes + NonceSizeBytes + TagSizeBytes;
            if (envelope.Length < minLength)
            {
                throw new CryptographicException("Ciphertext envelope is corrupted or too short.");
            }

            var salt = new byte[SaltSizeBytes];
            var nonce = new byte[NonceSizeBytes];
            var tag = new byte[TagSizeBytes];
            var cipherLength = envelope.Length - minLength;
            var cipherBytes = new byte[cipherLength];
            var plainBytes = new byte[cipherLength];

            Buffer.BlockCopy(envelope, 0, salt, 0, SaltSizeBytes);
            Buffer.BlockCopy(envelope, SaltSizeBytes, nonce, 0, NonceSizeBytes);
            Buffer.BlockCopy(envelope, SaltSizeBytes + NonceSizeBytes, tag, 0, TagSizeBytes);
            Buffer.BlockCopy(envelope, minLength, cipherBytes, 0, cipherLength);

            using var kdf = new Rfc2898DeriveBytes(passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            var key = kdf.GetBytes(KeySizeBytes);

            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
