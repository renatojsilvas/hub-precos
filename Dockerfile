FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/*/*.csproj ./src-projects/
RUN for f in src-projects/*.csproj; do \
      [ -f "$f" ] || continue; \
      name=$(basename $f .csproj); \
      dir="src/$name"; \
      mkdir -p "$dir" && mv "$f" "$dir/"; \
    done && \
    rm -rf src-projects
# PublishReadyToRun exige RID explicito, e o RID tem que ser o da arquitetura de
# DESTINO. Fixar linux-x64 producia R2R x64 dentro de uma imagem arm64 em qualquer
# maquina Apple Silicon: o runtime rejeita o assembly inteiro (FileLoadException,
# "Could not load file or assembly") porque o machine type do PE nao bate. A VPS e
# amd64 e o CI tambem, entao o defeito passava despercebido nos dois e so quebrava
# o desenvolvimento local — inclusive o docker-compose.e2e.yml, que builda daqui.
# TARGETARCH e preenchido pelo BuildKit; so o nome difere do RID do .NET (amd64/x64).
ARG TARGETARCH
RUN RID="linux-$(test "$TARGETARCH" = amd64 && echo x64 || echo "$TARGETARCH")" && \
    echo "RID=$RID" && \
    dotnet restore src/Hub.API/Hub.API.csproj -r "$RID" -p:PublishReadyToRun=true
COPY src/ src/
RUN RID="linux-$(test "$TARGETARCH" = amd64 && echo x64 || echo "$TARGETARCH")" && \
    dotnet publish src/Hub.API/Hub.API.csproj -c Release -r "$RID" --self-contained false -p:PublishReadyToRun=true -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hub.API.dll"]
