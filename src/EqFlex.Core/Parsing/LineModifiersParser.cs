using System.Buffers;
using System.Collections.Concurrent;

namespace EqFlex.Core.Parsing;

internal static class LineModifiersParser
{
    public const short None = -1;
    public const short Crit = 2;
    private const short Twincast = 1;
    private const short Lucky = 4;
    private const short Rampage = 8;
    private const short Strikethrough = 16;
    private const short Riposte = 32;
    private const short Assassinate = 64;
    private const short Headshot = 128;
    private const short Slay = 256;
    private const short Doublebow = 512;
    private const short Flurry = 1024;
    private const short Finishing = 2048;

    private static readonly ConcurrentDictionary<string, short> MaskCache = new();

    internal static bool IsAssassinate(int mask) => mask > -1 && (mask & Assassinate) != 0;
    internal static bool IsCrit(int mask) => mask > -1 && (mask & Crit) != 0;
    internal static bool IsDoubleBowShot(int mask) => mask > -1 && (mask & Doublebow) != 0;
    internal static bool IsFinishingBlow(int mask) => mask > -1 && (mask & Finishing) != 0;
    internal static bool IsFlurry(int mask) => mask > -1 && (mask & Flurry) != 0;
    internal static bool IsHeadshot(int mask) => mask > -1 && (mask & Headshot) != 0;
    internal static bool IsLucky(int mask) => mask > -1 && (mask & Lucky) != 0;
    internal static bool IsTwincast(int mask) => mask > -1 && (mask & Twincast) != 0;
    internal static bool IsSlayUndead(int mask) => mask > -1 && (mask & Slay) != 0;
    internal static bool IsRampage(int mask) => mask > -1 && (mask & Rampage) != 0;
    internal static bool IsRiposte(int mask) => mask > -1 && (mask & Riposte) != 0 && (mask & Strikethrough) == 0;
    internal static bool IsStrikethrough(int mask) => mask > -1 && (mask & Strikethrough) != 0;

    internal static short Parse(string? modifiers)
    {
        if (string.IsNullOrEmpty(modifiers)) return -1;
        if (!MaskCache.TryGetValue(modifiers, out var result))
        {
            result = BuildVector(modifiers);
            MaskCache[modifiers] = result;
        }
        return result;
    }

    internal static short BuildVector(string modifiers)
    {
        short result = 0;
        const int bufferSize = 64;
        var buffer = ArrayPool<char>.Shared.Rent(bufferSize);
        var spanPos = 0;

        var span = modifiers.AsSpan();
        var start = 0;
        for (var i = 0; i <= span.Length; i++)
        {
            if (i == span.Length || span[i] == ' ')
            {
                var wordLen = i - start;
                if (wordLen > 0)
                {
                    if (spanPos + wordLen >= buffer.Length)
                    {
                        var newBuf = ArrayPool<char>.Shared.Rent(Math.Max(buffer.Length * 2, spanPos + wordLen + 16));
                        Buffer.BlockCopy(buffer, 0, newBuf, 0, spanPos * sizeof(char));
                        ArrayPool<char>.Shared.Return(buffer);
                        buffer = newBuf;
                    }

                    span.Slice(start, wordLen).CopyTo(buffer.AsSpan(spanPos));
                    spanPos += wordLen;
                    var key = buffer.AsSpan(0, spanPos).ToString();

                    var known = key switch
                    {
                        "Lucky" or "Assassinate" or "Double Bow Shot" or "Finishing Blow" or
                        "Flurry" or "Headshot" or "Twincast" or "Rampage" or "Wild Rampage" or
                        "Riposte" or "Strikethrough" or "Slay Undead" or "Locked" or
                        "Critical" or "Deadly Strike" or "Crippling Blow" => true,
                        _ => false
                    };

                    if (known)
                    {
                        if (key is "Crippling Blow" or "Critical" or "Deadly Strike" or "Finishing Blow")
                            result |= Crit;

                        switch (key)
                        {
                            case "Lucky": result |= Lucky; break;
                            case "Assassinate": result |= Assassinate; break;
                            case "Double Bow Shot": result |= Doublebow; break;
                            case "Finishing Blow": result |= Finishing; break;
                            case "Flurry": result |= Flurry; break;
                            case "Headshot": result |= Headshot; break;
                            case "Twincast": result |= Twincast; break;
                            case "Rampage": case "Wild Rampage": result |= Rampage; break;
                            case "Riposte": result |= Riposte; break;
                            case "Strikethrough": result |= Strikethrough; break;
                            case "Slay Undead": result |= Slay; break;
                        }

                        spanPos = 0;
                    }
                    else
                    {
                        if (spanPos >= buffer.Length)
                        {
                            var newBuf = ArrayPool<char>.Shared.Rent(buffer.Length * 2);
                            Buffer.BlockCopy(buffer, 0, newBuf, 0, spanPos * sizeof(char));
                            ArrayPool<char>.Shared.Return(buffer);
                            buffer = newBuf;
                        }
                        buffer[spanPos++] = ' ';
                    }
                }
                start = i + 1;
            }
        }

        ArrayPool<char>.Shared.Return(buffer);
        return result;
    }
}
