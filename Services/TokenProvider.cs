using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DropCast.Services
{
    /// <summary>
    /// Encrypts/decrypts the Discord bot token using AES-256-CBC.
    /// The key is derived from a fixed passphrase — this is obfuscation to avoid
    /// storing the token in plain text, not cryptographic security.
    /// Both the Android and desktop apps use the same scheme so a token.enc
    /// file is interchangeable between them.
    /// </summary>
    public static class TokenProvider
    {
        private static readonly byte[] Salt =
            { 0xD4, 0xC7, 0x2A, 0x8B, 0x5E, 0x91, 0x3F, 0x06, 0xA2, 0x7D, 0xE8, 0x14, 0xBB, 0x60, 0xF3, 0x59 };

        private const string Passphrase = "DropCast-2025-meme-overlay";
        private const int Iterations = 100000;
        private const int KeyBytes = 32;  // 256-bit
        private const int IvBytes = 16;   // 128-bit block

        private static byte[] DeriveKey()
        {
            using (var kdf = new Rfc2898DeriveBytes(Passphrase, Salt, Iterations, HashAlgorithmName.SHA256))
            {
                return kdf.GetBytes(KeyBytes);
            }
        }

        /// <summary>Encrypt a token and write it to <paramref name="filePath"/>.</summary>
        public static void SaveEncrypted(string token, string filePath)
        {
            byte[] data = Encrypt(token);
            File.WriteAllBytes(filePath, data);
        }

        /// <summary>Read and decrypt a token from <paramref name="filePath"/>, or return null if missing or corrupted.</summary>
        public static string LoadEncrypted(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                if (data.Length <= IvBytes) return null;
                return Decrypt(data);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        public static byte[] Encrypt(string plainText)
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = KeyBytes * 8;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = DeriveKey();
                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] plain = Encoding.UTF8.GetBytes(plainText);
                    byte[] cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

                    // File format: [16-byte IV][ciphertext]
                    byte[] result = new byte[aes.IV.Length + cipher.Length];
                    Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                    Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);
                    return result;
                }
            }
        }

        public static string Decrypt(byte[] encryptedData)
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = KeyBytes * 8;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = DeriveKey();

                byte[] iv = new byte[IvBytes];
                Buffer.BlockCopy(encryptedData, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                {
                    int cipherLen = encryptedData.Length - iv.Length;
                    byte[] cipher = new byte[cipherLen];
                    Buffer.BlockCopy(encryptedData, iv.Length, cipher, 0, cipherLen);
                    byte[] plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                    return Encoding.UTF8.GetString(plain);
                }
            }
        }
    }
}
