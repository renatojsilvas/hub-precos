using Hub.Application.Outbox;
using Hub.Domain.Common;
using Hub.Domain.Outbox;

namespace Hub.Application.Tests.Outbox;

internal sealed class FakeOutboxReadRepository : IOutboxReadRepository
{
    private readonly Queue<Result<IReadOnlyList<OutboxPendente>>> _pendentesRespostas;
    private readonly Result<IReadOnlyList<OutboxPendente>> _pendentesRespostaPadrao =
        Result<IReadOnlyList<OutboxPendente>>.Success(Array.Empty<OutboxPendente>());
    private readonly Result<BacklogOutbox> _backlogResultado;

    public FakeOutboxReadRepository(
        IEnumerable<Result<IReadOnlyList<OutboxPendente>>>? pendentesRespostas = null,
        Result<BacklogOutbox>? backlogResultado = null)
    {
        _pendentesRespostas = new Queue<Result<IReadOnlyList<OutboxPendente>>>(pendentesRespostas ?? []);
        _backlogResultado = backlogResultado ?? Result<BacklogOutbox>.Success(new BacklogOutbox(0, null));
    }

    public List<int> LimitesChamados { get; } = [];

    public int ChamadasObterPendentes { get; private set; }

    public bool BacklogChamado { get; private set; }

    public Task<Result<IReadOnlyList<OutboxPendente>>> ObterPendentesAsync(int limite, CancellationToken ct)
    {
        ChamadasObterPendentes++;
        LimitesChamados.Add(limite);

        var resposta = _pendentesRespostas.Count > 0 ? _pendentesRespostas.Dequeue() : _pendentesRespostaPadrao;
        return Task.FromResult(resposta);
    }

    public Task<Result<BacklogOutbox>> ObterBacklogAsync(CancellationToken ct)
    {
        BacklogChamado = true;
        return Task.FromResult(_backlogResultado);
    }
}
