using Xunit;

namespace AllSky.Imaging.Overlay.Tests;

/// <summary>
/// Buffer con byte sentinella prima e dopo la zona utile: se una scrittura esce dai limiti, la sentinella cambia.
/// </summary>
internal static class CanaryBuffer
{
    /// <summary>Numero di byte sentinella prima e dopo il buffer utile.</summary>
    public const int Pad = 16;

    private const byte Value = 0xAA;

    /// <summary>Alloca un array con sentinelle attorno a una zona utile di <paramref name="size"/> byte.</summary>
    public static byte[] Allocate(int size)
    {
        var backing = new byte[Pad + size + Pad];
        Array.Fill(backing, Value);
        return backing;
    }

    /// <summary>Span della sola zona utile, senza le sentinelle.</summary>
    public static Span<byte> Payload(byte[] backing, int size) => backing.AsSpan(Pad, size);

    /// <summary>Verifica che le sentinelle prima e dopo la zona utile non siano state modificate.</summary>
    public static void AssertIntact(byte[] backing, int size)
    {
        for (int i = 0; i < Pad; i++)
        {
            Assert.Equal(Value, backing[i]);
        }

        for (int i = Pad + size; i < backing.Length; i++)
        {
            Assert.Equal(Value, backing[i]);
        }
    }
}
