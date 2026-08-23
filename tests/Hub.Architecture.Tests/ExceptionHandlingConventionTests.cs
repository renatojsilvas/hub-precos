using System.Text.RegularExpressions;

namespace Hub.Architecture.Tests;

public sealed partial class ExceptionHandlingConventionTests
{
    private static readonly string RepoRoot = LocalizarRaizDoRepo();

    [Fact]
    public void DomainEApplication_NaoDevemConterCatch()
    {
        var arquivosDomain = ListarArquivosCs(Path.Combine(RepoRoot, "src", "Hub.Domain"));
        var arquivosApplication = ListarArquivosCs(Path.Combine(RepoRoot, "src", "Hub.Application"));
        var arquivosInfrastructure = ListarArquivosCs(Path.Combine(RepoRoot, "src", "Hub.Infrastructure"));

        Assert.True(
            arquivosDomain.Count > 5,
            $"esta asserção só é uma convenção real se houver uma quantidade plausível de arquivos .cs " +
            $"em Hub.Domain para inspecionar; encontrados: {arquivosDomain.Count} em '{RepoRoot}'");
        Assert.True(
            arquivosApplication.Count > 5,
            $"esta asserção só é uma convenção real se houver uma quantidade plausível de arquivos .cs " +
            $"em Hub.Application para inspecionar; encontrados: {arquivosApplication.Count} em '{RepoRoot}'");
        Assert.True(
            arquivosInfrastructure.Count > 5,
            $"esta asserção só é uma convenção real se houver uma quantidade plausível de arquivos .cs " +
            $"em Hub.Infrastructure para inspecionar; encontrados: {arquivosInfrastructure.Count} em '{RepoRoot}'");

        var catchesDomain = EncontrarOcorrenciasDeCatch(arquivosDomain);
        var catchesApplication = EncontrarOcorrenciasDeCatch(arquivosApplication);
        var catchesInfrastructure = EncontrarOcorrenciasDeCatch(arquivosInfrastructure);

        Assert.True(
            catchesDomain.Count == 0,
            "Hub.Domain não deve conter 'catch' (tratamento de exceção pertence à Infrastructure, onde a " +
            "exceção nasce). Ocorrências encontradas:\n" + string.Join('\n', catchesDomain));

        Assert.True(
            catchesApplication.Count == 0,
            "Hub.Application não deve conter 'catch' (tratamento de exceção pertence à Infrastructure, onde a " +
            "exceção nasce). Ocorrências encontradas:\n" + string.Join('\n', catchesApplication));

        // Controle positivo (PADROES §10.8): sem isto, o dia em que o scanner quebrar (caminho errado,
        // pasta renomeada) as duas asserções negativas acima ficariam verdes sem checar nada de verdade.
        Assert.True(
            catchesInfrastructure.Count > 0,
            "Hub.Infrastructure legitimamente contém 'catch' (clients HTTP, repositórios, DbContext). " +
            "Se esta asserção falhar, o scanner de 'catch' parou de enxergar ocorrências reais e as " +
            "asserções negativas acima viraram vacuidade.");
    }

    private static List<string> EncontrarOcorrenciasDeCatch(IReadOnlyList<string> arquivos)
    {
        var ocorrencias = new List<string>();

        foreach (var arquivo in arquivos)
        {
            var linhas = File.ReadAllLines(arquivo);
            for (var i = 0; i < linhas.Length; i++)
            {
                if (PalavraCatch().IsMatch(linhas[i]))
                {
                    ocorrencias.Add($"{arquivo}:{i + 1}: {linhas[i].Trim()}");
                }
            }
        }

        return ocorrencias;
    }

    private static List<string> ListarArquivosCs(string diretorio)
    {
        if (!Directory.Exists(diretorio))
        {
            return [];
        }

        return Directory.EnumerateFiles(diretorio, "*.cs", SearchOption.AllDirectories)
            .Where(caminho => !ContemSegmentoBinOuObj(caminho))
            .ToList();
    }

    private static bool ContemSegmentoBinOuObj(string caminho)
    {
        var segmentos = caminho.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segmentos.Any(s => s is "bin" or "obj");
    }

    private static string LocalizarRaizDoRepo()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);

        while (diretorio is not null && !File.Exists(Path.Combine(diretorio.FullName, "Hub.sln")))
        {
            diretorio = diretorio.Parent;
        }

        if (diretorio is null)
        {
            throw new InvalidOperationException(
                $"Não foi possível localizar a raiz do repo (Hub.sln) subindo a partir de '{AppContext.BaseDirectory}'.");
        }

        return diretorio.FullName;
    }

    [GeneratedRegex(@"\bcatch\b")]
    private static partial Regex PalavraCatch();
}
