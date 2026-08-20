using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OrderGenerator.Application.DTOs;
using OrderGenerator.Application.Services;
using OrderGenerator.Web.Services;

namespace OrderGenerator.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IOrderService _orderService;
    private readonly ExposureTracker _exposureTracker;
    private readonly IdempotencyStore _idempotencyStore;
    private readonly OrderMetrics _metrics;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IOrderService orderService,
        ExposureTracker exposureTracker,
        IdempotencyStore idempotencyStore,
        OrderMetrics metrics,
        ILogger<IndexModel> logger)
    {
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
        _exposureTracker = exposureTracker ?? throw new ArgumentNullException(nameof(exposureTracker));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public OrderDto Order { get; set; } = new();

    public new OrderResponseDto? Response { get; set; }
    public Dictionary<string, ExposureInfo> Exposures { get; set; } = new();
    public decimal ExposureLimit { get; set; }
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? PendingOrderId { get; set; }

    public async Task<IActionResult> OnGetAsync(string? orderId)
    {
        Exposures = _exposureTracker.GetAllExposures();
        ExposureLimit = _exposureTracker.GetLimit();

        if (!string.IsNullOrEmpty(orderId))
        {
            PendingOrderId = orderId;
            var status = await _orderService.GetOrderStatusAsync(orderId);
            if (status != null && status.Status != "Pending")
            {
                Response = new OrderResponseDto
                {
                    IsAccepted = status.Status == "Accepted",
                    ClOrdId = status.OrderId,
                    RejectReason = status.RejectReason,
                    Status = status.Status,
                    Timestamp = status.ProcessedAt ?? status.Timestamp
                };
            }
            else
            {
                Response = new OrderResponseDto
                {
                    ClOrdId = orderId,
                    Status = "Pending",
                    Timestamp = DateTime.Now
                };
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Exposures = _exposureTracker.GetAllExposures();
        ExposureLimit = _exposureTracker.GetLimit();
        IdempotencyKey = Request.Form["IdempotencyKey"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var cachedResponse = _idempotencyStore.GetResponse(IdempotencyKey);
        if (cachedResponse != null)
        {
            _logger.LogWarning("Duplicate order blocked by idempotency key {Key}", IdempotencyKey);
            _metrics.RecordDuplicateBlocked();
            return RedirectToPage(new { orderId = cachedResponse.ClOrdId });
        }

        _metrics.RecordSent();
        _logger.LogInformation("Sending order: {Symbol} {Side} {Qty} @ {Price}", Order.Symbol, Order.Side, Order.Quantity, Order.Price);

        Response = await _orderService.SendOrderAsync(Order);

        if (Response.Status == "Accepted")
        {
            _metrics.RecordAccepted();
            var sign = Order.Side == "Compra" ? 1m : -1m;
            var orderExposure = Order.Price * Order.Quantity * sign;
            _exposureTracker.UpdateExposure(Order.Symbol, orderExposure, Order.Quantity);
        }
        else if (Response.Status == "Rejected")
        {
            _metrics.RecordRejected();
        }

        _idempotencyStore.Store(IdempotencyKey, Response);

        return RedirectToPage(new { orderId = Response.ClOrdId });
    }

    public async Task<IActionResult> OnGetOrderStatusAsync(string orderId)
    {
        var status = await _orderService.GetOrderStatusAsync(orderId);
        return new JsonResult(status);
    }
}
