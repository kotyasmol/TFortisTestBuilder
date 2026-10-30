using System.Collections.Generic;
using System.Linq;

namespace TestBuilder.Services;

internal static class Code128Encoding
{
    // Code128 symbol bar/space widths, indexed by symbol value (0..106, stop).
    private static readonly string[] Patterns = ("212222 222122 222221 121223 121322 131222 122213 122312 132212 221213 221312 231212 " +
        "112232 122132 122231 113222 123122 123221 223211 221132 221231 213212 223112 312131 311222 321122 321221 " +
        "312212 322112 322211 212123 212321 232121 111323 131123 131321 112313 132113 132311 211313 231113 231311 " +
        "112133 112331 132131 113123 113321 133121 313121 211331 231131 213113 213311 213131 311123 311321 331121 " +
        "312113 312311 332111 314111 221411 431111 111224 111422 121124 121421 141122 141221 112214 112412 122114 " +
        "122411 142112 142211 241211 221114 413111 241112 134111 111242 121142 121241 114212 124112 124211 " +
        "411212 421112 421211 212141 214121 412121 111143 111341 131141 114113 114311 411113 411311 113141 " +
        "114131 311141 411131 211412 211214 211232 2331112").Split(' ');
    public static bool CanUseC(string text) => text.Length > 0 && text.Length % 2 == 0 && text.All(char.IsAsciiDigit);
    public static string? EncodeAuto(string text)
    {
        if (CanUseC(text)) return Encode(text, true);
        if (text.Length is < 5 or > 256 || !text.All(char.IsAsciiDigit)) return Encode(text, false);
        var codes = new List<int> { 105 };
        for (var i = 0; i < text.Length - 1; i += 2) codes.Add((text[i] - '0') * 10 + text[i + 1] - '0');
        codes.Add(100); // Switch from pairs (C) to the final digit (B).
        codes.Add(text[^1] - 32);
        return Finish(codes);
    }
    public static string? Encode(string text, bool subsetC)
    {
        if (text.Length is 0 or > 256 || text.Any(c => c is < ' ' or > '~') || (subsetC && !CanUseC(text))) return null;
        var codes = new List<int> { subsetC ? 105 : 104 };
        if (subsetC)
            for (var i = 0; i < text.Length; i += 2) codes.Add((text[i] - '0') * 10 + text[i + 1] - '0');
        else codes.AddRange(text.Select(c => c - 32));
        return Finish(codes);
    }
    private static string Finish(List<int> codes)
    {
        var checksum = codes[0];
        for (var i = 1; i < codes.Count; i++) checksum += i * codes[i];
        codes.Add(checksum % 103); codes.Add(106);
        return string.Concat(codes.Select(c => Patterns[c]));
    }
}
