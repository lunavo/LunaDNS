namespace LunaDns.Core.Dns;

/// <summary>Nơi stub hỏi tiếp. Mọi máy chủ đều lỗi thì ném DohException; người gọi huỷ thì ném OperationCanceledException.</summary>
public interface IDnsUpstream
{
    Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken);
}
