namespace Hub.Application.Common.Interfaces;

public interface IBusinessMetrics
{
    void RecordCicloIngestao(string outcome);

    void RecordPrecosProcessados(string tipo, long quantidade);
}
