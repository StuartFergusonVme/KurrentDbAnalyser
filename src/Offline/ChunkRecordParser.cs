using System.Buffers.Binary;
using System.Text;

namespace ESAnalyser.Offline;

internal enum LogRecordType : byte
{
    Prepare = 0,
    Commit = 1,
    System = 2
}

internal sealed record PhysicalRecord(
    LogRecordType RecordType,
    string StreamName,
    string EventType,
    bool IsLinkedEvent,
    string? LinkTargetStream,
    int? LinkTargetEventNumber,
    DateTime TimestampUtc,
    int RecordSize,
    int PayloadSize);

internal readonly record struct AsciiStringSlice(int Offset, int Length, string Text);

internal static class ChunkRecordParser
{
    private const int ChunkHeaderSize = 128;
    private const int OpenRetryCount = 10;
    private const int OpenRetryDelayMs = 100;

    internal static IEnumerable<PhysicalRecord> ReadChunk(string path)
    {
        // Use shared access so a chunk can still be inspected while the database has it open.
        using var stream = OpenChunkStream(path);
        if (stream.Length <= ChunkHeaderSize)
        {
            yield break;
        }

        stream.Position = ChunkHeaderSize;
        var sizeBuffer = new byte[sizeof(int)];

        while (stream.Position + sizeof(int) <= stream.Length)
        {
            if (stream.Read(sizeBuffer, 0, sizeBuffer.Length) != sizeBuffer.Length)
            {
                yield break;
            }

            var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(sizeBuffer);
            if (bodyLength <= 0 || stream.Position + bodyLength + sizeof(int) > stream.Length)
            {
                yield break;
            }

            var body = new byte[bodyLength];
            if (stream.Read(body, 0, bodyLength) != bodyLength)
            {
                yield break;
            }

            if (stream.Read(sizeBuffer, 0, sizeBuffer.Length) != sizeBuffer.Length)
            {
                yield break;
            }

            var suffixLength = BinaryPrimitives.ReadInt32LittleEndian(sizeBuffer);
            if (suffixLength != bodyLength)
            {
                yield break;
            }

            if (TryParseRecord(body, out var parsed))
            {
                yield return parsed;
            }
        }
    }

    private static FileStream OpenChunkStream(string path)
    {
        Exception? lastError = null;
        var delayMs = OpenRetryDelayMs;

        for (var attempt = 1; attempt <= OpenRetryCount; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.SequentialScan);
            }
            catch (IOException ex)
            {
                lastError = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
            }

            if (attempt < OpenRetryCount)
            {
                Thread.Sleep(delayMs);
                delayMs = Math.Min(delayMs * 2, 1000);
            }
        }

        throw new IOException($"Unable to open chunk file '{path}' after {OpenRetryCount} attempts.", lastError);
    }

    private static bool TryParseRecord(ReadOnlySpan<byte> body, out PhysicalRecord parsed)
    {
        parsed = default!;
        if (body.Length < 10)
        {
            return false;
        }

        var recordType = (LogRecordType)body[0];
        if (recordType != LogRecordType.Prepare)
        {
            return false;
        }

        var version = body[1];
        if (version > 1)
        {
            return false;
        }

        var offset = 10;
        if (offset + sizeof(ushort) + sizeof(long) + sizeof(int) + sizeof(long) > body.Length)
        {
            return false;
        }

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        _ = BinaryPrimitives.ReadInt64LittleEndian(body.Slice(offset, sizeof(long)));
        offset += sizeof(long);
        _ = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(offset, sizeof(int)));
        offset += sizeof(int);

        if (version == 0)
        {
            if (offset + sizeof(int) > body.Length)
            {
                return false;
            }

            offset += sizeof(int);
        }
        else
        {
            if (offset + sizeof(long) > body.Length)
            {
                return false;
            }

            offset += sizeof(long);
        }

        if (!TryReadString(body, ref offset, out var streamName) || string.IsNullOrWhiteSpace(streamName))
        {
            return false;
        }

        if (offset + 16 + 16 + 8 > body.Length)
        {
            return false;
        }

        offset += 16; // event id
        offset += 16; // correlation id
        var timestampTicks = BinaryPrimitives.ReadInt64LittleEndian(body.Slice(offset, sizeof(long)));
        var timestampUtc = new DateTime(timestampTicks, DateTimeKind.Utc);
        offset += 8; // timestamp

        if (!TryReadString(body, ref offset, out var eventType))
        {
            return false;
        }

        if (offset + sizeof(int) > body.Length)
        {
            return false;
        }

        var dataLength = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(offset, sizeof(int)));
        if (dataLength < 0)
        {
            return false;
        }

        offset += sizeof(int);
        if (offset + dataLength + sizeof(int) > body.Length)
        {
            return false;
        }

        var data = body.Slice(offset, dataLength);
        offset += dataLength;

        var metadataLength = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(offset, sizeof(int)));
        if (metadataLength < 0)
        {
            return false;
        }

        offset += sizeof(int);
        if (offset + metadataLength > body.Length)
        {
            return false;
        }

        if (offset + metadataLength != body.Length)
        {
            return false;
        }

        var payloadSize = dataLength + metadataLength;
        var isLinked = (flags & 0x01) != 0 || streamName.StartsWith("$ce-", StringComparison.Ordinal) || streamName.StartsWith("$et-", StringComparison.Ordinal) || eventType == "$>";
        string? linkTargetStream = null;
        int? linkTargetEventNumber = null;

        if (isLinked)
        {
            var linkText = Encoding.UTF8.GetString(data);
            if (TryParseLinkTarget(linkText, out var parsedEventNumber, out var parsedStream))
            {
                linkTargetEventNumber = parsedEventNumber;
                linkTargetStream = parsedStream;
            }
        }

        parsed = new PhysicalRecord(recordType, streamName, eventType, isLinked, linkTargetStream, linkTargetEventNumber, timestampUtc, body.Length, payloadSize);
        return true;
    }

    private static bool TryReadString(ReadOnlySpan<byte> body, ref int offset, out string text)
    {
        text = string.Empty;
        if (!TryRead7BitEncodedInt(body, ref offset, out var length))
        {
            return false;
        }

        if (length < 0 || offset + length > body.Length)
        {
            return false;
        }

        text = Encoding.UTF8.GetString(body.Slice(offset, length));
        offset += length;
        return true;
    }

    private static bool TryRead7BitEncodedInt(ReadOnlySpan<byte> body, ref int offset, out int value)
    {
        value = 0;
        var shift = 0;

        while (true)
        {
            if (offset >= body.Length || shift == 35)
            {
                return false;
            }

            var b = body[offset++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }
    }

    private static bool TryParseLinkTarget(string text, out int eventNumber, out string streamName)
    {
        eventNumber = default;
        streamName = string.Empty;

        var separator = text.IndexOf('@');
        if (separator <= 0 || separator >= text.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(text[..separator], out eventNumber))
        {
            return false;
        }

        streamName = text[(separator + 1)..];
        return !string.IsNullOrWhiteSpace(streamName);
    }

}
