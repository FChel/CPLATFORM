using System;
using System.Security.Cryptography;
using System.Text;

public static class NORMCrypto
{
    public static string Sha256(string value)
    {
        return Sha256(Encoding.UTF8.GetBytes(value ?? ""));
    }

    public static string Sha256(byte[] value)
    {
        using (SHA256 algorithm = SHA256.Create())
        {
            byte[] hash = algorithm.ComputeHash(value);
            StringBuilder text = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) { text.Append(hash[i].ToString("x2")); }
            return text.ToString();
        }
    }
}
