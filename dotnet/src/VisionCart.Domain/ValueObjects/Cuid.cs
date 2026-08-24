using System.Security.Cryptography;

namespace VisionCart.Domain.ValueObjects;

/// <summary>
/// Collision-resistant, monotonically-increasing string identifier, compatible in
/// shape with the <c>cuid()</c> values the original Prisma schema generated.
///
/// Why not <see cref="Guid"/>: every primary key in the legacy database is a
/// 25-character cuid string, and order lines, audit rows and prescription
/// snapshots reference those values. Keeping the same identifier format means an
/// existing production dataset can be lifted into SQL Server unchanged, and any
/// external system holding a VisionCart id keeps working.
///
/// Format: 'c' + timestamp(base36) + counter(base36, 4) + fingerprint(4) + random(8).
/// The leading timestamp keeps values roughly sequential, which matters because
/// these are clustered primary keys in SQL Server — random GUID-like keys would
/// fragment every index in the database.
/// </summary>
public static class Cuid
{
    private const int BlockSize = 4;
    private const int Base = 36;
    private static readonly int Discrete = (int)Math.Pow(Base, BlockSize); // 1,679,616

    private static int _counter;
    private static readonly string Fingerprint = BuildFingerprint();

    public static string NewId()
    {
        var timestamp = ToBase36(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var counter = Pad(ToBase36(NextCounter()), BlockSize);
        var random = Pad(ToBase36(NextRandom()), BlockSize) + Pad(ToBase36(NextRandom()), BlockSize);
        return "c" + timestamp + counter + Fingerprint + random;
    }

    private static int NextCounter()
    {
        // Wraps at Discrete so the block never exceeds BlockSize characters.
        var next = Interlocked.Increment(ref _counter);
        return ((next % Discrete) + Discrete) % Discrete;
    }

    private static long NextRandom() => RandomNumberGenerator.GetInt32(Discrete);

    /// <summary>
    /// Distinguishes two processes writing to the same database, so a
    /// web-farm deployment cannot mint duplicate ids from identical clocks.
    /// </summary>
    private static string BuildFingerprint()
    {
        var pid = Environment.ProcessId % Discrete;
        var host = Environment.MachineName;
        var hostSum = host.Sum(c => (int)c) + host.Length + Base;
        return Pad(ToBase36(pid), 2)[^2..] + Pad(ToBase36(hostSum), 2)[^2..];
    }

    private static string ToBase36(long value)
    {
        if (value == 0) return "0";
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        var buffer = new Stack<char>();
        while (value > 0)
        {
            buffer.Push(alphabet[(int)(value % Base)]);
            value /= Base;
        }
        return new string([.. buffer]);
    }

    private static string Pad(string value, int size) =>
        value.Length >= size ? value[^size..] : value.PadLeft(size, '0');
}
