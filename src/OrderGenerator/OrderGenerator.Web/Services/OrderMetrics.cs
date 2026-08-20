using System.Collections.Concurrent;

namespace OrderGenerator.Web.Services;

public class OrderMetrics
{
    private long _ordersSent;
    private long _ordersAccepted;
    private long _ordersRejected;
    private long _ordersTimeout;
    private long _ordersDuplicateBlocked;

    private readonly ConcurrentDictionary<string, DateTime> _recentOrders = new();

    public long OrdersSent => Interlocked.Read(ref _ordersSent);
    public long OrdersAccepted => Interlocked.Read(ref _ordersAccepted);
    public long OrdersRejected => Interlocked.Read(ref _ordersRejected);
    public long OrdersTimeout => Interlocked.Read(ref _ordersTimeout);
    public long OrdersDuplicateBlocked => Interlocked.Read(ref _ordersDuplicateBlocked);

    public void RecordSent() => Interlocked.Increment(ref _ordersSent);
    public void RecordAccepted() => Interlocked.Increment(ref _ordersAccepted);
    public void RecordRejected() => Interlocked.Increment(ref _ordersRejected);
    public void RecordTimeout() => Interlocked.Increment(ref _ordersTimeout);
    public void RecordDuplicateBlocked() => Interlocked.Increment(ref _ordersDuplicateBlocked);

    public object GetSnapshot()
    {
        return new
        {
            OrdersSent,
            OrdersAccepted,
            OrdersRejected,
            OrdersTimeout,
            OrdersDuplicateBlocked,
            Timestamp = DateTime.UtcNow
        };
    }
}
