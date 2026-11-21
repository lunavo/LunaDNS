using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Tls;

public class TlsRecordSplitterTests
{
    private static readonly byte[] Hello = TestClientHello.Build("store.steampowered.com", paddingLength: 40);

    private static int BodyLength(ReadOnlyMemory<byte> record) => (record.Span[3] << 8) | record.Span[4];

    [Fact]
    public void Two_records_with_own_headers_and_original_body()
    {
        var cut = 50;

        var records = TlsRecordSplitter.Split(Hello, cut);

        Assert.Equal(2, records.Count);
        Assert.Equal(cut - 5, BodyLength(records[0]));
        Assert.Equal(Hello.Length - cut, BodyLength(records[1]));
        foreach (var record in records)
        {
            Assert.Equal(record.Length - 5, BodyLength(record));
            Assert.Equal(Hello.AsSpan(0, 3).ToArray(), record.Span[..3].ToArray()); // content type + version
        }
        var body = records.SelectMany(r => r.Span[5..].ToArray()).ToArray();
        Assert.Equal(Hello.AsSpan(5).ToArray(), body);
    }

    [Theory]
    [InlineData(5)]   // record 1 rỗng
    [InlineData(0)]
    public void Cut_inside_header_is_rejected(int cut) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TlsRecordSplitter.Split(Hello, cut));

    [Fact]
    public void Cut_at_end_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TlsRecordSplitter.Split(Hello, Hello.Length));

    [Fact]
    public void Input_that_is_not_one_whole_record_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => TlsRecordSplitter.Split(Hello.AsMemory(0, Hello.Length - 1), 10));
        Assert.Throws<ArgumentException>(() => TlsRecordSplitter.Split((byte[])[.. Hello, 1, 2], 10));
    }

    [Fact]
    public void Largest_record_splits_into_records_within_limit()
    {
        var record = new byte[5 + 16384];
        record[0] = 0x16;
        record[1] = 0x03;
        record[2] = 0x01;
        record[3] = 0x40; // 16384 = 0x4000
        record[4] = 0x00;

        var records = TlsRecordSplitter.Split(record, 6);

        Assert.All(records, r => Assert.InRange(BodyLength(r), 1, 16384));
        Assert.Equal(16384, records.Sum(BodyLength));
    }

    [Fact]
    public void Default_split_is_first_byte_without_records()
    {
        Assert.Equal(SplitMode.FirstByte, HelloSplit.Default.Mode);
        Assert.False(HelloSplit.Default.Records);
    }
}
