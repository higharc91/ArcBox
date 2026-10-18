using System.Security.Cryptography;

namespace ArcBox.Server.Rooms;

public static class RoomCodes
{
    public const string Alphabet = "BCDFGHJKLMNPQRSTVWXZ";

    public static string Generate(Func<string, bool> isTaken)
    {
        while (true)
        {
            var chars = new char[4];
            for (int i = 0; i < 4; i++)
            {
                int index = RandomNumberGenerator.GetInt32(Alphabet.Length);
                chars[i] = Alphabet[index];
            }

            var code = new string(chars);
            if (!isTaken(code))
            {
                return code;
            }
        }
    }

    public static string Normalize(string input)
    {
        return input.Trim().ToUpperInvariant();
    }
}
