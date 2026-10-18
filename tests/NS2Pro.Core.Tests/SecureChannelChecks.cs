using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using NS2Pro.Core;

internal static class SecureChannelChecks
{
    public static async Task<int> RunAsync()
    {
        int passed = 0;
        void Check(bool value, string text) { if (!value) throw new Exception("Secure channel: " + text); passed++; }
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        using var wire = new MemoryStream();
        using var sender = new ViiperSecureStream(wire, key);
        await sender.WriteAsync("first"u8.ToArray()); await sender.WriteAsync("second"u8.ToArray());
        byte[] encoded = wire.ToArray();
        using var managedWire = new MemoryStream();
        using var managedSender = new ViiperSecureStream(managedWire, key, forceManaged: true);
        await managedSender.WriteAsync("first"u8.ToArray()); await managedSender.WriteAsync("second"u8.ToArray());
        Check(managedWire.ToArray().AsSpan().SequenceEqual(encoded), "Windows 10 managed fallback matches native ciphertext, tags and direction counters");
        using var receiver = new ViiperSecureStream(new MemoryStream(encoded), key, server: true);
        byte[] plain = new byte[11]; await receiver.ReadExactlyAsync(plain);
        Check(plain.AsSpan().SequenceEqual("firstsecond"u8), "independent records reconstruct a continuous byte stream");
        using var managedReceiver = new ViiperSecureStream(new MemoryStream(encoded), key, server: true, forceManaged: true);
        byte[] managedPlain = new byte[11]; await managedReceiver.ReadExactlyAsync(managedPlain);
        Check(managedPlain.AsSpan().SequenceEqual(plain), "managed fallback verifies and decrypts native records");
        Check(await receiver.ReadAsync(new byte[1]) == 0, "clean record-boundary EOF is accepted");
        using var echo = new ViiperSecureStream(new MemoryStream(encoded), key);
        bool rejected = false; try { await echo.ReadExactlyAsync(new byte[5]); } catch (IOException) { rejected = true; }
        Check(rejected, "a reflected client record is rejected by the direction nonce");
        var tampered = encoded.ToArray(); tampered[20] ^= 1;
        using var damaged = new ViiperSecureStream(new MemoryStream(tampered), key, server: true);
        rejected = false; try { await damaged.ReadExactlyAsync(new byte[5]); } catch (CryptographicException) { rejected = true; }
        Check(rejected, "modified ciphertext cannot become controller input");
        rejected = false; try { await damaged.ReadExactlyAsync(new byte[5]); } catch (IOException) { rejected = true; }
        Check(rejected, "failed authenticated streams never resume in plaintext");
        using var managedDamaged = new ViiperSecureStream(new MemoryStream(tampered), key, server: true, forceManaged: true);
        rejected = false; try { await managedDamaged.ReadExactlyAsync(new byte[5]); } catch (CryptographicException) { rejected = true; }
        Check(rejected, "managed fallback rejects modified ciphertext");
        using var replay = new ViiperSecureStream(new MemoryStream(encoded[..37].Concat(encoded[..37]).ToArray()), key, server: true);
        await replay.ReadExactlyAsync(new byte[5]);
        rejected = false; try { await replay.ReadExactlyAsync(new byte[5]); } catch (IOException) { rejected = true; }
        Check(rejected, "replayed nonce counters are rejected");
        using var oversized = new ViiperSecureStream(new MemoryStream(new byte[] { 0xff, 0xff, 0xff, 0xff }), key, server: true);
        rejected = false; try { await oversized.ReadExactlyAsync(new byte[1]); } catch (IOException) { rejected = true; }
        Check(rejected, "untrusted record sizes are bounded before allocation");
        return passed;
    }
}
