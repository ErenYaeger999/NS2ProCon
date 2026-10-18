using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NS2Pro.Core;

/// <summary>Independent implementation of the pinned VIIPER auth-v2 wire contract.</summary>
public sealed class ViiperSecureStream : Stream
{
    private readonly Stream inner;
    private readonly ChaCha20Poly1305? encrypt, decrypt;
    private readonly NaCl.Core.ChaCha20Poly1305? managedEncrypt, managedDecrypt;
    private readonly SemaphoreSlim readGate = new(1), writeGate = new(1);
    private readonly uint sendDirection, receiveDirection;
    private ulong sent, received;
    private byte[] pending = [];
    private int offset, failed;
    public ViiperSecureStream(Stream inner, ReadOnlySpan<byte> sessionKey, bool server = false, bool forceManaged = false)
    {
        this.inner = inner;
        if (forceManaged || !ChaCha20Poly1305.IsSupported) { managedEncrypt = new(sessionKey.ToArray()); managedDecrypt = new(sessionKey.ToArray()); }
        else { encrypt = new(sessionKey); decrypt = new(sessionKey); }
        sendDirection = server ? 1u : 0u; receiveDirection = server ? 0u : 1u;
    }
    public static byte[] DeriveKey(string password) => Rfc2898DeriveBytes.Pbkdf2(password, "VIIPER-Key-v1"u8, 100_000, HashAlgorithmName.SHA256, 32);
    public static async Task<ViiperSecureStream> ConnectAsync(Stream stream, byte[] key, CancellationToken token)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        byte[] proof = HMACSHA256.HashData(key, "VIIPER-Auth-v2"u8.ToArray().Concat(nonce).ToArray());
        await stream.WriteAsync("eVI2\0"u8.ToArray().Concat(nonce).Concat(proof).ToArray(), token).ConfigureAwait(false);
        byte[] prefix = new byte[3]; await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        if (!prefix.AsSpan().SequenceEqual("OK\0"u8)) throw new IOException("增强后端认证失败；不会降级为明文连接。");
        byte[] remote = new byte[32]; await stream.ReadExactlyAsync(remote, token).ConfigureAwait(false);
        byte[] session = SHA256.HashData(key.Concat(remote).Concat(nonce).Concat("VIIPER-Session-v2"u8.ToArray()).ToArray());
        try { return new(stream, session); } finally { CryptographicOperations.ZeroMemory(session); }
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    {
        if (buffer.IsEmpty) return;
        await writeGate.WaitAsync(token).ConfigureAwait(false);
        try {
            if (Volatile.Read(ref failed) != 0 || sent == ulong.MaxValue || buffer.Length > 2 * 1024 * 1024 - 28) throw new IOException("增强认证通道已失效。");
            byte[] record = new byte[buffer.Length + 32];
            BinaryPrimitives.WriteUInt32BigEndian(record, (uint)(record.Length - 4));
            BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(4), sendDirection);
            BinaryPrimitives.WriteUInt64BigEndian(record.AsSpan(8), sent++);
            if (encrypt is not null) encrypt.Encrypt(record.AsSpan(4, 12), buffer.Span, record.AsSpan(16, buffer.Length), record.AsSpan(16 + buffer.Length, 16));
            else managedEncrypt!.Encrypt(record.AsSpan(4, 12), buffer.Span, record.AsSpan(16, buffer.Length), record.AsSpan(16 + buffer.Length, 16), ReadOnlySpan<byte>.Empty);
            await inner.WriteAsync(record, token).ConfigureAwait(false);
        } catch { Interlocked.Exchange(ref failed, 1); throw; }
        finally { writeGate.Release(); }
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        if (buffer.IsEmpty) return 0;
        await readGate.WaitAsync(token).ConfigureAwait(false);
        try {
            if (Volatile.Read(ref failed) != 0) throw new IOException("增强认证通道已失效。");
            while (offset == pending.Length) {
                byte[] header = new byte[4];
                int first = await inner.ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
                if (first == 0) return 0;
                await inner.ReadExactlyAsync(header.AsMemory(1), token).ConfigureAwait(false);
                uint size = BinaryPrimitives.ReadUInt32BigEndian(header);
                if (size is < 28 or > 2 * 1024 * 1024) throw new IOException("增强认证帧长度无效。");
                byte[] packet = new byte[size]; await inner.ReadExactlyAsync(packet, token).ConfigureAwait(false);
                if (BinaryPrimitives.ReadUInt32BigEndian(packet) != receiveDirection || received == ulong.MaxValue ||
                    BinaryPrimitives.ReadUInt64BigEndian(packet.AsSpan(4)) != received) throw new IOException("增强认证帧序号或方向无效。");
                byte[] plain = new byte[size - 28];
                if (decrypt is not null) decrypt.Decrypt(packet.AsSpan(0, 12), packet.AsSpan(12, plain.Length), packet.AsSpan(12 + plain.Length, 16), plain);
                else managedDecrypt!.Decrypt(packet.AsSpan(0, 12), packet.AsSpan(12, plain.Length), packet.AsSpan(12 + plain.Length, 16), plain, ReadOnlySpan<byte>.Empty);
                received++; pending = plain; offset = 0;
            }
            int take = Math.Min(buffer.Length, pending.Length - offset);
            pending.AsMemory(offset, take).CopyTo(buffer); offset += take; return take;
        } catch { Interlocked.Exchange(ref failed, 1); throw; }
        finally { readGate.Release(); }
    }
    protected override void Dispose(bool disposing) { if (disposing) { Interlocked.Exchange(ref failed, 1); inner.Dispose(); encrypt?.Dispose(); decrypt?.Dispose(); managedEncrypt?.Dispose(); managedDecrypt?.Dispose(); } base.Dispose(disposing); }
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
