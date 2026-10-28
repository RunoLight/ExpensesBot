# Build context: repository root
#   docker build -f ExpensesBot.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY ExpensesBot.Data/ExpensesBot.Data.csproj ExpensesBot.Data/
COPY ExpensesBot.ServiceDefaults/ExpensesBot.ServiceDefaults.csproj ExpensesBot.ServiceDefaults/
COPY ExpensesBot.Api/ExpensesBot.Api.csproj ExpensesBot.Api/
RUN dotnet restore ExpensesBot.Api/ExpensesBot.Api.csproj

COPY ExpensesBot.Data/ ExpensesBot.Data/
COPY ExpensesBot.ServiceDefaults/ ExpensesBot.ServiceDefaults/
COPY ExpensesBot.Api/ ExpensesBot.Api/
# .git is not in the build context: CI passes the commit so /version can report it.
ARG SOURCE_REVISION=
RUN dotnet publish ExpensesBot.Api/ExpensesBot.Api.csproj -c Release -o /app --no-restore     -p:SourceRevisionId=$SOURCE_REVISION

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SQLite database lives on a volume shared with the Worker.
RUN mkdir /data && chown "$APP_UID" /data
VOLUME /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__ExpensesDatabase="Data Source=/data/expenses.db"

COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ExpensesBot.Api.dll"]
