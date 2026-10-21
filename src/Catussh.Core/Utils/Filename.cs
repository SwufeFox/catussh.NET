// Port of _src/Foundation/CatusshModule/Sources/CatusshModule/Utils/String+Filename.swift
namespace Catussh.Core.Utils;

/// <summary>
/// Port of <c>String+Filename.swift</c> — shared filename validation, previously
/// duplicated in the macOS and iOS apps.
/// </summary>
public static class Filename
{
    /// <summary>
    /// The characters Swift builds for <c>isValidAsFilename</c>: <c>":"</c> and
    /// <c>"/"</c>, the newlines, <c>.illegalCharacters</c> and
    /// <c>.controlCharacters</c>, here as Unicode code points.
    /// </summary>
    private static readonly HashSet<int> InvalidCodePoints = BuildInvalidCodePoints();

    /// <summary>
    /// True when the string can name a file: non-empty and free of the characters
    /// listed in <see cref="InvalidCodePoints"/>.
    /// </summary>
    public static bool IsValidAsFilename(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return false;

        foreach (var rune in value.EnumerateRunes())
        {
            if (InvalidCodePoints.Contains(rune.Value)) return false;
        }

        return true;
    }

    private static HashSet<int> BuildInvalidCodePoints()
    {
        var invalid = new HashSet<int> { ':', '/' };

        // CharacterSet.newlines
        foreach (var codePoint in new[] { 0x000A, 0x000B, 0x000C, 0x000D, 0x0085, 0x2028, 0x2029 })
        {
            invalid.Add(codePoint);
        }

        // CharacterSet.illegalCharacters
        for (var codePoint = 0xFDD0; codePoint <= 0xFDEF; codePoint++) invalid.Add(codePoint);
        foreach (var codePoint in new[] { 0xFFFE, 0xFFFF, 0x1FFFE, 0x1FFFF, 0x2FFFE, 0x2FFFF, 0x3FFFE, 0x3FFFF,
                                          0x4FFFE, 0x4FFFF, 0x5FFFE, 0x5FFFF, 0x6FFFE, 0x6FFFF, 0x7FFFE, 0x7FFFF,
                                          0x8FFFE, 0x8FFFF, 0x9FFFE, 0x9FFFF, 0xAFFFE, 0xAFFFF, 0xBFFFE, 0xBFFFF,
                                          0xCFFFE, 0xCFFFF, 0xDFFFE, 0xDFFFF, 0xEFFFE, 0xEFFFF, 0xFFFFE, 0xFFFFF,
                                          0x10FFFE, 0x10FFFF })
        {
            invalid.Add(codePoint);
        }

        // CharacterSet.controlCharacters
        for (var codePoint = 0x0000; codePoint <= 0x001F; codePoint++) invalid.Add(codePoint);
        for (var codePoint = 0x007F; codePoint <= 0x009F; codePoint++) invalid.Add(codePoint);

        return invalid;
    }
}
