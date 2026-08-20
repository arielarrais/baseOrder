using OrderAccumulator.Domain.Enums;
using Shared.Domain.ValueObjects;

namespace OrderAccumulator.Application.Interfaces;

public interface IExposureService
{
    Money GetCurrentExposure(Symbol symbol);
    bool CanAcceptOrder(Symbol symbol, Money orderValue);
    void UpdateExposure(Symbol symbol, Money orderValue);
    Dictionary<Symbol, Money> GetAllExposures();
    Money GetLimit();
}
