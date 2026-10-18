using Microsoft.Extensions.Configuration;
using System;
using System.Security.Cryptography;
using System.Text;

namespace BLL.Notifications
{
    // הצפנת גוף ההודעה בזמן שהוא ממתין בתור השליחה (AES-256-GCM).
    // כך גם מי שרואה את טבלת Notifications לא רואה סיסמאות זמניות או קודי אימות.
    public interface IPayloadProtector
    {
        string Protect(string plaintext);
        string Unprotect(string protectedPayload);
    }

    public sealed class AesGcmPayloadProtector : IPayloadProtector
    {
        private const string Version = "v1:";
        private const int NonceSize = 12;
        private const int TagSize = 16;

        private readonly byte[] _key;

        // המפתח: Notifications:EncryptionKey (Base64 של 32 בתים). אם לא הוגדר - נגזר (HKDF) מ-Jwt:Key,
        // כך שאין צורך בהגדרה נוספת וכל השרתים מקבלים אותו מפתח.
        public AesGcmPayloadProtector(IConfiguration configuration)
        {
            var configured = configuration["Notifications:EncryptionKey"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                _key = Convert.FromBase64String(configured);
                if (_key.Length != 32)
                    throw new InvalidOperationException("Notifications:EncryptionKey must be a Base64 encoded 32-byte key.");
                return;
            }

            var secret = configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("Notifications:EncryptionKey or Jwt:Key must be configured.");

            _key = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                Encoding.UTF8.GetBytes(secret),
                outputLength: 32,
                salt: Encoding.UTF8.GetBytes("FoodRescue.Notifications"),
                info: Encoding.UTF8.GetBytes("payload-encryption-v1"));
        }

        public AesGcmPayloadProtector(byte[] key)
        {
            if (key == null || key.Length != 32) throw new ArgumentException("Key must be 32 bytes.", nameof(key));
            _key = key;
        }

        public string Protect(string plaintext)
        {
            var plain = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(_key, TagSize))
                aes.Encrypt(nonce, plain, cipher, tag);

            var output = new byte[NonceSize + TagSize + cipher.Length];
            Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
            Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);

            return Version + Convert.ToBase64String(output);
        }

        public string Unprotect(string protectedPayload)
        {
            if (string.IsNullOrEmpty(protectedPayload) || !protectedPayload.StartsWith(Version, StringComparison.Ordinal))
                throw new CryptographicException("Unknown payload format.");

            var data = Convert.FromBase64String(protectedPayload.Substring(Version.Length));
            if (data.Length < NonceSize + TagSize)
                throw new CryptographicException("Payload is too short.");

            var nonce = data.AsSpan(0, NonceSize);
            var tag = data.AsSpan(NonceSize, TagSize);
            var cipher = data.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];

            using (var aes = new AesGcm(_key, TagSize))
                aes.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }
    }
}
