using OrderAccumulator.Domain.Entities;
using OrderAccumulator.Domain.Enums;
using Shared.Domain.ValueObjects;

namespace OrderAccumulator.Domain.Interfaces;

public interface IExposureRepository
{
    Money GetCurrentExposure(Symbol symbol);
    void UpdateExposure(Symbol symbol, Money orderValue);
    bool CanAcceptOrder(Symbol symbol, Money orderValue);
    Dictionary<Symbol, Money> GetAllExposures();
    Money GetLimit();
}
