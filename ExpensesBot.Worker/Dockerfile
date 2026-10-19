# Build context: repository root
#   docker build -f ExpensesBot.Worker/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY ExpensesBot.Data/ExpensesBot.Data.csproj ExpensesBot.Data/
COPY ExpensesBot.ServiceDefaults/ExpensesBot.ServiceDefaults.csproj ExpensesBot.ServiceDefaults/
COPY ExpensesBot.Worker/ExpensesBot.Worker.csproj ExpensesBot.Worker/
RUN dotnet restore ExpensesBot.Worker/ExpensesBot.Worker.csproj

COPY ExpensesBot.Data/ ExpensesBot.Data/
COPY ExpensesBot.ServiceDefaults/ ExpensesBot.ServiceDefaults/
COPY ExpensesBot.Worker/ ExpensesBot.Worker/
RUN dotnet publish ExpensesBot.Worker/ExpensesBot.Worker.csproj -c Release -o /app --no-restore

# aspnet, not runtime: ServiceDefaults (OpenTelemetry, health checks) uses the ASP.NET Core framework.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir /data && chown "$APP_UID" /data
VOLUME /data

ENV ConnectionStrings__ExpensesDatabase="Data Source=/data/expenses.db"

COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "ExpensesBot.Worker.dll"]
