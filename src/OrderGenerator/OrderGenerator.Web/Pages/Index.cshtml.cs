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
    public Dictionary<string, decimal> Exposures { get; set; } = new();
    public decimal ExposureLimit { get; set; }
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");

    public void OnGet()
    {
        Exposures = _exposureTracker.GetAllExposures();
        ExposureLimit = _exposureTracker.GetLimit();
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
            Response = cachedResponse;
            Exposures = _exposureTracker.GetAllExposures();
            return Page();
        }

        _metrics.RecordSent();
        _logger.LogInformation("Sending order: {Symbol} {Side} {Qty} @ {Price}", Order.Symbol, Order.Side, Order.Quantity, Order.Price);

        Response = await _orderService.SendOrderAsync(Order);

        if (Response.IsAccepted)
        {
            _metrics.RecordAccepted();
            var sign = Order.Side == "Compra" ? 1m : -1m;
            var orderExposure = Order.Price * Order.Quantity * sign;
            _exposureTracker.UpdateExposure(Order.Symbol, orderExposure);
        }
        else
        {
            _metrics.RecordRejected();
        }

        _idempotencyStore.Store(IdempotencyKey, Response);
        Exposures = _exposureTracker.GetAllExposures();
        return Page();
    }
}
