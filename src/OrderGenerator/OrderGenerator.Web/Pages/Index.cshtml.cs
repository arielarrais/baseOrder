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

    public IndexModel(IOrderService orderService, ExposureTracker exposureTracker)
    {
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
        _exposureTracker = exposureTracker ?? throw new ArgumentNullException(nameof(exposureTracker));
    }

    [BindProperty]
    public OrderDto Order { get; set; } = new();

    public new OrderResponseDto? Response { get; set; }
    public Dictionary<string, decimal> Exposures { get; set; } = new();
    public decimal ExposureLimit { get; set; }

    public void OnGet()
    {
        Exposures = _exposureTracker.GetAllExposures();
        ExposureLimit = _exposureTracker.GetLimit();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Exposures = _exposureTracker.GetAllExposures();
        ExposureLimit = _exposureTracker.GetLimit();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        Response = await _orderService.SendOrderAsync(Order);

        if (Response.IsAccepted)
        {
            var sign = Order.Side == "Compra" ? 1m : -1m;
            var orderExposure = Order.Price * Order.Quantity * sign;
            _exposureTracker.UpdateExposure(Order.Symbol, orderExposure);
        }

        Exposures = _exposureTracker.GetAllExposures();
        return Page();
    }
}
