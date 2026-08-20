using OrderAccumulator.Application.Interfaces;
using OrderAccumulator.Domain.Enums;
using OrderAccumulator.Domain.Interfaces;
using Shared.Domain.ValueObjects;

namespace OrderAccumulator.Application.Services;

public class ExposureService : IExposureService
{
    private readonly IExposureRepository _repository;

    public ExposureService(IExposureRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Money GetCurrentExposure(Symbol symbol)
    {
        return _repository.GetCurrentExposure(symbol);
    }

    public bool CanAcceptOrder(Symbol symbol, Money orderValue)
    {
        return _repository.CanAcceptOrder(symbol, orderValue);
    }

    public void UpdateExposure(Symbol symbol, Money orderValue)
    {
        _repository.UpdateExposure(symbol, orderValue);
    }

    public Dictionary<Symbol, Money> GetAllExposures()
    {
        return _repository.GetAllExposures();
    }

    public Money GetLimit()
    {
        return _repository.GetLimit();
    }
}
