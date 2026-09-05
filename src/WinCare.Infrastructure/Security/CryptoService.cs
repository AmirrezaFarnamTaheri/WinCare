using System;
using System.Buffers.Binary;
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
        private const int CurrentPbkdf2Iterations = 600_000;
        private const int LegacyPbkdf2Iterations = 100_000;
        private const byte EnvelopeVersion = 1;
        private static ReadOnlySpan<byte> Magic => "WCE"u8;
        private const int HeaderSizeBytes = 3 + 1 + sizeof(int);

        public string Encrypt(string plainText, string passphrase)
        {
            if (plainText is null) throw new ArgumentNullException(nameof(plainText));
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            byte[] tag = new byte[TagSizeBytes];
            byte[] cipherBytes = new byte[plainBytes.Length];
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, CurrentPbkdf2Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

            try
            {
                using var aesGcm = new AesGcm(key, TagSizeBytes);
                aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

                // Versioned envelope:
                // [Magic "WCE" | Version(1) | PBKDF2 iterations LE(4) | Salt(16) | Nonce(12) | Tag(16) | Ciphertext(N)]
                // Keeping the work factor in the authenticated-encryption envelope lets future
                // releases raise KDF cost without silently breaking existing synced profiles.
                var envelope = new byte[HeaderSizeBytes + SaltSizeBytes + NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
                Magic.CopyTo(envelope);
                envelope[3] = EnvelopeVersion;
                BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(4, sizeof(int)), CurrentPbkdf2Iterations);
                int offset = HeaderSizeBytes;
                Buffer.BlockCopy(salt, 0, envelope, offset, SaltSizeBytes);
                offset += SaltSizeBytes;
                Buffer.BlockCopy(nonce, 0, envelope, offset, NonceSizeBytes);
                offset += NonceSizeBytes;
                Buffer.BlockCopy(tag, 0, envelope, offset, TagSizeBytes);
                offset += TagSizeBytes;
                Buffer.BlockCopy(cipherBytes, 0, envelope, offset, cipherBytes.Length);

                return Convert.ToBase64String(envelope);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }

        public string Decrypt(string cipherTextBase64, string passphrase)
        {
            if (string.IsNullOrWhiteSpace(cipherTextBase64)) throw new ArgumentException("Ciphertext cannot be empty", nameof(cipherTextBase64));
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));

            byte[] envelope = Convert.FromBase64String(cipherTextBase64);
            return HasCurrentEnvelopeHeader(envelope)
                ? DecryptVersioned(envelope, passphrase)
                : DecryptLegacy(envelope, passphrase);
        }

        private static string DecryptVersioned(byte[] envelope, string passphrase)
        {
            if (envelope.Length < HeaderSizeBytes + SaltSizeBytes + NonceSizeBytes + TagSizeBytes)
            {
                throw new CryptographicException("Ciphertext envelope is corrupted or too short.");
            }

            byte version = envelope[3];
            if (version != EnvelopeVersion)
            {
                throw new CryptographicException($"Unsupported encrypted profile envelope version '{version}'.");
            }

            int iterations = BinaryPrimitives.ReadInt32LittleEndian(envelope.AsSpan(4, sizeof(int)));
            if (iterations < 100_000 || iterations > 5_000_000)
            {
                throw new CryptographicException("Encrypted profile KDF work factor is outside the accepted safety range.");
            }

            return DecryptPayload(envelope.AsSpan(HeaderSizeBytes), passphrase, iterations);
        }

        private static string DecryptLegacy(byte[] envelope, string passphrase)
        {
            // Compatibility for envelopes produced before the versioned format was introduced:
            // [Salt(16) | Nonce(12) | Tag(16) | Ciphertext(N)]. New writes never use this path.
            return DecryptPayload(envelope, passphrase, LegacyPbkdf2Iterations);
        }

        private static string DecryptPayload(ReadOnlySpan<byte> payload, string passphrase, int iterations)
        {
            int minLength = SaltSizeBytes + NonceSizeBytes + TagSizeBytes;
            if (payload.Length < minLength)
            {
                throw new CryptographicException("Ciphertext envelope is corrupted or too short.");
            }

            ReadOnlySpan<byte> salt = payload[..SaltSizeBytes];
            ReadOnlySpan<byte> nonce = payload.Slice(SaltSizeBytes, NonceSizeBytes);
            ReadOnlySpan<byte> tag = payload.Slice(SaltSizeBytes + NonceSizeBytes, TagSizeBytes);
            ReadOnlySpan<byte> cipherBytes = payload[minLength..];
            byte[] plainBytes = new byte[cipherBytes.Length];
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, KeySizeBytes);

            try
            {
                using var aesGcm = new AesGcm(key, TagSizeBytes);
                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
                return Encoding.UTF8.GetString(plainBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }

        private static bool HasCurrentEnvelopeHeader(ReadOnlySpan<byte> envelope) =>
            envelope.Length >= HeaderSizeBytes && envelope[..3].SequenceEqual(Magic);
    }
}
