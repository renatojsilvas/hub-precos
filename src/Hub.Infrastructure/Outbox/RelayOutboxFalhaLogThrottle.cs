namespace Hub.Infrastructure.Outbox;

public sealed class RelayOutboxFalhaLogThrottle
{
    private bool? _ultimoCicloOk;

    public bool DeveLogarFalha()
    {
        var deveLogar = _ultimoCicloOk != false;
        _ultimoCicloOk = false;
        return deveLogar;
    }

    public bool DeveLogarRecuperacao()
    {
        var deveLogar = _ultimoCicloOk == false;
        _ultimoCicloOk = true;
        return deveLogar;
    }
}
