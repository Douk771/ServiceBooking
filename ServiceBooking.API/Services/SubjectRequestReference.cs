using System.Security.Cryptography;

namespace ServiceBooking.API.Services;

/// <summary>US-74 (ARCHITECTURE_CYCLE5.md §50.1) — a human-readable reference the anonymous requester
/// can quote in follow-up correspondence. Random, not sequential/derived from anything about the
/// request — it must reveal nothing about the subject or how many requests exist.</summary>
public static class SubjectRequestReference
{
    // No 0/O/1/I/L — glyphs that are easy to misread when read aloud or typed from a screenshot.
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int Length = 6;

    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return "SR-" + new string(chars);
    }
}
